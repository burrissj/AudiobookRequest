using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record LibraryStatus(int BookCount, DateTimeOffset? LastRefreshedAt, bool JellyfinConfigured);

/// <summary>
/// Keeps LibraryBookCache in sync with Jellyfin's full audiobook library (refreshed by
/// LibraryCatalogRefreshBackgroundService, or on demand from the admin "Rescan now" button) and answers
/// "do we already have this" for Browse search, series completion and recommendations. Matching is by
/// title with edition-noise and author tolerance -- see LibraryIndex.
/// </summary>
public class LibraryCatalogService(AppDbContext db, SettingsService settingsService, IJellyfinClient jellyfin)
{
    public async Task<LibraryIndex> GetIndexAsync(CancellationToken ct)
    {
        var rows = await db.LibraryBooks.Select(b => new { b.Title, b.Artists, b.Author }).ToListAsync(ct);
        return new LibraryIndex(rows.Select(r => new LibraryEntry(r.Title, r.Artists ?? r.Author)));
    }

    /// <summary>Exact owned titles, case-insensitive -- what the recommendation filters work against.</summary>
    public async Task<HashSet<string>> GetOwnedTitlesAsync(CancellationToken ct)
    {
        var titles = await db.LibraryBooks.Select(b => b.Title).ToListAsync(ct);
        return new HashSet<string>(titles, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<LibraryStatus> GetStatusAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var count = await db.LibraryBooks.CountAsync(ct);
        // SQLite can't aggregate DateTimeOffset server-side, so pull the (small) column and max it here.
        var stamps = await db.LibraryBooks.Select(b => b.RefreshedAt).ToListAsync(ct);
        var configured = !string.IsNullOrWhiteSpace(settings.JellyfinUrl) && !string.IsNullOrWhiteSpace(settings.JellyfinApiKey);
        return new LibraryStatus(count, stamps.Count == 0 ? null : stamps.Max(), configured);
    }

    /// <returns>false when Jellyfin isn't configured (nothing was attempted).</returns>
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.JellyfinUrl) || string.IsNullOrWhiteSpace(settings.JellyfinApiKey))
        {
            return false;
        }

        var items = await jellyfin.GetLibraryAudiobooksAsync(settings.JellyfinUrl, settings.JellyfinApiKey, ct);
        if (items.Count == 0)
        {
            return true;
        }

        var existing = await db.LibraryBooks.ToDictionaryAsync(b => b.JellyfinItemId, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var item in items)
        {
            var names = (item.ArtistItems ?? []).Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            var author = names.FirstOrDefault();
            var artists = names.Count > 0 ? string.Join('|', names) : null;
            if (existing.TryGetValue(item.Id, out var cached))
            {
                cached.Title = item.Name;
                cached.Author = author;
                cached.Artists = artists;
                cached.RefreshedAt = now;
            }
            else
            {
                db.LibraryBooks.Add(new LibraryBookCache
                {
                    JellyfinItemId = item.Id,
                    Title = item.Name,
                    Author = author,
                    Artists = artists,
                    RefreshedAt = now
                });
            }
        }

        // Anything no longer reported by Jellyfin (removed/renamed) shouldn't keep blocking a re-request.
        var currentIds = items.Select(i => i.Id).ToHashSet();
        var stale = existing.Values.Where(b => !currentIds.Contains(b.JellyfinItemId));
        db.LibraryBooks.RemoveRange(stale);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
