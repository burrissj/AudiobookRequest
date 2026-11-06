using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Drives each BookRequest from Requested -> Searching -> Downloading -> Processing by talking to
/// Chaptarr: adds the author+book in one atomic call (Chaptarr, unlike Readarr, doesn't need a
/// separate "add author, wait for its bibliography to sync" step), triggers a search, then watches
/// the queue/history for progress. Scoped so it can be driven both by the periodic background loop
/// (ChaptarrOrchestrationBackgroundService) and on-demand by the "Refresh" button in the UI.
/// </summary>
public class ChaptarrOrchestrationService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    INotificationSender notifications,
    IExternalNotifier externalNotifier,
    ImportFixService importFixer,
    ILogger<ChaptarrOrchestrationService> logger)
{
    private static readonly TimeSpan AddBookTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromHours(24);

    // History events that mean "something went wrong with this book's download/import".
    private static readonly string[] FailureEventTypes =
        ["downloadFailed", "bookImportIncomplete", "bookFileConversionFailed", "importFailed", "bookImportFailed"];

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
        {
            return;
        }

        var activeStatuses = new[] { BookRequestStatus.Requested, BookRequestStatus.Searching, BookRequestStatus.Snatched, BookRequestStatus.Downloading };
        var active = await db.BookRequests
            .Include(r => r.RequestedByUser)
            .Where(r => activeStatuses.Contains(r.Status))
            .ToListAsync(ct);

        if (active.Count == 0)
        {
            return;
        }

        var requested = active.Where(r => r.Status == BookRequestStatus.Requested).ToList();
        foreach (var request in requested)
        {
            await AdvanceRequestedAsync(request, settings, ct);
        }

        var inProgress = active.Where(r => r.Status is BookRequestStatus.Searching or BookRequestStatus.Snatched or BookRequestStatus.Downloading).ToList();
        if (inProgress.Count > 0)
        {
            await AdvanceInProgressAsync(inProgress, settings, ct);
        }
    }

    private async Task AdvanceRequestedAsync(BookRequest request, AppSettings settings, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.ChaptarrRootFolderPath))
            {
                throw new InvalidOperationException("The Chaptarr root folder path isn't set. Fill it in under Settings.");
            }

            var rootFolder = await chaptarr.FindRootFolderAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, settings.ChaptarrRootFolderPath!, ct)
                ?? throw new InvalidOperationException($"Chaptarr has no root folder configured at '{settings.ChaptarrRootFolderPath}'.");

            var bookId = await ChaptarrBookAdder.AddAsync(db, chaptarr, settings, request, rootFolder, ct);
            await chaptarr.TriggerBookSearchAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, bookId, ct);

            request.ChaptarrBookId = bookId;
            request.Status = BookRequestStatus.Searching;
            request.LastSearchAt = DateTimeOffset.UtcNow;
            request.DiagnosticMessage = null;
            await db.SaveChangesAsync(ct);
            await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to advance request {RequestId} past Requested", request.Id);
            var gone = ex is ChaptarrBookNotFoundException;
            if (DateTimeOffset.UtcNow - request.RequestedAt > AddBookTimeout && (!request.Wishlisted || gone))
            {
                // A listing Chaptarr no longer has will never be found by waiting (even when wishlisted), and only a failed
                // request offers "Retry with different search", so fail it rather than retry every pass forever.
                if (gone)
                {
                    await FailAsync(request, ex.Message + " Use \"Retry with different search\" to pick another listing.", settings, ct);
                    return;
                }

                await FailAsync(request, "Repeated errors talking to Chaptarr while adding this book.", settings, ct);
            }
            else
            {
                // Surface why it's stuck instead of leaving it silently "Requested".
                await SetDiagnosticAsync(request, $"Couldn't add this to Chaptarr yet: {ExceptionText.Describe(ex)}", ct);
            }
        }
    }

    private async Task AdvanceInProgressAsync(List<BookRequest> requests, AppSettings settings, CancellationToken ct)
    {
        IReadOnlyList<ChaptarrQueueItem> queue;
        IReadOnlyList<ChaptarrHistoryItem> history;
        try
        {
            queue = await chaptarr.GetQueueAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, ct);
            var earliestRequest = requests.Min(r => r.RequestedAt);
            history = await chaptarr.GetHistorySinceAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, earliestRequest, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to poll Chaptarr queue/history");
            return;
        }

        foreach (var request in requests)
        {
            var queueItem = queue.FirstOrDefault(q => q.BookId == request.ChaptarrBookId);
            var imported = history.Any(h => h.BookId == request.ChaptarrBookId &&
                (h.EventType == "bookFileImported" || h.EventType == "downloadFolderImported"));
            var grabbedAt = history
                .Where(h => h.BookId == request.ChaptarrBookId && h.EventType == "grabbed")
                .Select(h => (DateTimeOffset?)h.Date)
                .OrderBy(d => d)
                .FirstOrDefault();

            if (imported)
            {
                // Files have landed in Chaptarr's root folder. The library watcher takes it from here.
                request.Status = BookRequestStatus.Processing;
                request.DiagnosticMessage = null;
                await db.SaveChangesAsync(ct);
                await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
            }
            else if (queueItem is not null)
            {
                // Refresh progress every tick (not just on the first transition) so the UI's progress
                // bar actually moves instead of freezing at the value it had when it first entered the queue.
                request.Status = BookRequestStatus.Downloading;
                request.DownloadSizeBytes = queueItem.SizeBytes;
                request.DownloadSizeLeftBytes = queueItem.SizeLeftBytes;
                request.DownloadProgressPercent = queueItem.SizeBytes > 0
                    ? Math.Clamp(1 - queueItem.SizeLeftBytes / queueItem.SizeBytes, 0, 1)
                    : null;
                request.DownloadClientName = queueItem.DownloadClient;
                request.SnatchedAt ??= grabbedAt ?? DateTimeOffset.UtcNow;
                var queueProblem = DescribeQueueProblem(queueItem);
                // Chaptarr keeps reporting the block for a while after an automatic import starts; don't
                // overwrite the note saying what we did (or why it failed) with the stale block message.
                var previous = request.DiagnosticMessage;
                var keepAutoFixNote = queueProblem is not null && previous is not null &&
                    (previous.StartsWith("Auto-fix tried", StringComparison.Ordinal) ||
                     (previous.StartsWith("Auto-fixed", StringComparison.Ordinal) &&
                      request.AutoFixAttemptedAt is { } attempted && DateTimeOffset.UtcNow - attempted < TimeSpan.FromMinutes(10)));
                request.DiagnosticMessage = keepAutoFixNote ? previous : queueProblem;

                // Opt-in: import a download Chaptarr blocked only because it matched a sibling edition, once per attempt.
                if (settings.AutoFixImports && queueItem.TrackedDownloadState == "importBlocked" && request.AutoFixAttemptedAt is null)
                {
                    request.AutoFixAttemptedAt = DateTimeOffset.UtcNow;
                    var note = await importFixer.TryAutoFixAsync(request, settings, queueItem, ct);
                    if (note is not null)
                    {
                        request.DiagnosticMessage = note;
                    }
                }
                await db.SaveChangesAsync(ct);
                await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
            }
            else if (grabbedAt is not null && request.Status != BookRequestStatus.Snatched)
            {
                // Grabbed from an indexer but not showing in the queue yet (poll-timing gap between
                // the history event and the download client registering it).
                request.Status = BookRequestStatus.Snatched;
                request.SnatchedAt ??= grabbedAt;
                request.DiagnosticMessage = DescribeHistoryProblem(history, request, grabbedAt);
                await db.SaveChangesAsync(ct);
                await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
            }
            else if (grabbedAt is null)
            {
                await HandleNothingFoundAsync(request, settings, history, ct);
            }
            else
            {
                // Snatched but neither queued nor imported: usually a download that vanished or failed.
                var problem = DescribeHistoryProblem(history, request, grabbedAt);
                if (problem != request.DiagnosticMessage)
                {
                    await SetDiagnosticAsync(request, problem, ct);
                }
            }
        }
    }

    /// <summary>No release has been grabbed: re-search on the wishlist schedule, otherwise give up after 24h.</summary>
    private async Task HandleNothingFoundAsync(BookRequest request, AppSettings settings, IReadOnlyList<ChaptarrHistoryItem> history, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (request.Wishlisted && request.ChaptarrBookId is not null)
        {
            // A book that has just come out is searched far more often than the normal wishlist pace, since indexers
            // usually need a few hours to a few days to carry it; the pace eases back as the release gets older.
            var retryAfter = ReleaseSchedule.RetryInterval(request.ReleaseDate, now, settings.WishlistRetryHours);
            if (request.LastSearchAt is null || now - request.LastSearchAt > retryAfter)
            {
                try
                {
                    await chaptarr.TriggerBookSearchAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, request.ChaptarrBookId.Value, ct);
                    request.LastSearchAt = now;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Wishlist re-search failed for request {RequestId}", request.Id);
                }
            }

            var message = request.LastSearchAt is { } last
                ? $"Wishlisted: nothing found yet. Searching again around {last.Add(retryAfter).ToLocalTime():g}."
                : "Wishlisted: nothing found yet.";
            if (message != request.DiagnosticMessage || request.LastSearchAt == now)
            {
                request.DiagnosticMessage = message;
                await db.SaveChangesAsync(ct);
                await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
            }
            return;
        }

        if (now - request.RequestedAt > DownloadTimeout)
        {
            await FailAsync(request, "No download progress after 24 hours. No indexer had a matching release.", settings, ct);
            return;
        }

        var problem = DescribeHistoryProblem(history, request, grabbedAt: null)
            ?? (request.Status == BookRequestStatus.Searching ? "Searching your indexers; nothing matched yet." : null);
        if (problem != request.DiagnosticMessage)
        {
            await SetDiagnosticAsync(request, problem, ct);
        }
    }

    private static string? DescribeQueueProblem(ChaptarrQueueItem item)
    {
        var state = item.TrackedDownloadState ?? "";
        var bad = item.TrackedDownloadStatus is "warning" or "error"
            || state is "importBlocked" or "importFailed" or "failedPending" or "failed" or "downloadFailed";
        if (!bad)
        {
            return null;
        }

        var detail = item.ErrorMessage ?? item.Messages?.FirstOrDefault();
        var label = state switch
        {
            "importBlocked" => "Import blocked",
            "importFailed" => "Import failed",
            "failed" or "failedPending" or "downloadFailed" => "Download failed",
            _ => "Chaptarr reports a problem",
        };
        return detail is null ? label : $"{label}: {detail}";
    }

    private static string? DescribeHistoryProblem(IReadOnlyList<ChaptarrHistoryItem> history, BookRequest request, DateTimeOffset? grabbedAt)
    {
        var failure = history
            .Where(h => h.BookId == request.ChaptarrBookId && FailureEventTypes.Contains(h.EventType))
            .OrderByDescending(h => h.Date)
            .FirstOrDefault();
        if (failure is null)
        {
            return null;
        }

        var label = failure.EventType switch
        {
            "downloadFailed" => "Download failed",
            "bookImportIncomplete" => "Import incomplete",
            "bookFileConversionFailed" => "Conversion failed",
            _ => "Import failed",
        };
        return failure.Message is null ? label : $"{label}: {failure.Message}";
    }

    private async Task SetDiagnosticAsync(BookRequest request, string? message, CancellationToken ct)
    {
        if (request.DiagnosticMessage == message)
        {
            return;
        }
        request.DiagnosticMessage = message;
        await db.SaveChangesAsync(ct);
        await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
    }

    private async Task FailAsync(BookRequest request, string reason, AppSettings settings, CancellationToken ct)
    {
        request.Status = BookRequestStatus.Failed;
        request.FailureReason = reason;
        await db.SaveChangesAsync(ct);
        await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
        await externalNotifier.NotifyAsync(settings, ExternalNotifier.Failed(request, reason), ct);
    }
}
