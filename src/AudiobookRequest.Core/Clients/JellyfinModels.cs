using System.Text.Json.Serialization;

namespace AudiobookRequest.Core.Clients;

public class JellyfinAuthResult
{
    [JsonPropertyName("User")]
    public required JellyfinUser User { get; set; }

    [JsonPropertyName("AccessToken")]
    public required string AccessToken { get; set; }
}

public class JellyfinUser
{
    [JsonPropertyName("Id")]
    public required string Id { get; set; }

    [JsonPropertyName("Name")]
    public required string Name { get; set; }

    [JsonPropertyName("Policy")]
    public JellyfinUserPolicy? Policy { get; set; }
}

public class JellyfinUserPolicy
{
    [JsonPropertyName("IsAdministrator")]
    public bool IsAdministrator { get; set; }
}

public class JellyfinPlayedItem
{
    [JsonPropertyName("Id")]
    public required string Id { get; set; }

    [JsonPropertyName("Name")]
    public required string Name { get; set; }

    [JsonPropertyName("Album")]
    public string? Series { get; set; }

    [JsonPropertyName("Genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("ArtistItems")]
    public List<JellyfinNameId>? ArtistItems { get; set; }

    [JsonPropertyName("UserData")]
    public JellyfinUserData? UserData { get; set; }
}

public class JellyfinNameId
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
}

public class JellyfinUserData
{
    [JsonPropertyName("PlayCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("LastPlayedDate")]
    public DateTimeOffset? LastPlayedDate { get; set; }
}

internal class JellyfinItemsResponse
{
    [JsonPropertyName("Items")]
    public List<JellyfinPlayedItem> Items { get; set; } = [];
}
