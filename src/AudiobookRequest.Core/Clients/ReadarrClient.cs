using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AudiobookRequest.Core.Clients;

public class ReadarrClient(HttpClient httpClient) : IReadarrClient
{
    public async Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/system/status", ct: ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<ReadarrBookLookupResult>> LookupBooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default)
    {
        var path = $"/api/v1/book/lookup?term={Uri.EscapeDataString(term)}";
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, path, ct: ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadFromJsonAsync<List<ReadarrBookRaw>>(cancellationToken: ct) ?? [];
        return raw
            .Where(b => !string.IsNullOrEmpty(b.ForeignBookId))
            .Select(Map)
            .ToList();
    }

    public async Task<string?> FindForeignAuthorIdAsync(string baseUrl, string apiKey, string authorName, CancellationToken ct = default)
    {
        var path = $"/api/v1/author/lookup?term={Uri.EscapeDataString(authorName)}";
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, path, ct: ct);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<JsonObject>>(cancellationToken: ct) ?? [];
        return results.FirstOrDefault()?["foreignAuthorId"]?.GetValue<string>();
    }

    public async Task<int> EnsureAuthorAsync(string baseUrl, string apiKey, string foreignAuthorId, string authorName, ReadarrAddAuthorOptions options, CancellationToken ct = default)
    {
        using var listResponse = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/author", ct: ct);
        listResponse.EnsureSuccessStatusCode();
        var authors = await listResponse.Content.ReadFromJsonAsync<List<JsonObject>>(cancellationToken: ct) ?? [];
        var existing = authors.FirstOrDefault(a => a["foreignAuthorId"]?.GetValue<string>() == foreignAuthorId);
        if (existing is not null)
        {
            return existing["id"]!.GetValue<int>();
        }

        var body = new JsonObject
        {
            ["foreignAuthorId"] = foreignAuthorId,
            // Some metadata providers leave a field null that Readarr's own author-folder naming
            // template dereferences unconditionally, crashing its add-author validation with a 500
            // (NullReferenceException in FileNameBuilder.GetAuthorFolder) rather than a clean error.
            // Supplying authorName + an explicit path avoids Readarr computing the folder itself.
            ["authorName"] = authorName,
            ["path"] = CombineAuthorPath(options.RootFolderPath, authorName),
            ["rootFolderPath"] = options.RootFolderPath,
            ["qualityProfileId"] = options.QualityProfileId,
            ["metadataProfileId"] = options.MetadataProfileId,
            ["monitored"] = true,
            ["addOptions"] = new JsonObject
            {
                ["monitor"] = "none",
                ["searchForMissingBooks"] = false
            }
        };

        using var addResponse = await SendAsync(HttpMethod.Post, baseUrl, apiKey, "/api/v1/author", body, ct);
        addResponse.EnsureSuccessStatusCode();
        var created = await addResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        return created!["id"]!.GetValue<int>();
    }

    private static string CombineAuthorPath(string rootFolderPath, string authorName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(authorName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        var separator = rootFolderPath.Contains('\\') ? '\\' : '/';
        return rootFolderPath.TrimEnd('/', '\\') + separator + sanitized;
    }

    public async Task<int?> FindBookIdAsync(string baseUrl, string apiKey, int authorId, string foreignBookId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/book?authorId={authorId}", ct: ct);
        response.EnsureSuccessStatusCode();
        var books = await response.Content.ReadFromJsonAsync<List<JsonObject>>(cancellationToken: ct) ?? [];
        var match = books.FirstOrDefault(b => b["foreignBookId"]?.GetValue<string>() == foreignBookId);
        return match?["id"]?.GetValue<int>();
    }

    public async Task MarkBookMonitoredAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        using var getResponse = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/book/{bookId}", ct: ct);
        getResponse.EnsureSuccessStatusCode();
        var book = await getResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        book!["monitored"] = true;

        using var putResponse = await SendAsync(HttpMethod.Put, baseUrl, apiKey, $"/api/v1/book/{bookId}", book, ct);
        putResponse.EnsureSuccessStatusCode();
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

    public async Task<IReadOnlyList<ReadarrQueueItem>> GetQueueAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/queue?pageSize=200", ct: ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ReadarrQueueResponse>(cancellationToken: ct);
        return (result?.Records ?? [])
            .Select(r => new ReadarrQueueItem(r.Title, r.BookId, r.Status ?? "unknown", r.SizeLeft))
            .ToList();
    }

    public async Task<IReadOnlyList<ReadarrHistoryItem>> GetHistorySinceAsync(string baseUrl, string apiKey, DateTimeOffset since, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/history?pageSize=200&sortKey=date&sortDirection=descending", ct: ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ReadarrHistoryResponse>(cancellationToken: ct);
        return (result?.Records ?? [])
            .Where(r => r.Date >= since && r.EventType is not null)
            .Select(r => new ReadarrHistoryItem(r.BookId, r.EventType!, r.Date))
            .ToList();
    }

    public async Task<IReadOnlyList<ReadarrBookFile>> GetBookFilesAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/bookfile?bookId={bookId}", ct: ct);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadFromJsonAsync<List<ReadarrBookFileRaw>>(cancellationToken: ct) ?? [];
        return raw
            .Where(f => !string.IsNullOrEmpty(f.Path))
            .Select(f => new ReadarrBookFile(f.Id, f.BookId, f.Path!))
            .ToList();
    }

    private static ReadarrBookLookupResult Map(ReadarrBookRaw raw)
    {
        var cover = raw.Images?.FirstOrDefault(i => i.CoverType == "cover");
        var title = raw.Title ?? "Unknown title";
        return new ReadarrBookLookupResult
        {
            ForeignBookId = raw.ForeignBookId!,
            Title = title,
            Overview = raw.Overview,
            SeriesTitle = raw.SeriesTitle,
            CoverUrl = cover?.RemoteUrl ?? cover?.Url,
            ReleaseDate = raw.ReleaseDate,
            // The original Readarr metadata provider nests author info directly; some alternatives
            // adopted after Readarr's EOL only give a flattened "authorname title" string instead.
            ForeignAuthorId = raw.Author?.ForeignAuthorId,
            AuthorName = raw.Author?.AuthorName ?? ExtractAuthorName(raw.AuthorTitle, title)
        };
    }

    /// <summary>
    /// Derives a display-ready author name from a provider's flattened "authorname title" string
    /// (e.g. "weir, andy Project Hail Mary" for title "Project Hail Mary" -> "Andy Weir").
    /// </summary>
    private static string ExtractAuthorName(string? authorTitle, string title)
    {
        if (string.IsNullOrWhiteSpace(authorTitle))
        {
            return "Unknown author";
        }

        var titleIndex = authorTitle.LastIndexOf(title, StringComparison.OrdinalIgnoreCase);
        var namePart = (titleIndex >= 0 ? authorTitle[..titleIndex] : authorTitle).Trim();
        if (namePart.Length == 0)
        {
            return "Unknown author";
        }

        var commaIndex = namePart.IndexOf(',');
        if (commaIndex > 0)
        {
            var last = namePart[..commaIndex].Trim();
            var first = namePart[(commaIndex + 1)..].Trim();
            namePart = $"{first} {last}";
        }

        return string.Join(' ', namePart.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string baseUrl, string apiKey, string path, JsonObject? body = null, CancellationToken ct = default)
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
