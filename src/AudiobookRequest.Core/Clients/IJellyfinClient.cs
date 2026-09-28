namespace AudiobookRequest.Core.Clients;

public interface IJellyfinClient
{
    Task<JellyfinAuthResult?> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<JellyfinPlayedItem>> GetPlayedItemsAsync(string baseUrl, string apiKey, string userId, CancellationToken ct = default);
    Task RefreshItemAsync(string baseUrl, string apiKey, string itemId, CancellationToken ct = default);
    Task RefreshLibraryAsync(string baseUrl, string apiKey, CancellationToken ct = default);
}
