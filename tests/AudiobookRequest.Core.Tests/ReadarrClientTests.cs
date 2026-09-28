using System.Net;
using AudiobookRequest.Core.Clients;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class ReadarrClientTests
{
    private const string NestedAuthorLookupResponseJson = """
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
      }
    ]
    """;

    // Captured from a real Readarr instance after its operator switched to an alternative metadata
    // provider post-EOL: no nested "author" object at all, just a flattened "authorname title" string.
    private const string FlattenedAuthorTitleLookupResponseJson = """
    [
      {
        "title": "Project Hail Mary",
        "authorTitle": "weir, andy Project Hail Mary",
        "seriesTitle": "",
        "authorId": 0,
        "foreignBookId": "79106958",
        "images": [
          { "url": "/MediaCoverProxy/x/54493401.jpg", "coverType": "cover", "extension": ".jpg", "remoteUrl": "https://covers.example/hailmary2.jpg" }
        ]
      },
      {
        "title": "Pressprint Book",
        "authorTitle": "pressprint Pressprint Book",
        "foreignBookId": "90988498"
      }
    ]
    """;

    [Fact]
    public async Task LookupBooksAsync_UsesNestedAuthorInfoWhenPresent()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, NestedAuthorLookupResponseJson);
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
    public async Task LookupBooksAsync_DerivesAuthorNameFromFlattenedAuthorTitleWhenNoNestedAuthor()
    {
        // Regression test: this metadata provider shape (no nested author) previously made every
        // result look "missing an author" and get filtered out entirely, so search returned nothing.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, FlattenedAuthorTitleLookupResponseJson);
        var client = new ReadarrClient(new HttpClient(handler));

        var results = await client.LookupBooksAsync("http://readarr:8787", "test-key", "hail mary");

        Assert.Equal(2, results.Count);

        var hailMary = results[0];
        Assert.Equal("79106958", hailMary.ForeignBookId);
        Assert.Null(hailMary.ForeignAuthorId);
        Assert.Equal("Andy Weir", hailMary.AuthorName);
        Assert.Equal("https://covers.example/hailmary2.jpg", hailMary.CoverUrl);

        var pressprint = results[1];
        Assert.Equal("Pressprint", pressprint.AuthorName);
    }

    [Fact]
    public async Task FindForeignAuthorIdAsync_ReturnsTopMatch()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            [ { "foreignAuthorId": "6540057", "authorName": "Andy Weir" } ]
            """);
        var client = new ReadarrClient(new HttpClient(handler));

        var id = await client.FindForeignAuthorIdAsync("http://readarr:8787", "test-key", "Andy Weir");

        Assert.Equal("6540057", id);
        Assert.Contains("/api/v1/author/lookup?term=Andy%20Weir", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task FindForeignAuthorIdAsync_ReturnsNullWhenNoMatches()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = new ReadarrClient(new HttpClient(handler));

        var id = await client.FindForeignAuthorIdAsync("http://readarr:8787", "test-key", "Nobody Writer");

        Assert.Null(id);
    }

    [Fact]
    public async Task EnsureAuthorAsync_SendsAuthorNameAndExplicitPathToAvoidReadarrCrash()
    {
        // Regression test: a real Readarr instance (post-EOL, alternative metadata provider) returns
        // a 500 from a NullReferenceException in its own author-folder naming template when adding an
        // author without authorName + an explicit path -- it tries to compute the folder itself for a
        // validation check and dereferences a field the new provider left null. Confirmed live against
        // that server that including both sidesteps it.
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.Created, """{ "id": 12 }"""));
        var client = new ReadarrClient(new HttpClient(handler));
        var options = new ReadarrAddAuthorOptions(@"S:\Media Server\AudioBookStaging", 1, 1);

        var id = await client.EnsureAuthorAsync("http://readarr:8787", "test-key", "7539785", "Rebecca Yarros", options);

        Assert.Equal(12, id);
        var postBody = handler.RequestBodies[1]!;
        Assert.Contains("\"authorName\":\"Rebecca Yarros\"", postBody);
        Assert.Contains("\"path\":\"S:\\\\Media Server\\\\AudioBookStaging\\\\Rebecca Yarros\"", postBody);
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
