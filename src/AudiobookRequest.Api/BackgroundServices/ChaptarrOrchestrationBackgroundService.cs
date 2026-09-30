using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>
/// Drives each BookRequest from Requested -> Searching -> Downloading -> Processing by talking to
/// Chaptarr: adds the author+book in one atomic call (Chaptarr, unlike Readarr, doesn't need a
/// separate "add author, wait for its bibliography to sync" step), triggers a search, then watches
/// the queue/history for progress. Runs far more often than the library scan interval since it's
/// tracking active requests, not sweeping the filesystem (that's the library watcher's job).
/// </summary>
public class ChaptarrOrchestrationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ChaptarrOrchestrationBackgroundService> logger) : BackgroundService
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
                logger.LogError(ex, "Chaptarr orchestration tick failed");
            }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var chaptarr = scope.ServiceProvider.GetRequiredService<IChaptarrClient>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationSender>();

        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
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
            await AdvanceRequestedAsync(request, settings, chaptarr, db, notifications, ct);
        }

        var inProgress = active.Where(r => r.Status is BookRequestStatus.Searching or BookRequestStatus.Downloading).ToList();
        if (inProgress.Count > 0)
        {
            await AdvanceInProgressAsync(inProgress, settings, chaptarr, db, notifications, ct);
        }
    }

    private async Task AdvanceRequestedAsync(
        BookRequest request,
        AppSettings settings,
        IChaptarrClient chaptarr,
        AppDbContext db,
        INotificationSender notifications,
        CancellationToken ct)
    {
        try
        {
            var rootFolder = await chaptarr.FindRootFolderAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, settings.ChaptarrRootFolderPath!, ct)
                ?? throw new InvalidOperationException($"Chaptarr has no root folder configured at '{settings.ChaptarrRootFolderPath}'.");

            var bookId = await chaptarr.AddBookAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, request.ChaptarrForeignBookId, rootFolder, ct);
            await chaptarr.TriggerBookSearchAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, bookId, ct);

            request.ChaptarrBookId = bookId;
            request.Status = BookRequestStatus.Searching;
            await db.SaveChangesAsync(ct);
            await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to advance request {RequestId} past Requested", request.Id);
            if (DateTimeOffset.UtcNow - request.RequestedAt > AddBookTimeout)
            {
                await FailAsync(request, "Repeated errors talking to Chaptarr while adding this book.", db, notifications, ct);
            }
        }
    }

    private async Task AdvanceInProgressAsync(
        List<BookRequest> requests,
        AppSettings settings,
        IChaptarrClient chaptarr,
        AppDbContext db,
        INotificationSender notifications,
        CancellationToken ct)
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
            var inQueue = queue.Any(q => q.BookId == request.ChaptarrBookId);
            var imported = history.Any(h => h.BookId == request.ChaptarrBookId &&
                (h.EventType == "bookFileImported" || h.EventType == "downloadFolderImported"));

            if (imported)
            {
                // Files have landed in Chaptarr's root folder. The library watcher takes it from here.
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
