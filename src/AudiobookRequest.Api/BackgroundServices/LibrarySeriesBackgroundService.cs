using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>
/// Works through the Jellyfin library asking Audible which series each book is in (LibrarySeriesService), a few books at a
/// time with a pause between batches, so a first pass over a big library is gentle on Audible. Once everything has been asked
/// it checks again every few minutes for new books. The library list it works from is refreshed first when it is more than
/// half an hour old, so books added to Jellyfin are picked up without waiting for the six-hourly catalog refresh.
/// </summary>
public class LibrarySeriesBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<LibrarySeriesBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Library series lookup";
    private const int BatchSize = 15;
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan BetweenBatches = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WhenIdle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WhenFailing = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CatalogStale = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var (settled, failed) = await RunOnceAsync(stoppingToken);
                await Task.Delay(failed ? WhenFailing : settled > 0 ? BetweenBatches : WhenIdle, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task<(int Settled, bool Failed)> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var catalog = scope.ServiceProvider.GetRequiredService<LibraryCatalogService>();
            var status = await catalog.GetStatusAsync(ct);
            if (!status.JellyfinConfigured)
            {
                return (0, false);
            }
            if (status.LastRefreshedAt is null || DateTimeOffset.UtcNow - status.LastRefreshedAt > CatalogStale)
            {
                await catalog.RefreshAsync(ct);
            }

            var settled = await scope.ServiceProvider.GetRequiredService<LibrarySeriesService>().EnrichBatchAsync(BatchSize, ct);
            jobStatus.Success(JobName);
            return (settled, false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (ExternalCallLog.Describe(ex) is null)
            {
                logger.LogError(ex, "Library series lookup failed");
            }
            else
            {
                logger.LogFailure(ex, "Library series lookup failed");
            }
            jobStatus.Failure(JobName, ex);
            return (0, true);
        }
    }
}
