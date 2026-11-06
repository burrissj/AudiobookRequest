namespace AudiobookRequest.Core.Entities;

/// <summary>
/// An admin's correction to what the Library shows for one Jellyfin book: who wrote it and which series it is in. Wins over
/// what Jellyfin says, what Audible says and what the title says. Library-wide (everyone sees it) and kept apart from the
/// library cache so it survives that cache being rebuilt or Audible being asked again.
/// </summary>
public class BookMetadataOverride
{
    public int Id { get; set; }
    public required string JellyfinItemId { get; set; }

    /// <summary>The author to show and group under; null leaves what Jellyfin says.</summary>
    public string? Author { get; set; }

    /// <summary>The series to put the book in; null leaves what Audible or the title says.</summary>
    public string? Series { get; set; }
    public decimal? SeriesNumber { get; set; }

    /// <summary>The book is in no series, whatever Audible or its title say.</summary>
    public bool NotInSeries { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
