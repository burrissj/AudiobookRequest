namespace AudiobookRequest.Core.Entities;

public enum BookRequestStatus
{
    Requested,
    Searching,
    Downloading,
    Processing,
    Ready,
    Failed
}

public class BookRequest
{
    public int Id { get; set; }
    public int RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }

    public required string Title { get; set; }
    public required string Author { get; set; }
    public string? Series { get; set; }
    public string? CoverUrl { get; set; }

    /// <summary>The book id/GUID as returned by Readarr's lookup endpoint (foreignBookId).</summary>
    public required string ReadarrForeignBookId { get; set; }
    /// <summary>
    /// The author id/GUID needed to add the author to Readarr. Populated from the book lookup result
    /// when the metadata provider nests it there, otherwise resolved lazily (by author name, via
    /// IReadarrClient.FindForeignAuthorIdAsync) during the Requested -> Searching transition.
    /// </summary>
    public string? ReadarrForeignAuthorId { get; set; }
    /// <summary>Readarr's internal numeric book id, populated once the author/book has been added.</summary>
    public int? ReadarrBookId { get; set; }

    public BookRequestStatus Status { get; set; } = BookRequestStatus.Requested;
    public string? FailureReason { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadyAt { get; set; }

    public ProcessedBook? ProcessedBook { get; set; }
}
