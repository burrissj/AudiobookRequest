namespace AudiobookRequest.Core.Clients;

public class ReadarrClient(HttpClient httpClient) : IReadarrClient
{
    public async Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/api/v1/system/status");
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        using var response = await httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}
