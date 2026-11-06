using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AudiobookRequest.Core.Clients;

public class JellyfinPlayerClient(HttpClient httpClient) : IJellyfinPlayerClient
{
    private static readonly JsonSerializerOptions PascalCase = new() { PropertyNamingPolicy = null };

    public async Task<JellyfinItemsResponseRaw> GetItemsAsync(string baseUrl, JellyfinSession session, JellyfinItemQuery query, CancellationToken ct = default)
    {
        var path = $"/Users/{Uri.EscapeDataString(session.UserId)}/Items?IncludeItemTypes=AudioBook&Recursive=true" +
                   "&Fields=DateCreated&EnableUserData=true&EnableImageTypes=Primary" +
                   $"&SortBy={Uri.EscapeDataString(query.SortBy)}&SortOrder={(query.Descending ? "Descending" : "Ascending")}" +
                   $"&StartIndex={query.StartIndex}&Limit={query.Limit}";
        if (query.ResumableOnly)
        {
            path += "&Filters=IsResumable";
        }
        else if (query.PlayedOnly)
        {
            path += "&Filters=IsPlayed";
        }
        if (query.Ids is { Count: > 0 })
        {
            path += "&Ids=" + string.Join(',', query.Ids.Select(Uri.EscapeDataString));
        }
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            path += $"&SearchTerm={Uri.EscapeDataString(query.SearchTerm)}";
        }

        using var request = Authorized(HttpMethod.Get, baseUrl, path, session);
        using var response = await httpClient.SendAsync(request, ct);
        EnsureAuthorized(response);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JellyfinItemsResponseRaw>(cancellationToken: ct) ?? new JellyfinItemsResponseRaw();
    }

    public async Task<JellyfinPlayerItem?> GetItemAsync(string baseUrl, JellyfinSession session, string itemId, CancellationToken ct = default)
    {
        var path = $"/Users/{Uri.EscapeDataString(session.UserId)}/Items/{Uri.EscapeDataString(itemId)}";
        using var request = Authorized(HttpMethod.Get, baseUrl, path, session);
        using var response = await httpClient.SendAsync(request, ct);
        EnsureAuthorized(response);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JellyfinPlayerItem>(cancellationToken: ct);
    }

    public async Task<HttpResponseMessage> OpenAsync(string baseUrl, JellyfinSession session, string pathAndQuery, string? rangeHeader, CancellationToken ct = default)
    {
        var request = Authorized(HttpMethod.Get, baseUrl, pathAndQuery, session);
        if (!string.IsNullOrWhiteSpace(rangeHeader))
        {
            request.Headers.TryAddWithoutValidation("Range", rangeHeader);
        }

        try
        {
            // Headers only: the body is streamed on to the browser, not buffered.
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                response.Dispose();
                throw new JellyfinUnauthorizedException("Jellyfin no longer accepts this sign-in.");
            }
            return response;
        }
        finally
        {
            request.Dispose();
        }
    }

    public async Task ReportPlaybackAsync(string baseUrl, JellyfinSession session, JellyfinPlaybackReport report, CancellationToken ct = default)
    {
        var path = report.Kind switch
        {
            "Playing" => "/Sessions/Playing",
            "Stopped" => "/Sessions/Playing/Stopped",
            _ => "/Sessions/Playing/Progress",
        };

        using var request = Authorized(HttpMethod.Post, baseUrl, path, session);
        request.Content = JsonContent.Create(new
        {
            ItemId = report.ItemId,
            MediaSourceId = report.MediaSourceId ?? report.ItemId,
            PositionTicks = report.PositionTicks,
            IsPaused = report.IsPaused,
            PlaySessionId = report.PlaySessionId,
            CanSeek = true,
            PlayMethod = "DirectPlay",
        }, options: PascalCase);

        using var response = await httpClient.SendAsync(request, ct);
        EnsureAuthorized(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetPlayedAsync(string baseUrl, JellyfinSession session, string itemId, bool played, CancellationToken ct = default)
    {
        var path = $"/Users/{Uri.EscapeDataString(session.UserId)}/PlayedItems/{Uri.EscapeDataString(itemId)}";
        using var request = Authorized(played ? HttpMethod.Post : HttpMethod.Delete, baseUrl, path, session);
        using var response = await httpClient.SendAsync(request, ct);
        EnsureAuthorized(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutAsync(string baseUrl, JellyfinSession session, CancellationToken ct = default)
    {
        using var request = Authorized(HttpMethod.Post, baseUrl, "/Sessions/Logout", session);
        using var response = await httpClient.SendAsync(request, ct);
        // An already-expired token answers 401: the goal (no live token left) is met either way.
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task StopTranscodeAsync(string baseUrl, JellyfinSession session, string playSessionId, CancellationToken ct = default)
    {
        var path = $"/Videos/ActiveEncodings?deviceId={Uri.EscapeDataString(session.DeviceId)}&playSessionId={Uri.EscapeDataString(playSessionId)}";
        using var request = Authorized(HttpMethod.Delete, baseUrl, path, session);
        using var response = await httpClient.SendAsync(request, ct);
        EnsureAuthorized(response);
    }

    private static void EnsureAuthorized(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new JellyfinUnauthorizedException("Jellyfin no longer accepts this sign-in.");
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string baseUrl, string path, JellyfinSession session)
    {
        var request = new HttpRequestMessage(method, baseUrl.TrimEnd('/') + path);
        request.Headers.TryAddWithoutValidation("Authorization", JellyfinHeaders.ForSession(session));
        return request;
    }
}
