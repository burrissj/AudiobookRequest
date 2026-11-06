namespace AudiobookRequest.Core.Entities;

// A person's own notes on a book in the library: a star rating, tags, bookmarks. All are per person (never shared)
// and keyed by the book's Jellyfin id, so they follow the book wherever it is listened to from.

/// <summary>One person's 1-5 star rating of one book. Clearing a rating deletes the row.</summary>
public class BookRating
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public required string JellyfinItemId { get; set; }
    public int Stars { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A label a person put on a book ("Favorites", "Re-listen", "Great narrator").</summary>
public class BookTag
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public required string JellyfinItemId { get; set; }

    /// <summary>As the person typed it (and first spelled it): "Sci-Fi".</summary>
    public required string Tag { get; set; }

    /// <summary>Lower-case form used to match tags regardless of capitalisation: "sci-fi".</summary>
    public required string TagKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A saved place in a book, with an optional note.</summary>
public class BookBookmark
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public required string JellyfinItemId { get; set; }
    public double PositionSeconds { get; set; }
    public string? Note { get; set; }

    /// <summary>What was playing when it was made ("Chapter 12"), so the list reads well without opening the book.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Made up by the player when the bookmark is created. A bookmark made offline is sent again until it gets through;
    /// this lets the server recognise the repeat instead of saving it twice.
    /// </summary>
    public string? ClientId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
