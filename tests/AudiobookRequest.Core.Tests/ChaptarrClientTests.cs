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
}
