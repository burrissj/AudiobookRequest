using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AudiobookRequest.Core.Clients;

public class JellyfinClient(HttpClient httpClient, ILogger<JellyfinClient> logger) : IJellyfinClient
{
    // Client-identification header for the (unauthenticated) login request. Jellyfin's own web client
    // sends this as the standard "Authorization" header, not the legacy "X-Emby-Authorization" name
    // used by older Jellyfin/Emby versions -- confirmed against a real Jellyfin 12.1.0 server, where
    // using the legacy header name causes the server to reject the request with a generic 400 rather
    // than the 401 it returns for genuinely bad credentials.
    private const string DeviceHeader =
        "MediaBrowser Client=\"AudiobookRequest\", Device=\"Server\", DeviceId=\"audiobookrequest-service\", Version=\"1.0.0\"";

    // Jellyfin's API expects exact PascalCase field names ("Username", "Pw"). Pass this explicitly rather
    // than relying on JsonContent.Create's ambient default, which serializes anonymous types as camelCase.
    private static readonly JsonSerializerOptions PascalCaseOptions = new() { PropertyNamingPolicy = null };

    public async Task<JellyfinAuthResult?> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken ct = default, JellyfinDevice? device = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "/Users/authenticatebyname"));
        request.Headers.TryAddWithoutValidation("Authorization", device is null ? DeviceHeader : JellyfinHeaders.ForLogin(device));
        request.Content = JsonContent.Create(new { Username = username, Pw = password }, options: PascalCaseOptions);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Jellyfin login rejected with {Status}: {Body}", response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JellyfinAuthResult>(cancellationToken: ct);
    }

    public async Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, baseUrl, "/System/Info", apiKey);
        using var response = await httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<JellyfinPlayedItem>> GetPlayedItemsAsync(string baseUrl, string apiKey, string userId, CancellationToken ct = default)
    {
        var path = $"/Users/{userId}/Items?Filters=IsPlayed&SortBy=DatePlayed&SortOrder=Descending&Recursive=true&IncludeItemTypes=Audio,Book&Fields=UserData,Genres";
        using var request = AuthorizedRequest(HttpMethod.Get, baseUrl, path, apiKey);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(cancellationToken: ct);
        return result?.Items ?? [];
    }

    public async Task<IReadOnlyList<JellyfinPlayedItem>> GetLibraryAudiobooksAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        // Confirmed live: Jellyfin's actual item type for audiobooks is "AudioBook" (not "Audio"/"Book"),
        // and this works server-wide with Recursive=true + no ParentId -- no need to resolve a specific
        // library folder first.
        var path = "/Items?IncludeItemTypes=AudioBook&Recursive=true&Fields=Genres,ArtistItems";
        using var request = AuthorizedRequest(HttpMethod.Get, baseUrl, path, apiKey);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(cancellationToken: ct);
        return result?.Items ?? [];
    }

    public async Task<IReadOnlyList<JellyfinPlayedItem>> SearchAudiobooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default)
    {
        var path = $"/Items?IncludeItemTypes=AudioBook&Recursive=true&Limit=10&Fields=ArtistItems&SearchTerm={Uri.EscapeDataString(term)}";
        using var request = AuthorizedRequest(HttpMethod.Get, baseUrl, path, apiKey);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(cancellationToken: ct);
        return result?.Items ?? [];
    }

    public async Task RefreshItemAsync(string baseUrl, string apiKey, string itemId, CancellationToken ct = default)
    {
        using var request = AuthorizedRequest(HttpMethod.Post, baseUrl, $"/Items/{itemId}/Refresh?Recursive=true&MetadataRefreshMode=Default", apiKey);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RefreshLibraryAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var request = AuthorizedRequest(HttpMethod.Post, baseUrl, "/Library/Refresh", apiKey);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string baseUrl, string path, string apiKey)
    {
        var request = new HttpRequestMessage(method, Combine(baseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{apiKey}\"");
        return request;
    }

    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;
}
