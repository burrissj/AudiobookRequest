namespace AudiobookRequest.Core.Clients;

public interface IChaptarrClient
{
    Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default);

    /// <summary>Searches for books; only audiobook-media-type results are returned (Chaptarr manages both audiobooks and ebooks).</summary>
    Task<IReadOnlyList<ChaptarrBookLookupResult>> LookupBooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default);

    /// <summary>Looks up a configured root folder by path, including its audiobook quality/metadata profile defaults.</summary>
    Task<ChaptarrRootFolder?> FindRootFolderAsync(string baseUrl, string apiKey, string rootFolderPath, CancellationToken ct = default);

    /// <summary>
    /// Adds a book (and its author, if not already present) in a single atomic call, monitored for
    /// audiobook. Returns Chaptarr's local numeric book id. Unlike Readarr, this doesn't require
    /// adding the author first and waiting for its bibliography to sync -- it either succeeds immediately
    /// or throws.
    /// </summary>
    Task<int> AddBookAsync(string baseUrl, string apiKey, string foreignBookId, ChaptarrRootFolder rootFolder, CancellationToken ct = default);

    Task TriggerBookSearchAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default);

    Task<IReadOnlyList<ChaptarrQueueItem>> GetQueueAsync(string baseUrl, string apiKey, CancellationToken ct = default);

    Task<IReadOnlyList<ChaptarrHistoryItem>> GetHistorySinceAsync(string baseUrl, string apiKey, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>The actual file(s) Chaptarr imported for a book, as absolute paths on disk under the configured root folder.</summary>
    Task<IReadOnlyList<ChaptarrBookFile>> GetBookFilesAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default);
}
