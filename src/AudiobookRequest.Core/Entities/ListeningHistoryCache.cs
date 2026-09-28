namespace AudiobookRequest.Core.Entities;

/// <summary>
/// Cached snapshot of a user's Jellyfin playback state, refreshed periodically and used by the
/// recommendation engine. Jellyfin's core API only exposes last-played state per item, not a full
/// chronological event log.
/// </summary>
public class ListeningHistoryCache
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public required string JellyfinItemId { get; set; }
    public required string Title { get; set; }
    public string? Author { get; set; }
    public string? Series { get; set; }
    public string? Genres { get; set; }

    public DateTimeOffset? LastPlayedDate { get; set; }
    public int PlayCount { get; set; }

    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;
}
