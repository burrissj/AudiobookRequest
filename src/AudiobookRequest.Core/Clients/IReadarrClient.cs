namespace AudiobookRequest.Core.Clients;

public record ReadarrAddAuthorOptions(string RootFolderPath, int QualityProfileId, int MetadataProfileId);

public interface IReadarrClient
{
    Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default);

    Task<IReadOnlyList<ReadarrBookLookupResult>> LookupBooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default);

    /// <summary>Returns the local Readarr author id for the given foreign author id, adding the author (unmonitored) if it doesn't exist yet.</summary>
    Task<int> EnsureAuthorAsync(string baseUrl, string apiKey, string foreignAuthorId, ReadarrAddAuthorOptions options, CancellationToken ct = default);

    /// <summary>Finds the local Readarr book id for a foreign book id under the given author, or null if Readarr hasn't populated the author's bibliography yet.</summary>
    Task<int?> FindBookIdAsync(string baseUrl, string apiKey, int authorId, string foreignBookId, CancellationToken ct = default);

    Task MarkBookMonitoredAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default);

    Task TriggerBookSearchAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default);

    Task<IReadOnlyList<ReadarrQueueItem>> GetQueueAsync(string baseUrl, string apiKey, CancellationToken ct = default);

    Task<IReadOnlyList<ReadarrHistoryItem>> GetHistorySinceAsync(string baseUrl, string apiKey, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>The actual file(s) Readarr imported for a book, as absolute paths on disk under the configured root folder.</summary>
    Task<IReadOnlyList<ReadarrBookFile>> GetBookFilesAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default);
}
