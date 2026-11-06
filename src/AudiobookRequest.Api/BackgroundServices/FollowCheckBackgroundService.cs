using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>
/// Checks every followed author and series for new books. Waits a couple of minutes after startup so it
/// doesn't compete with the other first-run jobs, then repeats every six hours.
/// </summary>
public class FollowCheckBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<FollowCheckBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Follow checks";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // the service was stopped before the first check was due
        }
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<FollowService>().CheckAllAsync(stoppingToken);
                jobStatus.Success(JobName);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Follow checks failed");
                jobStatus.Failure(JobName, ex);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
