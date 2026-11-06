using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.BackgroundServices;

/// <summary>
/// Periodically pulls each user's played items from Jellyfin into ListeningHistoryCache, which
/// RecommendationService then scores against. Unlike the other background services here, this runs
/// once immediately on startup (not just after the first interval elapses) -- recommendations are
/// useless on a fresh install until this cache has data at least once.
/// </summary>
public class ListeningHistoryRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobStatus jobStatus,
    ILogger<ListeningHistoryRefreshBackgroundService> logger) : BackgroundService
{
    public const string JobName = "Listening history refresh";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    private static readonly TimeSpan NotConfiguredRetry = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            // On a fresh install the first pass runs before setup is finished; retry every minute until
            // Jellyfin is configured instead of waiting out a full interval with no recommendations.
            while (!stoppingToken.IsCancellationRequested)
            {
                var ran = false;
                try
                {
                    ran = await RefreshAllAsync(stoppingToken);
                    if (ran)
                    {
                        jobStatus.Success(JobName);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Listening history refresh failed");
                    jobStatus.Failure(JobName, ex);
                    ran = true;
                }

                if (ran)
                {
                    break;
                }
                await Task.Delay(NotConfiguredRetry, stoppingToken);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <returns>false when Jellyfin isn't configured yet and the caller should retry soon.</returns>
    private async Task<bool> RefreshAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var jellyfin = scope.ServiceProvider.GetRequiredService<IJellyfinClient>();

        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.JellyfinUrl) || string.IsNullOrWhiteSpace(settings.JellyfinApiKey))
        {
            return false;
        }

        var users = await db.Users.ToListAsync(ct);
        foreach (var user in users)
        {
            try
            {
                await RefreshForUserAsync(user, settings, db, jellyfin, ct);
            }
            catch (Exception ex)
            {
                logger.LogFailure(ex, "Failed to refresh listening history for user {UserId}", user.Id);
            }
        }
        return true;
    }

    private static async Task RefreshForUserAsync(AppUser user, AppSettings settings, AppDbContext db, IJellyfinClient jellyfin, CancellationToken ct)
    {
        var played = await jellyfin.GetPlayedItemsAsync(settings.JellyfinUrl!, settings.JellyfinApiKey!, user.JellyfinUserId, ct);
        if (played.Count == 0)
        {
            return;
        }

        var existing = await db.ListeningHistory
            .Where(h => h.AppUserId == user.Id)
            .ToDictionaryAsync(h => h.JellyfinItemId, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var item in played)
        {
            var author = item.ArtistItems?.FirstOrDefault()?.Name;
            var genres = item.Genres is { Count: > 0 } ? string.Join(',', item.Genres) : null;

            if (existing.TryGetValue(item.Id, out var cached))
            {
                cached.Title = item.Name;
                cached.Author = author;
                cached.Series = item.Series;
                cached.Genres = genres;
                cached.LastPlayedDate = item.UserData?.LastPlayedDate;
                cached.PlayCount = item.UserData?.PlayCount ?? 0;
                cached.RefreshedAt = now;
            }
            else
            {
                db.ListeningHistory.Add(new ListeningHistoryCache
                {
                    AppUserId = user.Id,
                    JellyfinItemId = item.Id,
                    Title = item.Name,
                    Author = author,
                    Series = item.Series,
                    Genres = genres,
                    LastPlayedDate = item.UserData?.LastPlayedDate,
                    PlayCount = item.UserData?.PlayCount ?? 0,
                    RefreshedAt = now
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
