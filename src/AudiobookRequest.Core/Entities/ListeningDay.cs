namespace AudiobookRequest.Core.Entities;

/// <summary>
/// How long one person listened to one book on one day, for the Stats page. Jellyfin keeps where you are up to
/// but not how long you listened, so the player reports it as it goes and the app keeps the totals.
/// </summary>
public class ListeningDay
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    /// <summary>The listener's own calendar day ("yyyy-MM-dd"), so late-night listening lands on the day it felt like.</summary>
    public required string Day { get; set; }

    public required string JellyfinItemId { get; set; }

    /// <summary>Snapshots, so stats still read well if the book later leaves the library.</summary>
    public required string Title { get; set; }
    public string? Author { get; set; }

    /// <summary>Seconds of the book that played (at 2x, an hour of listening is two hours of book).</summary>
    public double ContentSeconds { get; set; }

    /// <summary>Seconds of real time spent listening.</summary>
    public double WallSeconds { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
