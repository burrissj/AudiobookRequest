using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>
/// Periodically runs LibraryCatalogService.RefreshAsync to keep the "already in your library" check
/// current. Like ListeningHistoryRefreshBackgroundService, runs once immediately on startup (not just
/// after the first interval) -- otherwise the whole point of this cache (not re-requesting owned books)
/// is useless for up to 6 hours after a fresh install. If Jellyfin isn't configured yet (fresh install,
/// setup not finished) it retries every minute rather than waiting out a full interval.
/// </summary>
public class LibraryCatalogRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<LibraryCatalogRefreshBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Library catalog refresh";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan NotConfiguredRetry = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var ran = await RunOnceAsync(stoppingToken);
                if (ran)
                {
                    break;
                }
                await Task.Delay(NotConfiguredRetry, stoppingToken);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <returns>false when Jellyfin isn't configured yet and the caller should retry soon.</returns>
    private async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var ran = await scope.ServiceProvider.GetRequiredService<LibraryCatalogService>().RefreshAsync(ct);
            if (ran)
            {
                jobStatus.Success(JobName);
            }
            return ran;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (ExternalCallLog.Describe(ex) is null)
            {
                logger.LogError(ex, "Library catalog refresh failed");
            }
            else
            {
                logger.LogFailure(ex, "Library catalog refresh failed");   // Jellyfin unreachable: one short line, not a stack trace
            }
            jobStatus.Failure(JobName, ex);
            return true;
        }
    }
}
