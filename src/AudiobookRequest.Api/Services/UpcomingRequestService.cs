using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// The queue for books that aren't out yet. They stay out of the normal download flow (where a search can only fail)
/// and are looked at on a slow schedule instead -- adding the book to Chaptarr and searching, in case it appears early or
/// was mis-dated. When the release date arrives, the request is handed to the normal flow as a wishlist item, which then
/// searches far more often for the first days (see <see cref="ReleaseSchedule.RetryInterval"/>).
/// </summary>
public class UpcomingRequestService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    INotificationSender notifications,
    ILogger<UpcomingRequestService> logger)
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var upcoming = await db.BookRequests
            .Include(r => r.RequestedByUser)
            .Where(r => r.Status == BookRequestStatus.Upcoming)
            .ToListAsync(ct);
        if (upcoming.Count == 0)
        {
            return;
        }

        var settings = await settingsService.GetAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var request in upcoming.Where(r => !ReleaseSchedule.IsUnreleased(r.ReleaseDate, now)))
        {
            await ReleaseAsync(request, settings, ct);
        }

        var waiting = upcoming.Where(r => r.Status == BookRequestStatus.Upcoming).ToList();
        if (waiting.Count == 0 || !ChaptarrConfigured(settings))
        {
            return;
        }

        await PromoteEarlyGrabsAsync(waiting, settings, now, ct);

        foreach (var request in waiting.Where(r => r.Status == BookRequestStatus.Upcoming))
        {
            var due = ReleaseSchedule.NextUpcomingCheck(request.LastSearchAt, request.RequestedAt, request.ReleaseDate!.Value, now);
            if (due <= now)
            {
                await ProbeAsync(request, settings, ct);
            }
        }
    }

    /// <summary>The "check now" button: look for the book right away, or hand it on if its date has passed since the last tick.</summary>
    public async Task<RequestActionResult> CheckNowAsync(BookRequest request, AppSettings settings, CancellationToken ct)
    {
        if (!ReleaseSchedule.IsUnreleased(request.ReleaseDate, DateTimeOffset.UtcNow))
        {
            await ReleaseAsync(request, settings, ct);
            return RequestActionResult.Done(BookRequestDto.From(request));
        }

        if (!ChaptarrConfigured(settings))
        {
            return RequestActionResult.Invalid("Chaptarr isn't set up yet, so there is nothing to search with.");
        }

        await ProbeAsync(request, settings, ct);
        return RequestActionResult.Done(BookRequestDto.From(request));
    }

    private static bool ChaptarrConfigured(AppSettings s) =>
        !string.IsNullOrWhiteSpace(s.ChaptarrUrl) && !string.IsNullOrWhiteSpace(s.ChaptarrApiKey) && !string.IsNullOrWhiteSpace(s.ChaptarrRootFolderPath);

    /// <summary>One early look: make sure Chaptarr has the book, ask it to search, and note when we did.</summary>
    private async Task ProbeAsync(BookRequest request, AppSettings settings, CancellationToken ct)
    {
        try
        {
            if (request.ChaptarrBookId is null)
            {
                var rootFolder = await chaptarr.FindRootFolderAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, settings.ChaptarrRootFolderPath!, ct)
                    ?? throw new InvalidOperationException($"Chaptarr has no root folder configured at '{settings.ChaptarrRootFolderPath}'.");
                request.ChaptarrBookId = await ChaptarrBookAdder.AddAsync(db, chaptarr, settings, request, rootFolder, ct);
            }

            await chaptarr.TriggerBookSearchAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, request.ChaptarrBookId.Value, ct);
            request.DiagnosticMessage = null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Early check for upcoming request {RequestId} failed", request.Id);
            request.DiagnosticMessage = $"Couldn't check Chaptarr for this yet: {ExceptionText.Describe(ex)}";
        }

        // Stamped even on failure, so a down Chaptarr is retried on the normal schedule rather than every tick.
        request.LastSearchAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
    }

    /// <summary>The date has come: from here the request is an ordinary wishlisted one, searched often at first.</summary>
    private async Task ReleaseAsync(BookRequest request, AppSettings settings, CancellationToken ct)
    {
        request.Wishlisted = true;
        request.DiagnosticMessage = null;

        if (request.ChaptarrBookId is { } bookId && ChaptarrConfigured(settings))
        {
            request.Status = BookRequestStatus.Searching;
            try
            {
                await chaptarr.TriggerBookSearchAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, bookId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Release-day search for request {RequestId} failed; the normal wishlist search will retry", request.Id);
            }
            request.LastSearchAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Chaptarr has never seen it; the orchestration loop adds it and searches within seconds.
            request.Status = BookRequestStatus.Requested;
        }

        await db.SaveChangesAsync(ct);
        await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
    }

    /// <summary>If an indexer already has the book (or Chaptarr found it itself), stop waiting for the date and let it download.</summary>
    private async Task PromoteEarlyGrabsAsync(List<BookRequest> waiting, AppSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var tracked = waiting.Where(r => r.ChaptarrBookId is not null).ToList();
        if (tracked.Count == 0)
        {
            return;
        }

        IReadOnlyList<ChaptarrQueueItem> queue;
        IReadOnlyList<ChaptarrHistoryItem> history;
        try
        {
            queue = await chaptarr.GetQueueAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, ct);
            history = await chaptarr.GetHistorySinceAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, tracked.Min(r => r.RequestedAt), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't read Chaptarr's queue to see whether an upcoming book appeared early");
            return;
        }

        foreach (var request in tracked)
        {
            var found = queue.Any(q => q.BookId == request.ChaptarrBookId)
                || history.Any(h => h.BookId == request.ChaptarrBookId && h.EventType == "grabbed" && h.Date >= request.RequestedAt);
            if (!found)
            {
                continue;
            }

            // The normal loop takes it from here: it sees the queue entry or the grab and moves on to Downloading.
            request.Status = BookRequestStatus.Searching;
            request.Wishlisted = true;
            request.DiagnosticMessage = null;
            request.LastSearchAt = now;
            await db.SaveChangesAsync(ct);
            await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
        }
    }
}
