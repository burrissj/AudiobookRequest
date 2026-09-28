using System.Net;
using AudiobookRequest.Core.Clients;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class ReadarrClientTests
{
    private const string LookupResponseJson = """
    [
      {
        "title": "Project Hail Mary",
        "foreignBookId": "book-123",
        "overview": "A lone astronaut must save the earth.",
        "seriesTitle": null,
        "releaseDate": "2021-05-04T00:00:00Z",
        "images": [
          { "coverType": "cover", "url": "/cover.jpg", "remoteUrl": "https://covers.example/hailmary.jpg" }
        ],
        "author": {
          "foreignAuthorId": "author-456",
          "authorName": "Andy Weir"
        }
      },
      {
        "title": "No author book",
        "foreignBookId": "book-789",
        "author": null
      }
    ]
    """;

    [Fact]
    public async Task LookupBooksAsync_MapsReadarrResponseAndSkipsEntriesMissingAuthor()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, LookupResponseJson);
        var client = new ReadarrClient(new HttpClient(handler));

        var results = await client.LookupBooksAsync("http://readarr:8787", "test-key", "hail mary");

        Assert.Single(results);
        var result = results[0];
        Assert.Equal("book-123", result.ForeignBookId);
        Assert.Equal("Project Hail Mary", result.Title);
        Assert.Equal("author-456", result.ForeignAuthorId);
        Assert.Equal("Andy Weir", result.AuthorName);
        Assert.Equal("https://covers.example/hailmary.jpg", result.CoverUrl);
    }

    [Fact]
    public async Task LookupBooksAsync_SendsApiKeyHeaderAndEscapesTerm()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = new ReadarrClient(new HttpClient(handler));

        await client.LookupBooksAsync("http://readarr:8787/", "my-key", "hail mary & foo");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("my-key", handler.LastRequest!.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("http://readarr:8787/api/v1/book/lookup?term=hail%20mary%20%26%20foo", handler.LastRequest.RequestUri!.AbsoluteUri);
    }
}
