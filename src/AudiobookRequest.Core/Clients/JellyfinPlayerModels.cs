using System.Text.Json.Serialization;

namespace AudiobookRequest.Core.Clients;

/// <summary>
/// A signed-in user's own Jellyfin session, kept from login so the in-app player acts as that user. The device
/// id/name are the ones the token was issued to; Jellyfin expects every later call to identify as that same device.
/// </summary>
public record JellyfinSession(string Token, string UserId, string DeviceId = "audiobookrequest-web", string DeviceName = "Web Player");

/// <summary>How a browser shows up in Jellyfin's dashboard under Devices.</summary>
public record JellyfinDevice(string Id, string Name);

/// <summary>Jellyfin refused the user's token (expired or revoked). The person has to sign in again.</summary>
public class JellyfinUnauthorizedException(string message) : Exception(message);

public class JellyfinItemsResponseRaw
{
    [JsonPropertyName("Items")]
    public List<JellyfinPlayerItem> Items { get; set; } = [];

    [JsonPropertyName("TotalRecordCount")]
    public int TotalRecordCount { get; set; }
}

public class JellyfinPlayerItem
{
    [JsonPropertyName("Id")]
    public required string Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("ArtistItems")]
    public List<JellyfinNameId>? ArtistItems { get; set; }

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; set; }

    [JsonPropertyName("DateCreated")]
    public DateTimeOffset? DateCreated { get; set; }

    [JsonPropertyName("ImageTags")]
    public Dictionary<string, string>? ImageTags { get; set; }

    [JsonPropertyName("UserData")]
    public JellyfinPlayerUserData? UserData { get; set; }

    /// <summary>Jellyfin's own description of the book (from its metadata provider or the file's tags), when it has one.</summary>
    [JsonPropertyName("Overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("Chapters")]
    public List<JellyfinChapter>? Chapters { get; set; }

    [JsonPropertyName("MediaSources")]
    public List<JellyfinMediaSource>? MediaSources { get; set; }
}

public class JellyfinPlayerUserData
{
    [JsonPropertyName("PlaybackPositionTicks")]
    public long PlaybackPositionTicks { get; set; }

    [JsonPropertyName("Played")]
    public bool Played { get; set; }

    [JsonPropertyName("LastPlayedDate")]
    public DateTimeOffset? LastPlayedDate { get; set; }
}

public class JellyfinChapter
{
    [JsonPropertyName("StartPositionTicks")]
    public long StartPositionTicks { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }
}

public class JellyfinMediaSource
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Container")]
    public string? Container { get; set; }

    [JsonPropertyName("MediaStreams")]
    public List<JellyfinMediaStream>? MediaStreams { get; set; }
}

public class JellyfinMediaStream
{
    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("Codec")]
    public string? Codec { get; set; }
}

public record JellyfinItemQuery(
    bool ResumableOnly = false,
    bool PlayedOnly = false,
    string? SearchTerm = null,
    string SortBy = "SortName",
    bool Descending = false,
    int StartIndex = 0,
    int Limit = 60,
    IReadOnlyList<string>? Ids = null);

/// <param name="Kind">"Playing" (started), "Progress", or "Stopped".</param>
public record JellyfinPlaybackReport(string Kind, string ItemId, string? MediaSourceId, long PositionTicks, bool IsPaused, string? PlaySessionId);
