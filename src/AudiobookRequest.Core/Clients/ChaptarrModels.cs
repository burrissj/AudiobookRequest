using System.Text.Json.Serialization;

namespace AudiobookRequest.Core.Clients;

/// <summary>Simplified projection of a Chaptarr book lookup result, enough to drive the request UI.</summary>
public class ChaptarrBookLookupResult
{
    public required string ForeignBookId { get; set; }
    public required string Title { get; set; }
    public string? Overview { get; set; }
    public string? SeriesTitle { get; set; }
    public string? CoverUrl { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public required string AuthorName { get; set; }
    public double? RatingValue { get; set; }
    public int? RatingVotes { get; set; }
}

/// <summary>A Chaptarr root folder, carrying its own audiobook quality/metadata profile defaults.</summary>
public record ChaptarrRootFolder(string Path, int AudiobookQualityProfileId, int AudiobookMetadataProfileId);

public record ChaptarrQueueItem(
    string? Title, int? BookId, string Status, double SizeBytes, double SizeLeftBytes, string? DownloadClient,
    string? TrackedDownloadStatus = null, string? TrackedDownloadState = null, string? ErrorMessage = null,
    IReadOnlyList<string>? Messages = null, string? DownloadId = null);

public record ChaptarrHistoryItem(int? BookId, string EventType, DateTimeOffset Date, string? Message = null);

public record ChaptarrBookFile(int Id, int BookId, string Path);

/// <summary>
/// A downloaded file Chaptarr is holding for manual import, with the book it matched on its own.
/// <paramref name="Raw"/> is Chaptarr's full record, kept so the import call can send its quality and
/// flags back untouched.
/// </summary>
public record ChaptarrImportCandidate(
    string Path,
    string Name,
    long SizeBytes,
    int? MatchedBookId,
    string? MatchedBookTitle,
    int? MatchedAuthorId,
    IReadOnlyList<string> Rejections,
    System.Text.Json.Nodes.JsonObject Raw);

public record ChaptarrEditionInfo(int Id, bool IsEbook, bool Monitored);

public record ChaptarrBookInfo(int Id, string Title, int AuthorId, IReadOnlyList<ChaptarrEditionInfo> Editions);

public record ChaptarrCommandStatus(int Id, string Status, string? Result, string? Message)
{
    public bool IsFinished => Status is "completed" or "failed" or "aborted" or "cancelled" or "orphaned";
    public bool Succeeded => Status == "completed" && !string.Equals(Result, "unsuccessful", StringComparison.OrdinalIgnoreCase);
}

internal class ChaptarrBookRaw
{
    [JsonPropertyName("foreignBookId")]
    public string? ForeignBookId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("seriesTitle")]
    public string? SeriesTitle { get; set; }

    [JsonPropertyName("releaseDate")]
    public DateTimeOffset? ReleaseDate { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("images")]
    public List<ChaptarrImageRaw>? Images { get; set; }

    [JsonPropertyName("author")]
    public ChaptarrAuthorRaw? Author { get; set; }

    [JsonPropertyName("ratings")]
    public ChaptarrRatingsRaw? Ratings { get; set; }
}

internal class ChaptarrRatingsRaw
{
    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
}

internal class ChaptarrAuthorRaw
{
    [JsonPropertyName("authorName")]
    public string? AuthorName { get; set; }
}

internal class ChaptarrImageRaw
{
    [JsonPropertyName("coverType")]
    public string? CoverType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }
}

internal class ChaptarrRootFolderRaw
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("audiobookQualityProfileId")]
    public int AudiobookQualityProfileId { get; set; }

    [JsonPropertyName("audiobookMetadataProfileId")]
    public int AudiobookMetadataProfileId { get; set; }
}

internal class ChaptarrQueueResponse
{
    [JsonPropertyName("records")]
    public List<ChaptarrQueueRecordRaw> Records { get; set; } = [];
}

internal class ChaptarrQueueRecordRaw
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("bookId")]
    public int? BookId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("size")]
    public double Size { get; set; }

    [JsonPropertyName("sizeleft")]
    public double SizeLeft { get; set; }

    [JsonPropertyName("downloadClient")]
    public string? DownloadClient { get; set; }

    [JsonPropertyName("trackedDownloadStatus")]
    public string? TrackedDownloadStatus { get; set; }

    [JsonPropertyName("trackedDownloadState")]
    public string? TrackedDownloadState { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("downloadId")]
    public string? DownloadId { get; set; }

    [JsonPropertyName("statusMessages")]
    public List<ChaptarrStatusMessageRaw>? StatusMessages { get; set; }
}

internal class ChaptarrStatusMessageRaw
{
    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }
}

internal class ChaptarrHistoryRecordRaw
{
    [JsonPropertyName("bookId")]
    public int? BookId { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }

    // Values are strings in practice but typed loosely so one odd record can't fail the whole page.
    [JsonPropertyName("data")]
    public Dictionary<string, System.Text.Json.JsonElement>? Data { get; set; }
}

internal class ChaptarrHistoryResponse
{
    [JsonPropertyName("records")]
    public List<ChaptarrHistoryRecordRaw> Records { get; set; } = [];
}

internal class ChaptarrBookFileRaw
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("bookId")]
    public int BookId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
