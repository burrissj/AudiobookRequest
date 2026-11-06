using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Tests;

public sealed class FollowTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeMetadataClient metadata = new();
    private readonly FakeChaptarrClient chaptarr = new();
    private readonly FakeExternalNotifier external = new();
    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    private FollowService Service()
    {
        var library = new LibraryCatalogService(t.Db, t.Settings, new FakeJellyfinClient());
        var browse = new CatalogBrowseService(t.Db, t.Settings, metadata, chaptarr, library, cache);
        var creation = new RequestCreationService(t.Db, t.Settings, library, new RequestPolicyService(t.Db, t.Settings), external, new FakeNotificationSender());
        return new FollowService(t.Db, t.Settings, metadata, browse, creation, external, Nulls.Logger<FollowService>());
    }

    private static AudiobookListing Book(string asin, string title, string author = "Brandon Sanderson", string? series = null, string? seriesAsin = null, string? sequence = null) =>
        new(asin, title, author, $"https://img/{asin}.jpg", 600, 4.5, 100, series, sequence, [], [author], "2026-10-06", seriesAsin);

    private static AudiobookPage Page(params AudiobookListing[] books) => new(books, books.Length);

    private static CreateFollowRequest AuthorFollow(bool auto = false) => new("Author", "Brandon Sanderson", null, null, auto);

    private static ChaptarrBookLookupResult Hit(string id, string title, string author) =>
        new() { ForeignBookId = id, Title = title, AuthorName = author };

    [Fact]
    public async Task Create_TakesABaseline_SoTheBackCatalogNeverCountsAsNew()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"), Book("A2", "Old Two"));
        var service = Service();

        await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);
        await service.CheckAllAsync(default);

        Assert.Empty(t.Db.FollowFinds);
        Assert.NotNull((await t.Db.Follows.SingleAsync()).LastCheckedAt);
    }

    [Fact]
    public async Task Check_FindsOnlyBooksThatAppearedAfterTheBaseline_AndAnnouncesThem()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);

        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A9", "Brand New"), Book("A1", "Old One"));
        await service.CheckAllAsync(default);

        var find = await t.Db.FollowFinds.SingleAsync();
        Assert.Equal("Brand New", find.Title);
        var sent = Assert.Single(external.Sent);
        Assert.Equal(NotificationKind.NewRelease, sent.Kind);
        Assert.Contains("Brand New", sent.Message);
        Assert.Contains("victoria", sent.Message);

        // Checking again must not announce the same book twice.
        await service.CheckAllAsync(default);
        Assert.Single(t.Db.FollowFinds);
        Assert.Single(external.Sent);
    }

    [Fact]
    public async Task Check_IgnoresBooksByOtherAuthorsThatAudibleReturnedForTheFuzzyNameMatch()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);

        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("X1", "Someone Elses Book", "Brandon Taylor"), Book("A1", "Old One"));
        await service.CheckAllAsync(default);

        Assert.Empty(t.Db.FollowFinds);
    }

    [Fact]
    public async Task WhenAudibleIsDownAtFollowTime_ThePendingBaselineIsTakenLaterWithoutAFlood()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(); // Audible returned nothing
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);
        Assert.Null((await t.Db.Follows.SingleAsync()).LastCheckedAt);

        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"), Book("A2", "Old Two"));
        await service.CheckAllAsync(default);

        Assert.Empty(t.Db.FollowFinds);
        Assert.NotNull((await t.Db.Follows.SingleAsync()).LastCheckedAt);
    }

    [Fact]
    public async Task AnEmptyResultDuringACheck_ChangesNothing()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);

        metadata.AuthorPages["Brandon Sanderson"] = Page();
        await service.CheckAllAsync(default);
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        await service.CheckAllAsync(default);

        Assert.Empty(t.Db.FollowFinds);
    }

    [Fact]
    public async Task Create_IsIdempotent()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();

        var first = await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);
        var second = await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Single(t.Db.Follows);
    }

    [Theory]
    [InlineData("Nonsense", "X", null, null)]
    [InlineData("Author", "  ", null, null)]
    [InlineData("Series", "Stormlight", null, null)]
    [InlineData("Series", "Stormlight", "S1", null)]
    public async Task Create_RejectsIncompleteRequests(string kind, string name, string? seriesAsin, string? seriesAuthor)
    {
        var created = await Service().CreateAsync(t.Victoria.Id, new CreateFollowRequest(kind, name, seriesAsin, seriesAuthor, false), default);

        Assert.Null(created);
        Assert.Empty(t.Db.Follows);
    }

    [Fact]
    public async Task Finds_HideBooksAlreadyOwnedOrRequested_AndDismissedOnes()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);
        metadata.AuthorPages["Brandon Sanderson"] = Page(
            Book("N1", "Fresh One"), Book("N2", "Owned Two"), Book("N3", "Requested Three"), Book("N4", "Dismissed Four"), Book("A1", "Old One"));
        await service.CheckAllAsync(default);

        t.Db.LibraryBooks.Add(new LibraryBookCache { JellyfinItemId = "j", Title = "Owned Two", Author = "Brandon Sanderson", Artists = "Brandon Sanderson" });
        t.AddRequest(t.Admin, BookRequestStatus.Searching, "Requested Three", r => r.Author = "Brandon Sanderson");
        var dismissed = await t.Db.FollowFinds.SingleAsync(f => f.Title == "Dismissed Four");
        await service.DismissFindAsync(t.Victoria.Id, dismissed.Id, default);
        t.Db.SaveChanges();

        var finds = await service.FindsAsync(t.Victoria.Id, default);

        Assert.Equal(["Fresh One"], finds.Select(f => f.Item.Title));
        Assert.Equal("Brandon Sanderson", finds[0].FollowName);
    }

    [Fact]
    public async Task OneUsersFollowsAndFinds_AreInvisibleAndUntouchableToAnother()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        var follow = await service.CreateAsync(t.Victoria.Id, AuthorFollow(), default);

        Assert.Empty(await service.ListAsync(t.Admin.Id, default));
        Assert.False(await service.DeleteAsync(t.Admin.Id, follow!.Id, default));
        Assert.Null(await service.SetAutoRequestAsync(t.Admin.Id, follow.Id, true, default));
        Assert.True(await service.DeleteAsync(t.Victoria.Id, follow.Id, default));
    }

    [Fact]
    public async Task AutoRequest_RequestsEachNewBookForAnAuthorAndNeverTwice()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(auto: true), default);
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("N1", "Fresh One"), Book("A1", "Old One"));
        chaptarr.Lookups["Fresh One"] = [Hit("gr:fresh", "Fresh One", "Brandon Sanderson")];

        await service.CheckAllAsync(default);
        await service.CheckAllAsync(default);

        var request = await t.Db.BookRequests.SingleAsync();
        Assert.Equal("gr:fresh", request.ChaptarrForeignBookId);
        Assert.Equal(t.Victoria.Id, request.RequestedByUserId);
        Assert.Equal(BookRequestStatus.Requested, request.Status);
        Assert.Contains("Requested automatically", external.Sent.Single().Message);
    }

    [Fact]
    public async Task AutoRequest_KeepsTryingWhileChaptarrDoesNotKnowTheBookYet()
    {
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(auto: true), default);
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("N1", "Announced Book"), Book("A1", "Old One"));

        await service.CheckAllAsync(default);
        Assert.Empty(t.Db.BookRequests);
        Assert.Null((await t.Db.FollowFinds.SingleAsync()).AutoRequestedAt);

        chaptarr.Lookups["Announced Book"] = [Hit("gr:announced", "Announced Book", "Brandon Sanderson")];
        await service.CheckAllAsync(default);

        Assert.Equal("gr:announced", (await t.Db.BookRequests.SingleAsync()).ChaptarrForeignBookId);
    }

    [Fact]
    public async Task AutoRequest_RespectsTheWeeklyLimit_AndRetriesLater()
    {
        t.CurrentSettings.MaxRequestsPerUserPerWeek = 1;
        t.Db.SaveChanges();
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(auto: true), default);
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("N1", "First New"), Book("N2", "Second New"), Book("A1", "Old One"));
        chaptarr.Lookups["First New"] = [Hit("gr:1", "First New", "Brandon Sanderson")];
        chaptarr.Lookups["Second New"] = [Hit("gr:2", "Second New", "Brandon Sanderson")];

        await service.CheckAllAsync(default);

        Assert.Single(t.Db.BookRequests);
        Assert.Single(t.Db.FollowFinds, f => f.AutoRequestedAt == null);
    }

    [Fact]
    public async Task AutoRequest_ForAnApprovalRequiredUser_LandsInTheApprovalQueue()
    {
        t.CurrentSettings.RequireApproval = true;
        t.Db.SaveChanges();
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("A1", "Old One"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, AuthorFollow(auto: true), default);
        metadata.AuthorPages["Brandon Sanderson"] = Page(Book("N1", "Fresh One"), Book("A1", "Old One"));
        chaptarr.Lookups["Fresh One"] = [Hit("gr:fresh", "Fresh One", "Brandon Sanderson")];

        await service.CheckAllAsync(default);

        Assert.Equal(BookRequestStatus.PendingApproval, (await t.Db.BookRequests.SingleAsync()).Status);
    }

    // ---- series ----

    private static CreateFollowRequest SeriesFollow(bool auto) =>
        new("Series", "The Stormlight Archive", "SER1", "Brandon Sanderson", auto);

    private AudiobookPage SeriesPage(params (int Number, string Title)[] books) =>
        Page(books.Select(b => Book($"S{b.Number}", b.Title, series: "The Stormlight Archive", seriesAsin: "SER1", sequence: b.Number.ToString())).ToArray());

    [Fact]
    public async Task Series_OnlyTrackedMembersCount_NotOtherSeriesOrBundles()
    {
        metadata.AuthorPages["Brandon Sanderson"] = SeriesPage((1, "The Way of Kings"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, SeriesFollow(false), default);

        metadata.AuthorPages["Brandon Sanderson"] = Page(
            Book("S1", "The Way of Kings", series: "The Stormlight Archive", seriesAsin: "SER1", sequence: "1"),
            Book("S2", "Words of Radiance", series: "The Stormlight Archive", seriesAsin: "SER1", sequence: "2"),
            Book("OMNI", "Stormlight Bundle", series: "The Stormlight Archive", seriesAsin: "SER1", sequence: "1-2"),
            Book("OTHER", "Mistborn Thing", series: "Mistborn", seriesAsin: "SER9", sequence: "5"));
        await service.CheckAllAsync(default);

        Assert.Equal(["Words of Radiance"], t.Db.FollowFinds.Select(f => f.Title));
    }

    [Fact]
    public async Task Series_AutoRequestsTheBookAfterTheLastOneFinished()
    {
        metadata.AuthorPages["Brandon Sanderson"] = SeriesPage((1, "The Way of Kings"), (2, "Words of Radiance"), (3, "Oathbringer"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, SeriesFollow(true), default);
        t.Db.ListeningHistory.Add(new ListeningHistoryCache { AppUserId = t.Victoria.Id, JellyfinItemId = "p1", Title = "The Way of Kings", PlayCount = 1 });
        t.Db.ListeningHistory.Add(new ListeningHistoryCache { AppUserId = t.Victoria.Id, JellyfinItemId = "p2", Title = "Words of Radiance", PlayCount = 2 });
        t.Db.SaveChanges();
        chaptarr.Lookups["Oathbringer"] = [Hit("gr:oath", "Oathbringer", "Brandon Sanderson")];

        await service.CheckAllAsync(default);

        var request = await t.Db.BookRequests.SingleAsync();
        Assert.Equal("gr:oath", request.ChaptarrForeignBookId);
        Assert.Equal(["Oathbringer"], t.Db.FollowFinds.Select(f => f.Title));

        await service.CheckAllAsync(default); // already requested: nothing more
        Assert.Single(t.Db.BookRequests);
    }

    [Fact]
    public async Task Series_DoesNothingUntilTheUserHasFinishedAMember()
    {
        metadata.AuthorPages["Brandon Sanderson"] = SeriesPage((1, "The Way of Kings"), (2, "Words of Radiance"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, SeriesFollow(true), default);
        t.Db.ListeningHistory.Add(new ListeningHistoryCache { AppUserId = t.Victoria.Id, JellyfinItemId = "p", Title = "Some Other Book", PlayCount = 1 });
        t.Db.SaveChanges();
        chaptarr.Lookups["The Way of Kings"] = [Hit("gr:k", "The Way of Kings", "Brandon Sanderson")];

        await service.CheckAllAsync(default);

        Assert.Empty(t.Db.BookRequests);
    }

    [Fact]
    public async Task Series_SkipsTheNextBookWhenItIsAlreadyOwned()
    {
        metadata.AuthorPages["Brandon Sanderson"] = SeriesPage((1, "The Way of Kings"), (2, "Words of Radiance"));
        var service = Service();
        await service.CreateAsync(t.Victoria.Id, SeriesFollow(true), default);
        t.Db.ListeningHistory.Add(new ListeningHistoryCache { AppUserId = t.Victoria.Id, JellyfinItemId = "p1", Title = "The Way of Kings", PlayCount = 1 });
        t.Db.LibraryBooks.Add(new LibraryBookCache { JellyfinItemId = "j2", Title = "Words of Radiance", Author = "Brandon Sanderson", Artists = "Brandon Sanderson" });
        t.Db.SaveChanges();
        chaptarr.Lookups["Words of Radiance"] = [Hit("gr:wor", "Words of Radiance", "Brandon Sanderson")];

        await service.CheckAllAsync(default);

        Assert.Empty(t.Db.BookRequests);
    }

    public void Dispose()
    {
        cache.Dispose();
        t.Dispose();
    }
}
