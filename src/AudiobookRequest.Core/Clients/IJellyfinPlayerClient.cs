namespace AudiobookRequest.Core.Clients;

/// <summary>
/// Jellyfin calls made as a particular signed-in user (their own token), for the in-app audiobook player:
/// their library with their own progress, chapters, the audio stream, and reporting where they are up to.
/// Every method throws <see cref="JellyfinUnauthorizedException"/> if Jellyfin rejects the token.
/// </summary>
public interface IJellyfinPlayerClient
{
    Task<JellyfinItemsResponseRaw> GetItemsAsync(string baseUrl, JellyfinSession session, JellyfinItemQuery query, CancellationToken ct = default);

    /// <summary>One book with its chapters and media sources, or null if it doesn't exist or the user can't see it.</summary>
    Task<JellyfinPlayerItem?> GetItemAsync(string baseUrl, JellyfinSession session, string itemId, CancellationToken ct = default);

    /// <summary>
    /// Opens a GET to a Jellyfin path (audio stream, cover image) for passing straight through to the browser.
    /// The caller disposes the response. A Range header, if given, is forwarded so seeking works.
    /// </summary>
    Task<HttpResponseMessage> OpenAsync(string baseUrl, JellyfinSession session, string pathAndQuery, string? rangeHeader, CancellationToken ct = default);

    Task ReportPlaybackAsync(string baseUrl, JellyfinSession session, JellyfinPlaybackReport report, CancellationToken ct = default);

    Task SetPlayedAsync(string baseUrl, JellyfinSession session, string itemId, bool played, CancellationToken ct = default);

    /// <summary>Ends this sign-in on Jellyfin's side so it doesn't linger in the dashboard's device list.</summary>
    Task LogoutAsync(string baseUrl, JellyfinSession session, CancellationToken ct = default);

    /// <summary>Stops Jellyfin converting a book it was transcoding for this device (a courtesy; Jellyfin also times them out).</summary>
    Task StopTranscodeAsync(string baseUrl, JellyfinSession session, string playSessionId, CancellationToken ct = default);
}
