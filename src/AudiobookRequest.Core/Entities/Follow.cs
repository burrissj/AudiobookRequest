namespace AudiobookRequest.Core.Entities;

public enum FollowKind
{
    Author = 0,
    Series = 1
}

/// <summary>
/// "Tell me when this author or series has a new book." Optionally also requests it automatically:
/// for an author, every new release; for a series, the book after the last one the user finished.
/// </summary>
public class Follow
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public FollowKind Kind { get; set; }

    /// <summary>The author's name, or the series' title.</summary>
    public required string Name { get; set; }

    /// <summary>Audible's series id; set for a series follow, null for an author.</summary>
    public string? SeriesAsin { get; set; }

    /// <summary>The series' author, needed to search Audible for its books.</summary>
    public string? SeriesAuthor { get; set; }

    public bool AutoRequest { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>
    /// ASINs already seen when the follow started or on a previous check, as a JSON array. Only books
    /// that appear after this snapshot count as "new", so following a prolific author doesn't flood.
    /// </summary>
    public string KnownAsinsJson { get; set; } = "[]";

    public ICollection<FollowFind> Finds { get; set; } = [];
}

/// <summary>A new book found for a follow. Whether it is now owned or requested is worked out when shown.</summary>
public class FollowFind
{
    public int Id { get; set; }
    public int FollowId { get; set; }
    public Follow? Follow { get; set; }

    public required string Asin { get; set; }
    public required string Title { get; set; }
    public required string Author { get; set; }
    public string? CoverUrl { get; set; }
    public int? RuntimeMinutes { get; set; }

    /// <summary>Audible's release date (yyyy-MM-dd); in the future for announced books.</summary>
    public string? ReleaseDate { get; set; }

    public DateTimeOffset FoundAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Dismissed { get; set; }

    /// <summary>When an automatic request was created for this find. While null and auto-request is on, each check tries again.</summary>
    public DateTimeOffset? AutoRequestedAt { get; set; }
}
