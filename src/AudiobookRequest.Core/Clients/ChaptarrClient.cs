using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AudiobookRequest.Core.Clients;

public class ChaptarrClient(HttpClient httpClient) : IChaptarrClient
{
    public async Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/system/status", ct: ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<ChaptarrBookLookupResult>> LookupBooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default)
    {
        var path = $"/api/v1/book/lookup?term={Uri.EscapeDataString(term)}";
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, path, ct: ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadFromJsonAsync<List<ChaptarrBookRaw>>(cancellationToken: ct) ?? [];
        return raw
            .Where(b => !string.IsNullOrEmpty(b.ForeignBookId) && b.MediaType == "audiobook")
            .Select(Map)
            .ToList();
    }

    public async Task<ChaptarrRootFolder?> FindRootFolderAsync(string baseUrl, string apiKey, string rootFolderPath, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/rootfolder", ct: ct);
        response.EnsureSuccessStatusCode();
        var folders = await response.Content.ReadFromJsonAsync<List<ChaptarrRootFolderRaw>>(cancellationToken: ct) ?? [];
        var match = folders.FirstOrDefault(f => string.Equals(
            f.Path?.TrimEnd('/', '\\'), rootFolderPath.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase));
        return match is null ? null : new ChaptarrRootFolder(match.Path!, match.AudiobookQualityProfileId, match.AudiobookMetadataProfileId);
    }

    public async Task<int> AddBookAsync(string baseUrl, string apiKey, string foreignBookId, ChaptarrRootFolder rootFolder, CancellationToken ct = default)
    {
        // Chaptarr adds the author and book together in one call, so re-fetch the full nested
        // resource (book/lookup also supports exact-id lookups) and post it back with monitoring
        // and profile fields filled in -- no separate "add author, wait for it to sync" step needed.
        var lookupPath = $"/api/v1/book/lookup?term={Uri.EscapeDataString(foreignBookId)}";
        using var lookupResponse = await SendAsync(HttpMethod.Get, baseUrl, apiKey, lookupPath, ct: ct);
        lookupResponse.EnsureSuccessStatusCode();
        var results = await lookupResponse.Content.ReadFromJsonAsync<List<JsonObject>>(cancellationToken: ct) ?? [];
        var book = results.FirstOrDefault()
            ?? throw new InvalidOperationException($"Chaptarr no longer recognizes foreign book id '{foreignBookId}'.");

        book["monitored"] = true;
        book["audiobookMonitored"] = true;
        book["rootFolderPath"] = rootFolder.Path;
        book["addOptions"] = new JsonObject { ["addType"] = "automatic", ["searchForNewBook"] = false };

        if (book["author"] is JsonObject author)
        {
            author["monitored"] = true;
            author["rootFolderPath"] = rootFolder.Path;
            author["qualityProfileId"] = rootFolder.AudiobookQualityProfileId;
            author["metadataProfileId"] = rootFolder.AudiobookMetadataProfileId;
        }

        if (book["editions"] is JsonArray editions)
        {
            foreach (var edition in editions.OfType<JsonObject>().Where(e => e["isEbook"]?.GetValue<bool>() != true))
            {
                edition["monitored"] = true;
            }
        }

        using var addResponse = await SendAsync(HttpMethod.Post, baseUrl, apiKey, "/api/v1/book", book, ct);
        addResponse.EnsureSuccessStatusCode();
        var created = await addResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        return created!["id"]!.GetValue<int>();
    }

    public async Task TriggerBookSearchAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["name"] = "BookSearch",
            ["bookIds"] = new JsonArray(bookId)
        };
        using var response = await SendAsync(HttpMethod.Post, baseUrl, apiKey, "/api/v1/command", body, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ChaptarrQueueItem>> GetQueueAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/queue?pageSize=200", ct: ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChaptarrQueueResponse>(cancellationToken: ct);
        return (result?.Records ?? [])
            .Select(r => new ChaptarrQueueItem(r.Title, r.BookId, r.Status ?? "unknown", r.SizeLeft))
            .ToList();
    }

    public async Task<IReadOnlyList<ChaptarrHistoryItem>> GetHistorySinceAsync(string baseUrl, string apiKey, DateTimeOffset since, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/history?pageSize=200&sortKey=date&sortDirection=descending", ct: ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChaptarrHistoryResponse>(cancellationToken: ct);
        return (result?.Records ?? [])
            .Where(r => r.Date >= since && r.EventType is not null)
            .Select(r => new ChaptarrHistoryItem(r.BookId, r.EventType!, r.Date))
            .ToList();
    }

    public async Task<IReadOnlyList<ChaptarrBookFile>> GetBookFilesAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/bookfile?bookId={bookId}", ct: ct);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadFromJsonAsync<List<ChaptarrBookFileRaw>>(cancellationToken: ct) ?? [];
        return raw
            .Where(f => !string.IsNullOrEmpty(f.Path))
            .Select(f => new ChaptarrBookFile(f.Id, f.BookId, f.Path!))
            .ToList();
    }

    private static ChaptarrBookLookupResult Map(ChaptarrBookRaw raw)
    {
        var cover = raw.Images?.FirstOrDefault(i => i.CoverType == "cover");
        return new ChaptarrBookLookupResult
        {
            ForeignBookId = raw.ForeignBookId!,
            Title = raw.Title ?? "Unknown title",
            Overview = raw.Overview,
            SeriesTitle = raw.SeriesTitle,
            CoverUrl = cover?.RemoteUrl ?? cover?.Url,
            ReleaseDate = raw.ReleaseDate,
            AuthorName = raw.Author?.AuthorName ?? "Unknown author"
        };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string baseUrl, string apiKey, string path, JsonNode? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, baseUrl.TrimEnd('/') + path);
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await httpClient.SendAsync(request, ct);
    }
}
