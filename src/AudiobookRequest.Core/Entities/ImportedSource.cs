namespace AudiobookRequest.Core.Entities;

/// <summary>
/// A book in the admin's import folder that has been imported into the library. Remembering it keeps the scan from
/// offering the same book again when the original files were left where they were.
/// </summary>
public class ImportedSource
{
    public int Id { get; set; }

    /// <summary>The full path of the book's folder or file, lower-cased, for matching without regard to capitalisation.</summary>
    public required string SourceKey { get; set; }

    public required string SourcePath { get; set; }
    public required string FinalPath { get; set; }
    public required string Title { get; set; }
    public required string Author { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}
