using System.Text.Json.Serialization;

namespace AudiobookRequest.Core.Clients;

/// <summary>Simplified projection of Readarr's book lookup result, enough to drive the request UI and the add/search flow.</summary>
public class ReadarrBookLookupResult
{
    public required string ForeignBookId { get; set; }
    public required string Title { get; set; }
    public string? Overview { get; set; }
    public string? SeriesTitle { get; set; }
    public string? CoverUrl { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }

    public required string ForeignAuthorId { get; set; }
    public required string AuthorName { get; set; }
}

public record ReadarrQueueItem(string? Title, int? BookId, string Status, double SizeLeftBytes);

public record ReadarrHistoryItem(int? BookId, string EventType, DateTimeOffset Date);

public record ReadarrBookFile(int Id, int BookId, string Path);

internal class ReadarrBookFileRaw
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("bookId")]
    public int BookId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

internal class ReadarrBookRaw
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

    [JsonPropertyName("images")]
    public List<ReadarrImageRaw>? Images { get; set; }

    [JsonPropertyName("author")]
    public ReadarrAuthorRaw? Author { get; set; }
}

internal class ReadarrAuthorRaw
{
    [JsonPropertyName("foreignAuthorId")]
    public string? ForeignAuthorId { get; set; }

    [JsonPropertyName("authorName")]
    public string? AuthorName { get; set; }
}

internal class ReadarrImageRaw
{
    [JsonPropertyName("coverType")]
    public string? CoverType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }
}

internal class ReadarrQueueResponse
{
    [JsonPropertyName("records")]
    public List<ReadarrQueueRecordRaw> Records { get; set; } = [];
}

internal class ReadarrQueueRecordRaw
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

internal class ReadarrHistoryRecordRaw
{
    [JsonPropertyName("bookId")]
    public int? BookId { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }
}

internal class ReadarrHistoryResponse
{
    [JsonPropertyName("records")]
    public List<ReadarrHistoryRecordRaw> Records { get; set; } = [];
}
