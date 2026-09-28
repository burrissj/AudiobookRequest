using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AudiobookRequest.Core.Clients;

public class JellyfinClient(HttpClient httpClient) : IJellyfinClient
{
    private const string DeviceHeader =
        "MediaBrowser Client=\"AudiobookRequest\", Device=\"Server\", DeviceId=\"audiobookrequest-service\", Version=\"1.0.0\"";

    public async Task<JellyfinAuthResult?> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "/Users/AuthenticateByName"));
        request.Headers.TryAddWithoutValidation("X-Emby-Authorization", DeviceHeader);
        request.Content = JsonContent.Create(new { Username = username, Pw = password });

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
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
