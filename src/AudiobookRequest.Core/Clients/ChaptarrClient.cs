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
        await EnsureSuccessAsync(lookupResponse, "look the book up", ct);
        var results = await lookupResponse.Content.ReadFromJsonAsync<List<JsonObject>>(cancellationToken: ct) ?? [];
        var book = results.FirstOrDefault()
            ?? throw new ChaptarrBookNotFoundException(foreignBookId);

        // A book Chaptarr already has comes back from the lookup with its own id (new ones have none, or 0). Adding it
        // again is pointless, and Chaptarr can answer such a second add with a reply that carries no id.
        if (PositiveId(book) is { } existingId)
        {
            return existingId;
        }

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
        await EnsureSuccessAsync(addResponse, "add the book", ct);

        // Read the reply by hand: a body that is empty, "null" or has no id would otherwise surface as a bare
        // "object reference not set" with nothing to say what Chaptarr actually answered.
        var createdText = await addResponse.Content.ReadAsStringAsync(ct);
        if (FindId(createdText) is { } id)
        {
            return id;
        }

        // Accepted, but the reply doesn't say which book it made: ask Chaptarr again, which now knows it by id.
        using var recheck = await SendAsync(HttpMethod.Get, baseUrl, apiKey, lookupPath, ct: ct);
        if (recheck.IsSuccessStatusCode && FindId(await recheck.Content.ReadAsStringAsync(ct)) is { } found)
        {
            return found;
        }

        throw new InvalidOperationException($"Chaptarr accepted the book but didn't say what id it gave it{Snippet(createdText)}");
    }

    private static int? PositiveId(JsonObject? node) =>
        node?["id"] is JsonValue v && v.TryGetValue<int>(out var id) && id > 0 ? id : null;

    /// <summary>The new book's id from an add reply: a book object, a list of them, or one nested under "book"/"books".</summary>
    private static int? FindId(string json)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        return parsed switch
        {
            JsonObject o => PositiveId(o) ?? IdOf(o["book"]) ?? IdOf(o["books"]),
            JsonArray a => a.OfType<JsonObject>().Select(PositiveId).FirstOrDefault(i => i is not null),
            _ => null,
        };

        static int? IdOf(JsonNode? node) => node switch
        {
            JsonObject o => PositiveId(o),
            JsonArray a => a.OfType<JsonObject>().Select(PositiveId).FirstOrDefault(i => i is not null),
            _ => null,
        };
    }

    public async Task TriggerBookSearchAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["name"] = "BookSearch",
            ["bookIds"] = new JsonArray(bookId)
        };
        using var response = await SendAsync(HttpMethod.Post, baseUrl, apiKey, "/api/v1/command", body, ct);
        await EnsureSuccessAsync(response, "start the search", ct);
    }

    public async Task<IReadOnlyList<ChaptarrQueueItem>> GetQueueAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/queue?pageSize=200", ct: ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChaptarrQueueResponse>(cancellationToken: ct);
        return (result?.Records ?? [])
            .Select(r => new ChaptarrQueueItem(
                r.Title, r.BookId, r.Status ?? "unknown", r.Size, r.SizeLeft, r.DownloadClient,
                r.TrackedDownloadStatus, r.TrackedDownloadState, r.ErrorMessage,
                (r.StatusMessages ?? []).SelectMany(m => m.Messages ?? []).Distinct().Take(3).ToList(), r.DownloadId))
            .ToList();
    }

    public async Task<IReadOnlyList<ChaptarrHistoryItem>> GetHistorySinceAsync(string baseUrl, string apiKey, DateTimeOffset since, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, "/api/v1/history?pageSize=200&sortKey=date&sortDirection=descending", ct: ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChaptarrHistoryResponse>(cancellationToken: ct);
        return (result?.Records ?? [])
            .Where(r => r.Date >= since && r.EventType is not null)
            .Select(r => new ChaptarrHistoryItem(r.BookId, r.EventType!, r.Date, ReadMessage(r)))
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

    public async Task<IReadOnlyList<ChaptarrImportCandidate>> GetManualImportCandidatesAsync(string baseUrl, string apiKey, string downloadId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/manualimport?downloadId={Uri.EscapeDataString(downloadId)}", ct: ct);
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<JsonObject>>(cancellationToken: ct) ?? [];

        return items
            .Where(i => !string.IsNullOrEmpty(i["path"]?.GetValue<string>()))
            .Select(i => new ChaptarrImportCandidate(
                i["path"]!.GetValue<string>(),
                i["name"]?.GetValue<string>() ?? Path.GetFileName(i["path"]!.GetValue<string>()),
                i["size"]?.GetValue<long>() ?? 0,
                i["book"]?["id"]?.GetValue<int>(),
                i["book"]?["title"]?.GetValue<string>(),
                i["author"]?["id"]?.GetValue<int>(),
                ((i["rejections"] as JsonArray) ?? [])
                    .Select(r => r is JsonObject o ? o["reason"]?.GetValue<string>() : r?.GetValue<string>())
                    .OfType<string>()
                    .ToList(),
                i))
            .ToList();
    }

    public async Task<ChaptarrBookInfo?> GetBookInfoAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        using var bookResponse = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/book/{bookId}", ct: ct);
        if (bookResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        bookResponse.EnsureSuccessStatusCode();
        var book = await bookResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        if (book is null)
        {
            return null;
        }

        // A single-book lookup doesn't carry editions; they have their own endpoint.
        using var editionResponse = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/edition?bookId={bookId}", ct: ct);
        editionResponse.EnsureSuccessStatusCode();
        var editions = (await editionResponse.Content.ReadFromJsonAsync<List<JsonObject>>(cancellationToken: ct) ?? [])
            .Where(e => e["id"] is not null)
            .Select(e => new ChaptarrEditionInfo(
                e["id"]!.GetValue<int>(),
                e["isEbook"]?.GetValue<bool>() ?? false,
                e["monitored"]?.GetValue<bool>() ?? false))
            .ToList();

        return new ChaptarrBookInfo(bookId, book["title"]?.GetValue<string>() ?? "", book["authorId"]?.GetValue<int>() ?? 0, editions);
    }

    public async Task<int> StartManualImportAsync(string baseUrl, string apiKey, IReadOnlyList<JsonObject> files, string importMode, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["name"] = "ManualImport",
            ["files"] = new JsonArray(files.Select(f => (JsonNode)f).ToArray()),
            ["importMode"] = importMode
        };
        using var response = await SendAsync(HttpMethod.Post, baseUrl, apiKey, "/api/v1/command", body, ct);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        return created!["id"]!.GetValue<int>();
    }

    public async Task<ChaptarrCommandStatus> GetCommandStatusAsync(string baseUrl, string apiKey, int commandId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, baseUrl, apiKey, $"/api/v1/command/{commandId}", ct: ct);
        response.EnsureSuccessStatusCode();
        var command = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        return new ChaptarrCommandStatus(
            commandId,
            command?["status"]?.GetValue<string>() ?? "unknown",
            command?["result"]?.GetValue<string>(),
            command?["message"]?.GetValue<string>());
    }

    public async Task DeleteBookAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, baseUrl, apiKey,
            $"/api/v1/book/{bookId}?deleteFiles=false&addImportListExclusion=false", ct: ct);
        response.EnsureSuccessStatusCode();
    }

    private static string? ReadMessage(ChaptarrHistoryRecordRaw record)
    {
        if (record.Data is not null &&
            record.Data.TryGetValue("message", out var message) &&
            message.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var text = message.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        return null;
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
            AuthorName = raw.Author?.AuthorName ?? "Unknown author",
            RatingValue = raw.Ratings?.Value,
            RatingVotes = raw.Ratings?.Votes
        };
    }

    /// <summary>Like EnsureSuccessStatusCode, but says what Chaptarr answered -- its validation messages are the useful part.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Chaptarr wouldn't {what} (HTTP {(int)response.StatusCode}){Snippet(body)}", null, response.StatusCode);
    }

    private static string Snippet(string? body)
    {
        var text = string.Join(' ', (body ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? "." : $": {(text.Length > 300 ? text[..300] + "…" : text)}";
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
