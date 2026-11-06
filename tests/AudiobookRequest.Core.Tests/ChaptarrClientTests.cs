using System.Net;
using AudiobookRequest.Core.Clients;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class ChaptarrClientTests
{
    // Trimmed from a real Chaptarr instance's response shape.
    private const string LookupResponseJson = """
    [
      {
        "title": "Project Hail Mary",
        "authorTitle": " Project Hail Mary",
        "overview": "Ryland Grace is the sole survivor...",
        "foreignBookId": "gr:79106958",
        "mediaType": "audiobook",
        "images": [
          { "coverType": "cover", "url": "/cover.jpg", "remoteUrl": "https://covers.example/hailmary.jpg" }
        ],
        "author": { "authorName": "Andy Weir" }
      },
      {
        "title": "Project Hail Mary (ebook)",
        "foreignBookId": "gr:79106958-ebook",
        "mediaType": "ebook",
        "author": { "authorName": "Andy Weir" }
      }
    ]
    """;

    [Fact]
    public async Task LookupBooksAsync_MapsResultAndFiltersToAudiobookMediaType()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, LookupResponseJson);
        var client = new ChaptarrClient(new HttpClient(handler));

        var results = await client.LookupBooksAsync("http://chaptarr:8789", "test-key", "hail mary");

        Assert.Single(results);
        var result = results[0];
        Assert.Equal("gr:79106958", result.ForeignBookId);
        Assert.Equal("Project Hail Mary", result.Title);
        Assert.Equal("Andy Weir", result.AuthorName);
        Assert.Equal("https://covers.example/hailmary.jpg", result.CoverUrl);
    }

    [Fact]
    public async Task LookupBooksAsync_MapsRatings()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            [
              {
                "title": "Project Hail Mary",
                "foreignBookId": "gr:79106958",
                "mediaType": "audiobook",
                "ratings": { "votes": 1888251, "value": 4.51 },
                "author": { "authorName": "Andy Weir" }
              }
            ]
            """);
        var client = new ChaptarrClient(new HttpClient(handler));

        var results = await client.LookupBooksAsync("http://chaptarr:8789", "test-key", "hail mary");

        var result = Assert.Single(results);
        Assert.Equal(4.51, result.RatingValue);
        Assert.Equal(1888251, result.RatingVotes);
    }

    [Fact]
    public async Task FindRootFolderAsync_MatchesByPathIgnoringTrailingSlashAndCase()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            [
              { "path": "/audiobooks/", "audiobookQualityProfileId": 2, "audiobookMetadataProfileId": 1 },
              { "path": "/ebooks", "audiobookQualityProfileId": 1, "audiobookMetadataProfileId": 2 }
            ]
            """);
        var client = new ChaptarrClient(new HttpClient(handler));

        var folder = await client.FindRootFolderAsync("http://chaptarr:8789", "test-key", "/AudioBooks");

        Assert.NotNull(folder);
        Assert.Equal("/audiobooks/", folder!.Path);
        Assert.Equal(2, folder.AudiobookQualityProfileId);
        Assert.Equal(1, folder.AudiobookMetadataProfileId);
    }

    [Fact]
    public async Task FindRootFolderAsync_ReturnsNullWhenNoMatch()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = new ChaptarrClient(new HttpClient(handler));

        var folder = await client.FindRootFolderAsync("http://chaptarr:8789", "test-key", "/nope");

        Assert.Null(folder);
    }

    [Fact]
    public async Task AddBookAsync_RefetchesByForeignIdAndFillsInMonitoringAndProfileFields()
    {
        // Regression test: confirmed live against a real Chaptarr instance that a single POST with
        // the re-fetched, filled-in resource atomically creates both author and book -- no separate
        // "add author, wait for bibliography sync" dance like Readarr needed.
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, """
                [
                  {
                    "title": "Project Hail Mary",
                    "foreignBookId": "gr:79106958",
                    "monitored": false,
                    "audiobookMonitored": false,
                    "author": { "authorName": "Andy Weir", "foreignAuthorId": "gr:6540057", "monitored": false },
                    "editions": [
                      { "foreignEditionId": "gr:1", "isEbook": false, "monitored": false },
                      { "foreignEditionId": "gr:2", "isEbook": true, "monitored": false }
                    ]
                  }
                ]
                """),
            (HttpStatusCode.Created, """{ "id": 2833 }"""));
        var client = new ChaptarrClient(new HttpClient(handler));
        var rootFolder = new ChaptarrRootFolder("/audiobooks", AudiobookQualityProfileId: 2, AudiobookMetadataProfileId: 1);

        var id = await client.AddBookAsync("http://chaptarr:8789", "test-key", "gr:79106958", rootFolder);

        Assert.Equal(2833, id);
        Assert.Equal("http://chaptarr:8789/api/v1/book/lookup?term=gr%3A79106958", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("http://chaptarr:8789/api/v1/book", handler.Requests[1].RequestUri!.AbsoluteUri);

        var postBody = handler.RequestBodies[1]!;
        Assert.Contains("\"monitored\":true", postBody);
        Assert.Contains("\"audiobookMonitored\":true", postBody);
        Assert.Contains("\"rootFolderPath\":\"/audiobooks\"", postBody);
        Assert.Contains("\"qualityProfileId\":2", postBody);
        Assert.Contains("\"metadataProfileId\":1", postBody);
    }

    private const string LookupHit = """[{ "title": "T", "foreignBookId": "gr:1", "author": { "authorName": "A" }, "editions": [] }]""";

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "title": "no id here" }""")]
    public async Task AddBookAsync_SaysSoWhenChaptarrAcceptsTheBookButGivesNoId(string reply)
    {
        // Used to surface as "Object reference not set to an instance of an object".
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, LookupHit), (HttpStatusCode.OK, reply));
        var client = new ChaptarrClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.AddBookAsync("http://chaptarr:8789", "k", "gr:1", new ChaptarrRootFolder("/audiobooks", 1, 1)));

        Assert.Contains("didn't say what id", ex.Message);
    }

    private static ChaptarrRootFolder Root => new("/audiobooks", 1, 1);

    [Fact]
    public async Task AddBookAsync_UsesTheIdOfABookChaptarrAlreadyHasWithoutAddingItAgain()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """[{ "id": 4410, "title": "T", "foreignBookId": "gr:1", "author": { "authorName": "A" } }]""");
        var client = new ChaptarrClient(new HttpClient(handler));

        var id = await client.AddBookAsync("http://chaptarr:8789", "k", "gr:1", Root);

        Assert.Equal(4410, id);
        Assert.Single(handler.Requests); // only the lookup, no POST
    }

    [Theory]
    [InlineData("""{ "id": 0, "title": "T" }""")]
    public async Task AddBookAsync_ABookWithIdZeroIsNew(string lookup)
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, $"[{lookup}]"), (HttpStatusCode.Created, """{ "id": 91 }"""));
        var client = new ChaptarrClient(new HttpClient(handler));

        Assert.Equal(91, await client.AddBookAsync("http://chaptarr:8789", "k", "gr:1", Root));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("""[{ "id": 77, "title": "T" }]""")]
    [InlineData("""{ "book": { "id": 77 } }""")]
    [InlineData("""{ "books": [{ "id": 77 }] }""")]
    public async Task AddBookAsync_FindsTheIdInTheOtherShapesAnAddReplyCanTake(string reply)
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, LookupHit), (HttpStatusCode.OK, reply));
        var client = new ChaptarrClient(new HttpClient(handler));

        Assert.Equal(77, await client.AddBookAsync("http://chaptarr:8789", "k", "gr:1", Root));
    }

    [Fact]
    public async Task AddBookAsync_AsksChaptarrAgainWhenTheAddReplyHasNoId()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, LookupHit),
            (HttpStatusCode.OK, ""),
            (HttpStatusCode.OK, """[{ "id": 5150, "title": "T", "foreignBookId": "gr:1" }]"""));
        var client = new ChaptarrClient(new HttpClient(handler));

        Assert.Equal(5150, await client.AddBookAsync("http://chaptarr:8789", "k", "gr:1", Root));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task AddBookAsync_RelaysChaptarrsOwnReasonWhenItRefusesTheBook()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, LookupHit),
            (HttpStatusCode.BadRequest, """[{ "propertyName": "ForeignBookId", "errorMessage": "This book has already been added" }]"""));
        var client = new ChaptarrClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.AddBookAsync("http://chaptarr:8789", "k", "gr:1", new ChaptarrRootFolder("/audiobooks", 1, 1)));

        Assert.Contains("wouldn't add the book (HTTP 400)", ex.Message);
        Assert.Contains("already been added", ex.Message);
    }

    [Fact]
    public async Task TriggerBookSearchAsync_RelaysChaptarrsReasonWhenItRefuses()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Conflict, """{ "message": "A search is already running" }""");
        var client = new ChaptarrClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.TriggerBookSearchAsync("http://chaptarr:8789", "k", 5));

        Assert.Contains("wouldn't start the search (HTTP 409)", ex.Message);
        Assert.Contains("already running", ex.Message);
    }

    [Fact]
    public async Task GetQueueAsync_MapsSizeAndDownloadClientForProgressCalculation()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            {
              "records": [
                { "title": "Project Hail Mary", "bookId": 2833, "status": "downloading", "size": 1000.0, "sizeleft": 250.0, "downloadClient": "Transmission" }
              ]
            }
            """);
        var client = new ChaptarrClient(new HttpClient(handler));

        var queue = await client.GetQueueAsync("http://chaptarr:8789", "test-key");

        var item = Assert.Single(queue);
        Assert.Equal(2833, item.BookId);
        Assert.Equal("downloading", item.Status);
        Assert.Equal(1000.0, item.SizeBytes);
        Assert.Equal(250.0, item.SizeLeftBytes);
        Assert.Equal("Transmission", item.DownloadClient);
    }

    [Fact]
    public async Task GetQueueAsync_CapturesBlockedImportDiagnostics()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            {
              "records": [
                {
                  "title": "Mistborn - The Final Empire", "bookId": 3368, "status": "completed", "size": 100.0, "sizeleft": 0.0,
                  "trackedDownloadStatus": "warning", "trackedDownloadState": "importBlocked",
                  "errorMessage": "Rejected: matched a different book.",
                  "statusMessages": [
                    { "title": "P01.mp3", "messages": ["Rejected: matched a different book.", "Second problem"] },
                    { "title": "P02.mp3", "messages": ["Rejected: matched a different book."] }
                  ]
                }
              ]
            }
            """);
        var client = new ChaptarrClient(new HttpClient(handler));

        var item = Assert.Single(await client.GetQueueAsync("http://chaptarr:8789", "test-key"));

        Assert.Equal("warning", item.TrackedDownloadStatus);
        Assert.Equal("importBlocked", item.TrackedDownloadState);
        Assert.Equal("Rejected: matched a different book.", item.ErrorMessage);
        Assert.Equal(["Rejected: matched a different book.", "Second problem"], item.Messages);
    }

    [Fact]
    public async Task GetHistorySinceAsync_ReadsFailureMessageFromEventData()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            {
              "records": [
                { "bookId": 3368, "eventType": "bookFileConversionFailed", "date": "2026-10-01T19:12:05Z",
                  "data": { "message": "Conversion skipped.", "sourceFileCount": "6" } },
                { "bookId": 3368, "eventType": "grabbed", "date": "2026-10-01T19:07:01Z", "data": { "size": 12 } }
              ]
            }
            """);
        var client = new ChaptarrClient(new HttpClient(handler));

        var history = await client.GetHistorySinceAsync("http://chaptarr:8789", "test-key", DateTimeOffset.MinValue);

        Assert.Equal("Conversion skipped.", history.Single(h => h.EventType == "bookFileConversionFailed").Message);
        Assert.Null(history.Single(h => h.EventType == "grabbed").Message);
    }

    [Fact]
    public async Task GetQueueAsync_CapturesTheDownloadIdNeededForManualImport()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            { "records": [ { "title": "Part 2", "bookId": 3636, "status": "completed", "size": 1.0, "sizeleft": 0.0, "downloadId": "9BE490C4" } ] }
            """);
        var client = new ChaptarrClient(new HttpClient(handler));

        var item = Assert.Single(await client.GetQueueAsync("http://chaptarr:8789", "k"));

        Assert.Equal("9BE490C4", item.DownloadId);
    }

    [Fact]
    public async Task GetManualImportCandidatesAsync_ReadsTheMatchedBookAndKeepsTheRawRecord()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            [
              {
                "path": "/downloads/Part 2/part2.m4b", "name": "part2", "size": 638731591,
                "author": { "id": 30, "authorName": "Amber V. Nicole" },
                "book": { "id": 3628, "title": "The Throne of Broken Gods" },
                "quality": { "quality": { "id": 12, "name": "M4B" } },
                "rejections": [ { "reason": "Not an upgrade", "type": "permanent" } ],
                "downloadId": "9BE490C4"
              },
              { "name": "no path, ignored" }
            ]
            """);
        var client = new ChaptarrClient(new HttpClient(handler));

        var candidates = await client.GetManualImportCandidatesAsync("http://chaptarr:8789", "k", "9BE490C4");

        var c = Assert.Single(candidates);
        Assert.Equal("/downloads/Part 2/part2.m4b", c.Path);
        Assert.Equal(638731591, c.SizeBytes);
        Assert.Equal(3628, c.MatchedBookId);
        Assert.Equal("The Throne of Broken Gods", c.MatchedBookTitle);
        Assert.Equal(30, c.MatchedAuthorId);
        Assert.Equal(["Not an upgrade"], c.Rejections);
        Assert.Equal(12, c.Raw["quality"]!["quality"]!["id"]!.GetValue<int>());
        Assert.Contains("downloadId=9BE490C4", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetBookInfoAsync_CombinesTheBookWithItsEditionsFromTheEditionEndpoint()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, """{ "id": 3636, "title": "Part 2 of 2", "authorId": 30 }"""),
            (HttpStatusCode.OK, """
                [ { "id": 23267, "isEbook": false, "monitored": true }, { "id": 23270, "isEbook": true, "monitored": false } ]
                """));
        var client = new ChaptarrClient(new HttpClient(handler));

        var book = await client.GetBookInfoAsync("http://chaptarr:8789", "k", 3636);

        Assert.NotNull(book);
        Assert.Equal(30, book.AuthorId);
        Assert.Equal([23267, 23270], book.Editions.Select(e => e.Id));
        Assert.True(book.Editions[1].IsEbook);
        Assert.Contains("edition?bookId=3636", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetBookInfoAsync_ReturnsNullWhenChaptarrDoesNotHaveTheBook()
    {
        var client = new ChaptarrClient(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{}")));

        Assert.Null(await client.GetBookInfoAsync("http://chaptarr:8789", "k", 999));
    }

    [Fact]
    public async Task StartManualImportAsync_PostsTheManualImportCommandWithTheFilesAndMode()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{ "id": 27701, "name": "ManualImport", "status": "queued" }""");
        var client = new ChaptarrClient(new HttpClient(handler));
        var file = System.Text.Json.Nodes.JsonNode.Parse("""{ "path": "/downloads/a.m4b", "bookId": 3636, "authorId": 30 }""")!.AsObject();

        var commandId = await client.StartManualImportAsync("http://chaptarr:8789", "k", [file], "auto");

        Assert.Equal(27701, commandId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/api/v1/command", handler.LastRequest.RequestUri!.AbsolutePath);
        var body = System.Text.Json.Nodes.JsonNode.Parse(handler.LastRequestBody!)!;
        Assert.Equal("ManualImport", body["name"]!.GetValue<string>());
        Assert.Equal("auto", body["importMode"]!.GetValue<string>());
        Assert.Equal(3636, body["files"]![0]!["bookId"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("completed", "successful", true, true)]
    [InlineData("completed", "unsuccessful", true, false)]
    [InlineData("failed", null, true, false)]
    [InlineData("started", null, false, false)]
    public async Task GetCommandStatusAsync_ReportsWhetherTheCommandFinishedAndSucceeded(string status, string? result, bool finished, bool succeeded)
    {
        var resultJson = result is null ? "null" : $"\"{result}\"";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, $$"""{ "id": 5, "status": "{{status}}", "result": {{resultJson}}, "message": "why" }""");
        var client = new ChaptarrClient(new HttpClient(handler));

        var command = await client.GetCommandStatusAsync("http://chaptarr:8789", "k", 5);

        Assert.Equal(finished, command.IsFinished);
        Assert.Equal(succeeded, command.Succeeded);
        Assert.Equal("why", command.Message);
    }
}
