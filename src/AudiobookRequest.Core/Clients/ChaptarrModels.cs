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
}

/// <summary>A Chaptarr root folder, carrying its own audiobook quality/metadata profile defaults.</summary>
public record ChaptarrRootFolder(string Path, int AudiobookQualityProfileId, int AudiobookMetadataProfileId);

public record ChaptarrQueueItem(string? Title, int? BookId, string Status, double SizeLeftBytes);

public record ChaptarrHistoryItem(int? BookId, string EventType, DateTimeOffset Date);

public record ChaptarrBookFile(int Id, int BookId, string Path);

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

    [JsonPropertyName("sizeleft")]
    public double SizeLeft { get; set; }
}

internal class ChaptarrHistoryRecordRaw
{
    [JsonPropertyName("bookId")]
    public int? BookId { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }
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
