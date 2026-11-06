using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Recommendations;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Tests;

public sealed class CatalogBrowseTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeMetadataClient metadata = new();
    private readonly FakeChaptarrClient chaptarr = new();
    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    private CatalogBrowseService Service() =>
        new(t.Db, t.Settings, metadata, chaptarr, new LibraryCatalogService(t.Db, t.Settings, new FakeJellyfinClient()), cache);

    private static AudiobookListing Listing(string asin, string title, string author = "An Author") =>
        new(asin, title, author, $"https://img/{asin}.jpg", 600, 4.5, 100, null, null, []);

    [Fact]
    public async Task Home_FetchesEachCategoryOnceWithLayoutSizedPreviews_AndCachesThem()
    {
        metadata.Page = new AudiobookPage([Listing("A1", "Book One")], 5000);
        var service = Service();

        var first = await service.HomeAsync(seed: 5, count: 4, default);
        var callsAfterFirst = metadata.BrowseCalls.Count;
        await service.HomeAsync(seed: 5, count: 4, default);

        Assert.Equal(4, first.Count);
        Assert.Equal(4, callsAfterFirst);
        Assert.Equal(callsAfterFirst, metadata.BrowseCalls.Count); // second visit served from cache
        Assert.All(first, s => Assert.Equal(5000, s.TotalTitles));
        foreach (var call in metadata.BrowseCalls)
        {
            Assert.Contains(call.Size, new[] { 8, 10, 12 });
        }
    }

    [Fact]
    public async Task Home_DropsCategoriesThatCameBackEmpty()
    {
        metadata.Page = new AudiobookPage([], 0);

        var sections = await Service().HomeAsync(5, 4, default);

        Assert.Empty(sections);
    }

    [Fact]
    public async Task Page_FlagsOwnedSimilarAndAlreadyRequestedBooks()
    {
        t.Db.LibraryBooks.Add(new LibraryBookCache { JellyfinItemId = "j1", Title = "Quicksilver", Author = "Callie Hart", Artists = "Callie Hart" });
        t.Db.LibraryBooks.Add(new LibraryBookCache { JellyfinItemId = "j2", Title = "The Final Empire", Author = "Brandon Sanderson", Artists = "Brandon Sanderson" });
        t.Db.SaveChanges();
        t.AddRequest(t.Victoria, BookRequestStatus.Searching, "Gideon the Ninth", r => r.Author = "Tamsyn Muir");
        metadata.Page = new AudiobookPage(
        [
            Listing("A", "Quicksilver (Unabridged)", "Callie Hart"),
            Listing("B", "Mistborn: The Final Empire", "Brandon Sanderson"),
            Listing("C", "Gideon the Ninth", "Tamsyn Muir"),
            Listing("D", "Something New", "Nobody"),
        ], 4);

        var page = await Service().PageAsync(CategoryCatalog.Find("spooky")!, null, 0, default);

        var byAsin = page.Items.ToDictionary(i => i.Asin);
        Assert.True(byAsin["A"].AlreadyInLibrary);
        Assert.False(byAsin["B"].AlreadyInLibrary);
        Assert.Equal("The Final Empire", byAsin["B"].SimilarInLibraryTitle);
        Assert.True(byAsin["C"].AlreadyRequested);
        Assert.False(byAsin["D"].AlreadyInLibrary);
        Assert.False(byAsin["D"].AlreadyRequested);
        Assert.Null(byAsin["D"].SimilarInLibraryTitle);
    }

    [Fact]
    public async Task Page_DeniedRequestsDoNotCountAsRequested()
    {
        t.AddRequest(t.Victoria, BookRequestStatus.Denied, "Declined Book", r => r.Author = "Someone");
        metadata.Page = new AudiobookPage([Listing("A", "Declined Book", "Someone")], 1);

        var page = await Service().PageAsync(CategoryCatalog.Find("spooky")!, null, 0, default);

        Assert.False(page.Items[0].AlreadyRequested);
    }

    [Fact]
    public async Task Page_UsesTheCategorysSortUnlessAValidOverrideIsGiven_AndReportsMore()
    {
        metadata.Page = new AudiobookPage(Enumerable.Range(0, 24).Select(i => Listing($"A{i}", $"Book {i}")).ToList(), 100);
        var service = Service();
        var category = CategoryCatalog.Find("spooky")!;

        var defaultSort = await service.PageAsync(category, null, 0, default);
        await service.PageAsync(category, "-ReleaseDate", 1, default);
        await service.PageAsync(category, "DROP TABLE;", 2, default);

        Assert.Equal("BestSellers", metadata.BrowseCalls[0].Sort);
        Assert.Equal("-ReleaseDate", metadata.BrowseCalls[1].Sort);
        Assert.Equal("BestSellers", metadata.BrowseCalls[2].Sort); // unknown sort ignored
        Assert.True(defaultSort.HasMore);
        Assert.Equal(1, metadata.BrowseCalls[1].PageNumber);
    }

    [Fact]
    public async Task Page_LastPageHasNoMore()
    {
        metadata.Page = new AudiobookPage([Listing("A", "Only Book")], 1);

        var page = await Service().PageAsync(CategoryCatalog.Find("spooky")!, null, 0, default);

        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Page_CustomSearchCategoryQueriesByKeywords()
    {
        metadata.Page = new AudiobookPage([Listing("A", "Dragon Book")], 1);

        await Service().PageAsync(BrowseCategory.Custom("dragons"), null, 0, default);

        Assert.Null(metadata.BrowseCalls[0].CategoryId);
        Assert.Equal("dragons", metadata.BrowseCalls[0].Keywords);
        Assert.Equal("Relevance", metadata.BrowseCalls[0].Sort);
    }

    [Fact]
    public async Task Page_AuthorCategory_QueriesByAuthorAndKeepsOnlyBooksCreditingThemIncludingCoAuthored()
    {
        metadata.Page = new AudiobookPage(
        [
            Listing("A", "Solo Book", "Brandon Sanderson") with { AllAuthors = ["Brandon Sanderson"] },
            Listing("B", "Co-written", "Janci Patterson") with { AllAuthors = ["Janci Patterson", "Brandon Sanderson"] },
            Listing("C", "Fuzzy Match Noise", "Brandon Taylor") with { AllAuthors = ["Brandon Taylor"] },
        ], 3);

        var page = await Service().PageAsync(BrowseCategory.ForAuthor("Brandon Sanderson"), null, 0, default);

        Assert.Equal(["A", "B"], page.Items.Select(i => i.Asin));
        Assert.Equal("Brandon Sanderson", page.Category.Title);
        Assert.Equal("Brandon Sanderson", metadata.BrowseAuthors[0]);
        Assert.Null(metadata.BrowseCalls[0].Keywords);
    }

    private static ChaptarrBookLookupResult Hit(string id, string title, string author) =>
        new() { ForeignBookId = id, Title = title, AuthorName = author };

    [Fact]
    public async Task Resolve_PicksTheSameAuthorsBookOverALookalike()
    {
        chaptarr.Lookups["Quicksilver"] =
        [
            Hit("gr:wrong", "Quicksilver", "Neal Stephenson"),
            Hit("gr:right", "Quicksilver (Fae & Alchemy, #1)", "Callie Hart"),
        ];

        var match = await Service().ResolveAsync("Quicksilver", "Callie Hart", default);

        Assert.Equal("gr:right", match?.ForeignBookId);
    }

    [Fact]
    public async Task Resolve_RetriesWithTheAuthorAddedWhenTheTitleAloneFindsNothing()
    {
        chaptarr.Lookups["Gideon the Ninth Tamsyn Muir"] = [Hit("gr:gideon", "Gideon the Ninth", "Tamsyn Muir")];

        var match = await Service().ResolveAsync("Gideon the Ninth", "Tamsyn Muir", default);

        Assert.Equal("gr:gideon", match?.ForeignBookId);
        Assert.Equal(["Gideon the Ninth", "Gideon the Ninth Tamsyn Muir"], chaptarr.LookupTerms);
    }

    [Fact]
    public async Task Resolve_ReturnsNullWhenChaptarrOnlyKnowsOtherAuthors()
    {
        chaptarr.Lookups["Inferno"] = [Hit("gr:x", "Inferno", "Dan Brown")];
        chaptarr.Lookups["Inferno Dante Alighieri"] = [];

        Assert.Null(await Service().ResolveAsync("Inferno", "Dante Alighieri", default));
    }

    [Fact]
    public async Task Resolve_MatchesAcrossSubtitleStyleDifferences()
    {
        chaptarr.Lookups["The Final Empire"] = [Hit("gr:mb", "Mistborn: The Final Empire", "Brandon Sanderson")];

        var match = await Service().ResolveAsync("The Final Empire", "Brandon Sanderson", default);

        Assert.Equal("gr:mb", match?.ForeignBookId);
    }

    public void Dispose()
    {
        cache.Dispose();
        t.Dispose();
    }
}
