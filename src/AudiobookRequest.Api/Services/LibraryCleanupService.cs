using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record StaleRequestDto(int Id, string Title, string Author, string RequestedBy, DateTimeOffset? ReadyAt);

/// <param name="Blocked">Why nothing can be reported safely (Jellyfin not set up, or it reports an empty library); null when the scan is trustworthy.</param>
public record StaleScanDto(int FinishedChecked, int JellyfinBooks, string? Blocked, IReadOnlyList<StaleRequestDto> Missing);

public record RemoveStaleRequest(IReadOnlyList<int> RequestIds);

public record RemoveStaleResult(int Removed, int StillInJellyfin);

/// <summary>
/// Finds finished ("Ready") requests whose book has since been deleted from Jellyfin, and removes their records
/// so they stop cluttering the Requests list and the book can be requested again. Only records are removed --
/// never audio files. It asks Jellyfin live (not the cached copy) and refuses to act when the answer looks
/// untrustworthy, because "Jellyfin is down" must never read as "every book was deleted".
/// </summary>
public class LibraryCleanupService(
    AppDbContext db,
    SettingsService settingsService,
    IJellyfinClient jellyfin,
    RequestCancellationService cancellation)
{
    public async Task<StaleScanDto> ScanAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.JellyfinUrl) || string.IsNullOrWhiteSpace(settings.JellyfinApiKey))
        {
            return new StaleScanDto(0, 0, "Jellyfin isn't set up yet, so there's nothing to compare against.", []);
        }

        var ready = await db.BookRequests.Include(r => r.RequestedByUser).Where(r => r.Status == BookRequestStatus.Ready).ToListAsync(ct);
        // Throws if Jellyfin can't be reached: the caller reports that, and nothing is touched.
        var items = await jellyfin.GetLibraryAudiobooksAsync(settings.JellyfinUrl, settings.JellyfinApiKey, ct);

        if (ready.Count > 0 && items.Count == 0)
        {
            return new StaleScanDto(ready.Count, 0,
                "Jellyfin reports no audiobooks at all. That usually means its library is offline or still scanning, so nothing is being flagged.", []);
        }

        var index = new LibraryIndex(items.Select(i =>
            new LibraryEntry(i.Name, string.Join('|', (i.ArtistItems ?? []).Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n))))));

        var missing = ready
            .Where(r => !index.IsOwned(r.Title, r.Author) && index.FindSimilar(r.Title, r.Author) is null)
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(r => new StaleRequestDto(r.Id, r.Title, r.Author, r.RequestedByUser?.Username ?? "", r.ReadyAt))
            .ToList();

        return new StaleScanDto(ready.Count, items.Count, null, missing);
    }

    /// <summary>Removes the chosen requests, but only those that a fresh scan still finds missing -- a book put back since the list was shown is left alone.</summary>
    public async Task<RemoveStaleResult> RemoveAsync(IEnumerable<int> requestIds, CancellationToken ct)
    {
        var scan = await ScanAsync(ct);
        if (scan.Blocked is not null)
        {
            throw new InvalidOperationException(scan.Blocked);
        }

        var missing = scan.Missing.Select(m => m.Id).ToHashSet();
        var wanted = requestIds.Distinct().ToList();
        var removed = 0;
        foreach (var id in wanted.Where(missing.Contains))
        {
            if (await cancellation.CancelAsync(id, ct))
            {
                removed++;
            }
        }

        return new RemoveStaleResult(removed, wanted.Count(id => !missing.Contains(id)));
    }
}
