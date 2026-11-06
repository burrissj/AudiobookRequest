using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>Periodically runs UpcomingRequestService.RunOnceAsync: probes books that aren't out yet and hands over the ones whose date has come.</summary>
public class UpcomingBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<UpcomingBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Upcoming releases check";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<UpcomingRequestService>().RunOnceAsync(stoppingToken);
                jobStatus.Success(JobName);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Upcoming releases check failed");
                jobStatus.Failure(JobName, ex);
            }
        }
    }
}
