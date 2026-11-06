using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>Periodically hands any request Chaptarr has finished importing (Status == Processing) off to BookProcessingService.</summary>
public class LibraryProcessingBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<LibraryProcessingBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Book processing";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processingService = scope.ServiceProvider.GetRequiredService<BookProcessingService>();
                await processingService.ProcessReadyRequestsAsync(stoppingToken);
                jobStatus.Success(JobName);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Library processing tick failed");
                jobStatus.Failure(JobName, ex);
            }
        }
    }
}
