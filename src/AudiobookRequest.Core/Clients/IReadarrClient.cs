namespace AudiobookRequest.Core.Clients;

public interface IReadarrClient
{
    Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default);
}
