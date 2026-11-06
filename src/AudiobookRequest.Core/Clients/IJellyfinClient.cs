namespace AudiobookRequest.Core.Clients;

public interface IJellyfinClient
{
    /// <param name="device">The browser signing in. Each browser gets its own Jellyfin device so signing out of one never ends another's session.</param>
    Task<JellyfinAuthResult?> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken ct = default, JellyfinDevice? device = null);
    Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<JellyfinPlayedItem>> GetPlayedItemsAsync(string baseUrl, string apiKey, string userId, CancellationToken ct = default);

    /// <summary>The full audiobook library, server-wide -- not scoped to any user or played state. Used to flag already-owned books.</summary>
    Task<IReadOnlyList<JellyfinPlayedItem>> GetLibraryAudiobooksAsync(string baseUrl, string apiKey, CancellationToken ct = default);
    /// <summary>Audiobooks whose name matches a search term, live from Jellyfin -- used to link straight to a book that just finished.</summary>
    Task<IReadOnlyList<JellyfinPlayedItem>> SearchAudiobooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default);

    Task RefreshItemAsync(string baseUrl, string apiKey, string itemId, CancellationToken ct = default);
    Task RefreshLibraryAsync(string baseUrl, string apiKey, CancellationToken ct = default);
}
