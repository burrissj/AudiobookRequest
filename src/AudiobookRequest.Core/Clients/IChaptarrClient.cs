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

    /// <summary>
    /// Removes a book from Chaptarr entirely (unmonitors it and stops any in-progress search/download) --
    /// used when cancelling a request, so a fresh request for the same book doesn't collide with a
    /// leftover Chaptarr entry. Never deletes files on disk.
    /// </summary>
    Task DeleteBookAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default);

    /// <summary>The files of a completed download that Chaptarr is holding for manual import, each with the book it matched.</summary>
    Task<IReadOnlyList<ChaptarrImportCandidate>> GetManualImportCandidatesAsync(string baseUrl, string apiKey, string downloadId, CancellationToken ct = default);

    /// <summary>A book's id/author and its editions, or null if Chaptarr no longer has it.</summary>
    Task<ChaptarrBookInfo?> GetBookInfoAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default);

    /// <summary>Queues Chaptarr's ManualImport command for the given files and returns the command id to poll.</summary>
    Task<int> StartManualImportAsync(string baseUrl, string apiKey, IReadOnlyList<System.Text.Json.Nodes.JsonObject> files, string importMode, CancellationToken ct = default);

    Task<ChaptarrCommandStatus> GetCommandStatusAsync(string baseUrl, string apiKey, int commandId, CancellationToken ct = default);
}
