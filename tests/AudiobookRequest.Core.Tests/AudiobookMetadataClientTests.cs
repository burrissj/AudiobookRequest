using System.Net;
using AudiobookRequest.Core.Clients;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookRequest.Core.Tests;

public class AudiobookMetadataClientTests
{
    private const string SearchHitJson = """
        {
          "products": [
            {
              "asin": "B08G9PRS1K", "title": "Project Hail Mary", "authors": [{ "name": "Andy Weir" }],
              "series": [{ "asin": "B0SERIESASIN", "title": "Standalone Series", "sequence": "1" }]
            }
          ]
        }
        """;

    private const string DetailJson = """
        {
          "runtimeLengthMin": 970,
          "narrators": [{ "name": "Ray Porter" }],
          "genres": [
            { "name": "Science Fiction & Fantasy", "type": "genre" },
            { "name": "Hard Science Fiction", "type": "tag" }
          ],
          "description": "A short tagline, not what we want.",
          "summary": "<p>The <b>full</b> HTML-formatted synopsis.</p>",
          "formatType": "unabridged",
          "language": "english",
          "publisherName": "Audible Studios"
        }
        """;

    private const string BrowseJson = """
        {
          "total_results": 24298,
          "products": [
            {
              "asin": "B0727YWRML", "title": "The Wolf of the North", "language": "english", "content_delivery_type": "SinglePartBook",
              "authors": [{ "name": "Duncan M. Hamilton" }],
              "narrators": [{ "name": "Simon Vance" }],
              "runtime_length_min": 623,
              "product_images": { "500": "https://m.media-amazon.com/images/I/51sSOJv5WML._SL500_.jpg" },
              "rating": { "overall_distribution": { "average_rating": 4.587, "num_ratings": 5707 } },
              "series": [{ "asin": "S1", "title": "Wolf of the North", "sequence": "1" }]
            },
            { "asin": "PODCAST", "title": "True Crime Weekly", "language": "english", "authors": [{ "name": "E" }],
              "content_delivery_type": "PodcastParent", "product_images": { "500": "https://img/pod.jpg" } },
            { "asin": "NOCOVER", "title": "No Cover Book", "language": "english", "authors": [{ "name": "A" }] },
            { "asin": "FRENCH", "title": "Un Livre", "language": "french", "authors": [{ "name": "B" }],
              "product_images": { "500": "https://img/fr.jpg" } },
            { "asin": "PLACEHOLDER", "title": "Untitled Book Two", "language": "english", "authors": [{ "name": "C" }],
              "product_images": { "500": "https://img/untitled.jpg" } },
            { "asin": "UNRATED", "title": "Brand New", "language": "english", "authors": [{ "name": "D" }],
              "product_images": { "500": "https://img/new.jpg" },
              "rating": { "overall_distribution": { "average_rating": 0.0, "num_ratings": 0 } } }
          ]
        }
        """;

    [Fact]
    public async Task SearchAsync_UsesAudiblesTitleAndAuthorSearch_NotKeywords()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"products":[{"asin":"B002V1OF70","title":"Dune","language":"english","content_delivery_type":"MultiPartBook","authors":[{"name":"Frank Herbert"}],"product_images":{"500":"http://img/dune.jpg"}}],"total_results":1}""");
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var page = await client.SearchAsync("Dune & Co", "Frank Herbert", 25);

        var url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        Assert.Contains("title=Dune%20%26%20Co", url);
        Assert.Contains("author=Frank%20Herbert", url);
        Assert.Contains("num_results=25", url);
        Assert.DoesNotContain("keywords=", url);
        Assert.DoesNotContain("page=", url);   // Audible pages start at 0; page=1 would skip the best results
        Assert.Equal("Dune", Assert.Single(page.Items).Title);
    }

    [Fact]
    public async Task SearchAsync_WithoutAnAuthorSendsNone()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"products":[],"total_results":0}""");
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        await client.SearchAsync("Dune", null, 10);

        Assert.DoesNotContain("author=", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SearchAsync_DropsTranslationsAndFailuresGiveAnEmptyPage()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"products":[{"asin":"F1","title":"Le Messie de Dune","language":"french","content_delivery_type":"MultiPartBook","product_images":{"500":"http://img/f.jpg"}},{"asin":"E1","title":"Dune","language":"english","content_delivery_type":"MultiPartBook","product_images":{"500":"http://img/e.jpg"}}],"total_results":2}""");
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        Assert.Equal(["E1"], (await client.SearchAsync("Dune", null, 10)).Items.Select(i => i.Asin));

        var failing = new AudiobookMetadataClient(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "")), NullLogger<AudiobookMetadataClient>.Instance);
        Assert.Empty((await failing.SearchAsync("Dune", null, 10)).Items);
    }

    [Fact]
    public async Task SearchAsync_TakesTheNumberedSeriesWhenAUniverseIsListedFirst()
    {
        // Audible lists "The Cosmere" (a whole universe, no book number) ahead of "The Mistborn Saga" (book 1).
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"products":[{"asin":"E1","title":"The Final Empire","language":"english","content_delivery_type":"MultiPartBook","product_images":{"500":"http://img/e.jpg"},"series":[{"asin":"S0","title":"The Cosmere","sequence":""},{"asin":"S1","title":"The Mistborn Saga","sequence":"1"}]},{"asin":"E2","title":"Standalone Universe Tale","language":"english","content_delivery_type":"MultiPartBook","product_images":{"500":"http://img/f.jpg"},"series":[{"asin":"S0","title":"The Cosmere","sequence":""}]}],"total_results":2}""");
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var items = (await client.SearchAsync("The Final Empire", null, 10)).Items;

        Assert.Equal(("The Mistborn Saga", "1", "S1"), (items[0].SeriesTitle, items[0].SeriesSequence, items[0].SeriesAsin));
        Assert.Equal("The Cosmere", items[1].SeriesTitle); // no numbered series: the first listed is all there is
    }

    [Fact]
    public async Task BrowseAsync_MapsCoverLengthRatingAndSeries_AndSkipsUnshowableBooks()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, BrowseJson);
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var page = await client.BrowseAsync("18580615011", "epic", "BestSellers", 2, 24);

        Assert.Equal(24298, page.TotalResults);
        // Podcasts, no cover, non-English and "Untitled" placeholders are dropped; the rest stay.
        Assert.Equal(["B0727YWRML", "UNRATED"], page.Items.Select(i => i.Asin));
        var wolf = page.Items[0];
        Assert.Equal("Duncan M. Hamilton", wolf.Author);
        Assert.Equal("https://m.media-amazon.com/images/I/51sSOJv5WML._SL500_.jpg", wolf.CoverUrl);
        Assert.Equal(623, wolf.RuntimeMinutes);
        Assert.Equal(4.587, wolf.Rating);
        Assert.Equal(5707, wolf.RatingCount);
        Assert.Equal("Wolf of the North", wolf.SeriesTitle);
        Assert.Equal(["Simon Vance"], wolf.Narrators);
        Assert.Null(page.Items[1].Rating);
    }

    [Fact]
    public async Task BrowseAsync_SendsGenreKeywordsSortAndPaging()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, BrowseJson);
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        await client.BrowseAsync("18580615011", "full cast", "-ReleaseDate", 3, 12);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("category_id=18580615011", query);
        Assert.Contains("keywords=full%20cast", query);
        Assert.Contains("products_sort_by=-ReleaseDate", query);
        Assert.Contains("page=3", query);
        Assert.Contains("num_results=12", query);
        Assert.Contains("content_type=Product", query); // audiobooks only, no podcast episodes
    }

    [Fact]
    public async Task BrowseAsync_ReturnsEmptyOnFailure_RatherThanThrowing()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "oops");
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var page = await client.BrowseAsync(null, "fantasy", "Relevance", 0, 24);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalResults);
    }

    [Fact]
    public async Task TryResolveDetailAsync_ResolvesRuntimeNarratorsAndOnlyRealGenres_WhenMatchFound()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, SearchHitJson),
            (HttpStatusCode.OK, DetailJson));
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var result = await client.TryResolveDetailAsync("Project Hail Mary", "Andy Weir");

        Assert.NotNull(result);
        Assert.Equal(970, result!.RuntimeMinutes);
        Assert.Equal(["Ray Porter"], result.Narrators);
        // "tag" entries (e.g. "Hard Science Fiction") are excluded -- only true "genre"-typed entries surface.
        Assert.Equal(["Science Fiction & Fantasy"], result.Genres);
        // "summary" (the full blurb) is used, not "description" (a short tagline) -- confirmed live against
        // Audnexus that "description" is far too short to be useful as the book's shown description.
        Assert.Equal("<p>The <b>full</b> HTML-formatted synopsis.</p>", result.Description);
        Assert.Equal("unabridged", result.FormatType);
        Assert.Equal("english", result.Language);
        Assert.Equal("Audible Studios", result.PublisherName);
        Assert.Equal("Standalone Series", result.SeriesTitle);
        Assert.Equal("B0SERIESASIN", result.SeriesAsin);
        Assert.Equal("1", result.SeriesSequence);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("api.audible.com", handler.Requests[0].RequestUri!.Host);
        Assert.Contains("api.audnex.us", handler.Requests[1].RequestUri!.Host);
    }

    [Fact]
    public async Task TryResolveDetailAsync_ReturnsNullWithoutCallingAudnexus_WhenNoAudibleMatch()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{ "products": [] }""");
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var result = await client.TryResolveDetailAsync("Some Obscure Title", "Some Author");

        Assert.Null(result);
        // The exact title-and-author search, then the title alone; never Audnexus.
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Contains("api.audible.com", r.RequestUri!.Host));
        Assert.DoesNotContain("author=", handler.Requests[1].RequestUri!.Query);
    }

    private const string EmptySearchJson = """{ "products": [] }""";

    [Fact]
    public async Task TryResolveDetailAsync_RetriesWithTheTitleCleanedOfEditionNotes()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, EmptySearchJson),
            (HttpStatusCode.OK, SearchHitJson),
            (HttpStatusCode.OK, DetailJson));
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var result = await client.TryResolveDetailAsync("Project Hail Mary (Unabridged)", "Andy Weir");

        Assert.NotNull(result);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("title=Project%20Hail%20Mary%20%28Unabridged%29", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("title=Project%20Hail%20Mary&", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("author=Andy%20Weir", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task TryResolveDetailAsync_WithNoAuthor_FindsTheBookByItsTitleAlone()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, """{ "products": [ { "asin": "OTHER", "title": "Hail Mary Pass" }, { "asin": "B08G9PRS1K", "title": "Project Hail Mary" } ] }"""),
            (HttpStatusCode.OK, DetailJson));
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var result = await client.TryResolveDetailAsync("Project Hail Mary", "");

        Assert.NotNull(result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("author=", handler.Requests[0].RequestUri!.Query);
        Assert.EndsWith("/B08G9PRS1K?region=us", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task TryResolveDetailAsync_WithNoAuthor_DoesNotTakeADifferentBook()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{ "products": [ { "asin": "OTHER", "title": "Hail Mary Pass" } ] }""");
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        Assert.Null(await client.TryResolveDetailAsync("Project Hail Mary", ""));
        Assert.All(handler.Requests, r => Assert.Contains("api.audible.com", r.RequestUri!.Host));
    }

    [Fact]
    public async Task TryResolveDetailAsync_ReturnsNull_WhenAudnexusLookupFails()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.OK, SearchHitJson),
            (HttpStatusCode.NotFound, "not found"));
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var result = await client.TryResolveDetailAsync("Project Hail Mary", "Andy Weir");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSeriesBooksAsync_MapsSeriesAsinAndSequencePerEntry()
    {
        // Mirrors the real shape confirmed live for "The Darkwood Trilogy" by Anthea Sharp: searching
        // by series title + author returns a mix of the individual books (sequence "1", "2", "3"), the
        // omnibus itself (sequence "1-3"), and unrelated noise -- the client maps all of it as-is and
        // leaves filtering to the caller.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            {
              "products": [
                { "asin": "BOOK1", "title": "White as Frost", "authors": [{ "name": "Anthea Sharp" }],
                  "series": [{ "asin": "SERIESASIN", "title": "The Darkwood Trilogy", "sequence": "1" }] },
                { "asin": "BOOK2", "title": "Black as Night", "authors": [{ "name": "Anthea Sharp" }],
                  "series": [{ "asin": "SERIESASIN", "title": "The Darkwood Trilogy", "sequence": "2" }] },
                { "asin": "OMNIBUS", "title": "The Darkwood Trilogy", "authors": [{ "name": "Anthea Sharp" }],
                  "series": [{ "asin": "SERIESASIN", "title": "The Darkwood Trilogy", "sequence": "1-3" }] }
              ]
            }
            """);
        var client = new AudiobookMetadataClient(new HttpClient(handler), NullLogger<AudiobookMetadataClient>.Instance);

        var results = await client.GetSeriesBooksAsync("The Darkwood Trilogy", "Anthea Sharp");

        Assert.Equal(3, results.Count);
        Assert.Equal("1", results[0].SeriesSequence);
        Assert.Equal("SERIESASIN", results[0].SeriesAsin);
        Assert.Equal("1-3", results[2].SeriesSequence);
    }
}
