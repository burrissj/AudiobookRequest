using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>
/// Backs up the database about once a day. Checks hourly whether the newest backup is older than 20 hours,
/// so restarting the service doesn't pile up extra copies and a machine that is off at night still gets one.
/// </summary>
public class DatabaseBackupBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<DatabaseBackupBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Database backup";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinAge = TimeSpan.FromHours(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // the service was stopped before the first backup was due
        }
        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var backups = scope.ServiceProvider.GetRequiredService<DatabaseBackupService>();
                if (await backups.IsDueAsync(MinAge, stoppingToken))
                {
                    await backups.CreateAsync(stoppingToken);
                }
                jobStatus.Success(JobName);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Database backup failed");
                jobStatus.Failure(JobName, ex);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
