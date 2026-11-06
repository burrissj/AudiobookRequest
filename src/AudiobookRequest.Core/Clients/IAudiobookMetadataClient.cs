namespace AudiobookRequest.Core.Clients;

/// <summary>
/// Free, unauthenticated audiobook metadata (Audible's public catalog search + Audnexus) -- fills the
/// gap Chaptarr's own lookup leaves for genre discovery and listening time, neither of which Chaptarr's
/// text search exposes (see AddBookAsync's neighbors for why: that data only shows up in Chaptarr's own
/// catalog after a book is added and background-synced, too late for a pre-request preview).
/// </summary>
public interface IAudiobookMetadataClient
{
    /// <summary>
    /// One page of Audible's catalog filtered by genre id and/or keywords, with cover art, length and
    /// rating -- powers the category browse screens. Best-effort: returns an empty page on failure.
    /// </summary>
    Task<AudiobookPage> BrowseAsync(string? audibleCategoryId, string? keywords, string sort, int page, int pageSize, string? author = null, CancellationToken ct = default);

    /// <summary>
    /// Looks a book up by its title (and author, when known) -- Audible's title search, which finds the exact book far
    /// more reliably than a keyword search (that ranks translated editions and anthologies first). Used to identify
    /// books by name. Best-effort: returns an empty page on failure.
    /// </summary>
    Task<AudiobookPage> SearchAsync(string title, string? author, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Resolves a specific book's listening time/narrators/genres by title+author. Best-effort: never
    /// throws, returns null if no confident match is found or either upstream call fails.
    /// </summary>
    Task<AudiobookMetadata?> TryResolveDetailAsync(string title, string authorName, CancellationToken ct = default);

    /// <summary>
    /// Searches for other books carrying the same series, by series title + author -- used to expand a
    /// bundle/omnibus into its individual member books. Results are unfiltered (include the bundle
    /// itself, other unrelated same-named items, etc.); the caller filters by SeriesAsin + sequence
    /// pattern. Best-effort: returns an empty list on failure.
    /// </summary>
    Task<IReadOnlyList<AudiobookCatalogEntry>> GetSeriesBooksAsync(string seriesTitle, string authorName, CancellationToken ct = default);
}
