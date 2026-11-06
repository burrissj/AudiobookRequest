using AudiobookRequest.Api.Services;
using System.Text.Json;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>Daily job: deletes the original files Chaptarr downloaded once their retention period has passed.</summary>
public class OriginalCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<OriginalCleanupBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Original file cleanup";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
                jobStatus.Success(JobName);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Original cleanup pass failed");
                jobStatus.Failure(JobName, ex);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var due = await db.ProcessedBooks
            .Where(p => !p.OriginalDeleted)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var processed in due.Where(p => p.OriginalCleanupAt <= now))
        {
            var paths = JsonSerializer.Deserialize<List<string>>(processed.OriginalSourcePathsJson) ?? [];
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete original file '{Path}' for ProcessedBook {Id}", path, processed.Id);
                }
            }

            processed.OriginalDeleted = true;
        }

        await db.SaveChangesAsync(ct);
    }
}
