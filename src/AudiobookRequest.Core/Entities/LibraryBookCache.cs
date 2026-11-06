namespace AudiobookRequest.Core.Entities;

/// <summary>
/// Cached snapshot of the FULL Jellyfin audiobook library (not just played items, unlike
/// ListeningHistoryCache) -- used to flag "you already have this" in Browse search and exclude
/// already-owned books from recommendations. Library-wide, not tied to any one user.
/// </summary>
public class LibraryBookCache
{
    public int Id { get; set; }
    public required string JellyfinItemId { get; set; }
    public required string Title { get; set; }
    public string? Author { get; set; }
    /// <summary>Every Jellyfin artist on the item, '|'-separated (Jellyfin mixes authors and narrators here).</summary>
    public string? Artists { get; set; }
    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The series Audible lists this book in, when it was found there (LibrarySeriesService fills this in the background).</summary>
    public string? SeriesName { get; set; }
    public string? SeriesAsin { get; set; }
    /// <summary>Audible's own place-in-series text: "3", or "1-3" for an omnibus; may be blank.</summary>
    public string? SeriesSequence { get; set; }
    /// <summary>When Audible was last asked about this book (found or not); null until the first time.</summary>
    public DateTimeOffset? SeriesCheckedAt { get; set; }
}
