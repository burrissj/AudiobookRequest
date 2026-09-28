namespace AudiobookRequest.Core.Entities;

public class ProcessedBook
{
    public int Id { get; set; }

    /// <summary>Null when the book was picked up from a manual Readarr grab rather than an in-app request.</summary>
    public int? BookRequestId { get; set; }
    public BookRequest? BookRequest { get; set; }

    public required string FinalPath { get; set; }

    /// <summary>JSON-serialized list of the original source file paths (still intact in Readarr's root folder).</summary>
    public required string OriginalSourcePathsJson { get; set; }

    public DateTimeOffset CopiedAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public DateTimeOffset OriginalCleanupAt { get; set; }
    public bool OriginalDeleted { get; set; }
}
