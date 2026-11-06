using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>Periodically runs ChaptarrOrchestrationService.RunOnceAsync -- see that class for the actual logic.</summary>
public class ChaptarrOrchestrationBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<ChaptarrOrchestrationBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Chaptarr status polling";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ChaptarrOrchestrationService>().RunOnceAsync(stoppingToken);
                jobStatus.Success(JobName);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Chaptarr orchestration tick failed");
                jobStatus.Failure(JobName, ex);
            }
        }
    }
}
