using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>
/// Drives each BookRequest from Requested -> Searching -> Downloading -> Processing by talking to Readarr:
/// adds/find the author, locates the specific book, monitors + searches it, then watches the queue/history
/// for progress. Runs far more often than the library scan interval since it's tracking active requests,
/// not sweeping the filesystem (that's Phase 3's job).
/// </summary>
public class ReadarrOrchestrationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReadarrOrchestrationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AddBookTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Readarr orchestration tick failed");
            }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var readarr = scope.ServiceProvider.GetRequiredService<IReadarrClient>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationSender>();

        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ReadarrUrl) || string.IsNullOrWhiteSpace(settings.ReadarrApiKey))
        {
            return;
        }

        var activeStatuses = new[] { BookRequestStatus.Requested, BookRequestStatus.Searching, BookRequestStatus.Downloading };
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
            await AdvanceRequestedAsync(request, settings, readarr, db, notifications, ct);
        }

        var inProgress = active.Where(r => r.Status is BookRequestStatus.Searching or BookRequestStatus.Downloading).ToList();
        if (inProgress.Count > 0)
        {
            await AdvanceInProgressAsync(inProgress, settings, readarr, db, notifications, ct);
        }
    }

    private async Task AdvanceRequestedAsync(
        BookRequest request,
        AppSettings settings,
        IReadarrClient readarr,
        AppDbContext db,
        INotificationSender notifications,
        CancellationToken ct)
    {
        try
        {
            var authorOptions = new ReadarrAddAuthorOptions(
                settings.ReadarrRootFolderPath!,
                settings.ReadarrQualityProfileId ?? 1,
                settings.ReadarrMetadataProfileId ?? 1);

            var authorId = await readarr.EnsureAuthorAsync(settings.ReadarrUrl!, settings.ReadarrApiKey!, request.ReadarrForeignAuthorId, authorOptions, ct);
            var bookId = await readarr.FindBookIdAsync(settings.ReadarrUrl!, settings.ReadarrApiKey!, authorId, request.ReadarrForeignBookId, ct);

            if (bookId is null)
            {
                if (DateTimeOffset.UtcNow - request.RequestedAt > AddBookTimeout)
                {
                    await FailAsync(request, "Readarr never populated this book after adding the author.", db, notifications, ct);
                }
                return;
            }

            await readarr.MarkBookMonitoredAsync(settings.ReadarrUrl!, settings.ReadarrApiKey!, bookId.Value, ct);
            await readarr.TriggerBookSearchAsync(settings.ReadarrUrl!, settings.ReadarrApiKey!, bookId.Value, ct);

            request.ReadarrBookId = bookId.Value;
            request.Status = BookRequestStatus.Searching;
            await db.SaveChangesAsync(ct);
            await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to advance request {RequestId} past Requested", request.Id);
            if (DateTimeOffset.UtcNow - request.RequestedAt > AddBookTimeout)
            {
                await FailAsync(request, "Repeated errors talking to Readarr while adding this book.", db, notifications, ct);
            }
        }
    }

    private async Task AdvanceInProgressAsync(
        List<BookRequest> requests,
        AppSettings settings,
        IReadarrClient readarr,
        AppDbContext db,
        INotificationSender notifications,
        CancellationToken ct)
    {
        IReadOnlyList<ReadarrQueueItem> queue;
        IReadOnlyList<ReadarrHistoryItem> history;
        try
        {
            queue = await readarr.GetQueueAsync(settings.ReadarrUrl!, settings.ReadarrApiKey!, ct);
            var earliestRequest = requests.Min(r => r.RequestedAt);
            history = await readarr.GetHistorySinceAsync(settings.ReadarrUrl!, settings.ReadarrApiKey!, earliestRequest, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to poll Readarr queue/history");
            return;
        }

        foreach (var request in requests)
        {
            var inQueue = queue.Any(q => q.BookId == request.ReadarrBookId);
            var imported = history.Any(h => h.BookId == request.ReadarrBookId &&
                (h.EventType == "bookFileImported" || h.EventType == "downloadFolderImported"));

            if (imported)
            {
                // Files have landed in Readarr's root folder. The Phase 3 library watcher takes it from here.
                request.Status = BookRequestStatus.Processing;
                await db.SaveChangesAsync(ct);
                await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
            }
            else if (inQueue && request.Status != BookRequestStatus.Downloading)
            {
                request.Status = BookRequestStatus.Downloading;
                await db.SaveChangesAsync(ct);
                await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
            }
            else if (!inQueue && DateTimeOffset.UtcNow - request.RequestedAt > DownloadTimeout)
            {
                await FailAsync(request, "No download progress after 24 hours.", db, notifications, ct);
            }
        }
    }

    private static async Task FailAsync(BookRequest request, string reason, AppDbContext db, INotificationSender notifications, CancellationToken ct)
    {
        request.Status = BookRequestStatus.Failed;
        request.FailureReason = reason;
        await db.SaveChangesAsync(ct);
        await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
    }
}
