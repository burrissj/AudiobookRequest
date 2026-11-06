using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public sealed class UpcomingRequestTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeChaptarrClient chaptarr = new();
    private readonly FakeNotificationSender push = new();
    private readonly FakeExternalNotifier external = new();

    private static DateTimeOffset InDays(double days) => DateTimeOffset.UtcNow.AddDays(days);

    private UpcomingRequestService Service() => new(t.Db, t.Settings, chaptarr, push, Nulls.Logger<UpcomingRequestService>());

    private RequestCreationService Creation()
    {
        var library = new LibraryCatalogService(t.Db, t.Settings, new FakeJellyfinClient());
        return new RequestCreationService(t.Db, t.Settings, library, new RequestPolicyService(t.Db, t.Settings), external, push);
    }

    private ChaptarrOrchestrationService Orchestration() =>
        new(t.Db, t.Settings, chaptarr, push, external, new ImportFixService(t.Db, t.Settings, chaptarr, Nulls.Logger<ImportFixService>()),
            Nulls.Logger<ChaptarrOrchestrationService>());

    private BookRequest UpcomingRequest(Action<BookRequest>? configure = null, double releaseInDays = 10) =>
        t.AddRequest(t.Victoria, BookRequestStatus.Upcoming, configure: r =>
        {
            r.ReleaseDate = InDays(releaseInDays);
            configure?.Invoke(r);
        });

    // ---- creating a request -------------------------------------------------------------------------------

    [Fact]
    public async Task Create_ForABookNotOutYet_WaitsInTheUpcomingQueueWithItsDate()
    {
        var release = InDays(20);

        var result = await Creation().CreateAsync(t.Victoria.Id, false, "fb:1", "Soon", "An Author", null, null, release, default);

        Assert.Equal(CreateOutcome.Created, result.Outcome);
        Assert.Equal("Upcoming", result.Request!.Status);
        Assert.Equal(release, result.Request.ReleaseDate);
        Assert.NotNull(result.Request.NextSearchAt);
        Assert.Equal(BookRequestStatus.Upcoming, t.Db.BookRequests.Single().Status);
    }

    [Fact]
    public async Task Create_ForABookAlreadyOut_StartsNormallyAndKeepsTheDate()
    {
        var release = InDays(-5);

        var result = await Creation().CreateAsync(t.Victoria.Id, false, "fb:1", "Out", "An Author", null, null, release, default);

        Assert.Equal("Requested", result.Request!.Status);
        Assert.Equal(release, result.Request.ReleaseDate);
        Assert.Null(result.Request.NextSearchAt);
    }

    [Fact]
    public async Task Create_WithoutADate_IsUnchanged()
    {
        var result = await Creation().CreateAsync(t.Victoria.Id, false, "fb:1", "Undated", "An Author", null, null, null, default);

        Assert.Equal("Requested", result.Request!.Status);
        Assert.Null(result.Request.ReleaseDate);
    }

    [Fact]
    public async Task Create_WhenApprovalIsRequired_StillWaitsForAnAdminFirst()
    {
        t.CurrentSettings.RequireApproval = true;
        t.Db.SaveChanges();

        var result = await Creation().CreateAsync(t.Victoria.Id, false, "fb:1", "Soon", "An Author", null, null, InDays(20), default);

        Assert.Equal("PendingApproval", result.Request!.Status);
    }

    [Fact]
    public async Task Approve_SendsAnUnreleasedBookToUpcoming_AndAReleasedOneToRequested()
    {
        var future = t.AddRequest(t.Victoria, BookRequestStatus.PendingApproval, configure: r => r.ReleaseDate = InDays(9));
        var past = t.AddRequest(t.Victoria, BookRequestStatus.PendingApproval, configure: r => r.ReleaseDate = InDays(-9));
        var actions = new RequestActionsService(t.Db, t.Settings, chaptarr, external, push, Service(), Nulls.Logger<RequestActionsService>());

        await actions.ApproveAsync(future.Id, default);
        await actions.ApproveAsync(past.Id, default);

        Assert.Equal(BookRequestStatus.Upcoming, future.Status);
        Assert.Equal(BookRequestStatus.Requested, past.Status);
    }

    // ---- the upcoming queue -------------------------------------------------------------------------------

    [Fact]
    public async Task FirstTick_AddsTheBookToChaptarrAndSearchesOnce()
    {
        var request = UpcomingRequest();

        await Service().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Upcoming, request.Status);
        Assert.Equal(100, request.ChaptarrBookId);
        Assert.Equal([100], chaptarr.SearchedBookIds);
        Assert.NotNull(request.LastSearchAt);
        Assert.Null(request.DiagnosticMessage);
    }

    [Fact]
    public async Task ABookIsNotSearchedAgainUntilItsNextCheckIsDue()
    {
        var request = UpcomingRequest(r =>
        {
            r.ChaptarrBookId = 5;
            r.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-3); // 10 days out means one check a day
        });

        await Service().RunOnceAsync(default);
        Assert.Empty(chaptarr.SearchedBookIds);

        request.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-25);
        t.Db.SaveChanges();
        await Service().RunOnceAsync(default);

        Assert.Equal([5], chaptarr.SearchedBookIds);
    }

    [Fact]
    public async Task CheckGetsMoreFrequentAsTheReleaseApproaches()
    {
        // Last checked 8 hours ago: too soon with 10 days to go (daily), due with 2 days to go (every 6 hours).
        var far = UpcomingRequest(r => { r.ChaptarrBookId = 1; r.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-8); }, releaseInDays: 10);
        var near = UpcomingRequest(r => { r.ChaptarrBookId = 2; r.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-8); }, releaseInDays: 2);

        await Service().RunOnceAsync(default);

        Assert.Equal([2], chaptarr.SearchedBookIds);
        Assert.Equal(BookRequestStatus.Upcoming, far.Status);
        Assert.Equal(BookRequestStatus.Upcoming, near.Status);
    }

    [Fact]
    public async Task WhenChaptarrIsDown_TheReasonIsShownAndTheCheckIsNotRepeatedEveryTick()
    {
        chaptarr.AddBookError = new HttpRequestException("Chaptarr is unreachable");
        var request = UpcomingRequest();

        await Service().RunOnceAsync(default);
        await Service().RunOnceAsync(default);

        Assert.Equal(1, chaptarr.AddBookCalls);
        Assert.Contains("Couldn't check Chaptarr", request.DiagnosticMessage);
        Assert.Equal(BookRequestStatus.Upcoming, request.Status);
        Assert.Null(request.ChaptarrBookId);
    }

    [Fact]
    public async Task OnReleaseDay_ABookChaptarrNeverSawGoesToRequestedAsAWishlistItem()
    {
        var request = UpcomingRequest(releaseInDays: -0.01);

        await Service().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Requested, request.Status);
        Assert.True(request.Wishlisted);
        Assert.Empty(chaptarr.SearchedBookIds); // the normal loop adds it to Chaptarr and searches
    }

    [Fact]
    public async Task OnReleaseDay_ABookAlreadyInChaptarrIsSearchedImmediately()
    {
        var request = UpcomingRequest(r => r.ChaptarrBookId = 42, releaseInDays: -0.01);

        await Service().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Searching, request.Status);
        Assert.True(request.Wishlisted);
        Assert.Equal([42], chaptarr.SearchedBookIds);
        Assert.Contains(push.Updates, u => u.Id == request.Id && u.Status == "Searching");
    }

    [Fact]
    public async Task ABookThatAppearsEarly_IsPassedToTheNormalFlowWithoutWaitingForTheDate()
    {
        var request = UpcomingRequest(r => r.ChaptarrBookId = 8);
        chaptarr.History.Add(new ChaptarrHistoryItem(8, "grabbed", DateTimeOffset.UtcNow, null));

        await Service().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Searching, request.Status);
        Assert.True(request.Wishlisted);
    }

    [Fact]
    public async Task ABookInChaptarrsQueueEarly_IsPassedOnToo()
    {
        var request = UpcomingRequest(r => r.ChaptarrBookId = 9);
        chaptarr.Queue.Add(new ChaptarrQueueItem("Book", 9, "downloading", 100, 90, "Deluge"));

        await Service().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Searching, request.Status);
    }

    [Fact]
    public async Task AnEarlierGrabFromBeforeTheRequestIsIgnored()
    {
        var request = UpcomingRequest(r =>
        {
            r.ChaptarrBookId = 8;
            r.RequestedAt = DateTimeOffset.UtcNow.AddHours(-1);
            r.LastSearchAt = DateTimeOffset.UtcNow;
        });
        chaptarr.History.Add(new ChaptarrHistoryItem(8, "grabbed", DateTimeOffset.UtcNow.AddDays(-30), null));

        await Service().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Upcoming, request.Status);
    }

    [Fact]
    public async Task CheckNow_SearchesRightAwayEvenThoughNothingIsDue()
    {
        var request = UpcomingRequest(r => { r.ChaptarrBookId = 6; r.LastSearchAt = DateTimeOffset.UtcNow.AddMinutes(-5); });
        var actions = new RequestActionsService(t.Db, t.Settings, chaptarr, external, push, Service(), Nulls.Logger<RequestActionsService>());

        var result = await actions.SearchAgainAsync(request.Id, default);

        Assert.Equal(RequestActionOutcome.Ok, result.Outcome);
        Assert.Equal([6], chaptarr.SearchedBookIds);
        Assert.Equal(BookRequestStatus.Upcoming, request.Status);
        Assert.True(DateTimeOffset.UtcNow - request.LastSearchAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Wishlist_CantBeToggledOnAnUpcomingBook()
    {
        var request = UpcomingRequest();
        var actions = new RequestActionsService(t.Db, t.Settings, chaptarr, external, push, Service(), Nulls.Logger<RequestActionsService>());

        var result = await actions.SetWishlistedAsync(request.Id, true, default);

        Assert.Equal(RequestActionOutcome.InvalidState, result.Outcome);
        Assert.False(request.Wishlisted);
    }

    // ---- searching faster once it is out ------------------------------------------------------------------

    [Fact]
    public async Task AWishlistedBookThatJustCameOut_IsResearchedEvenThoughTheNormalPaceHasNotElapsed()
    {
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Searching, configure: r =>
        {
            r.ChaptarrBookId = 21;
            r.Wishlisted = true;
            r.ReleaseDate = DateTimeOffset.UtcNow.AddHours(-3);
            r.LastSearchAt = DateTimeOffset.UtcNow.AddMinutes(-45); // normal pace is 12h; release-day pace is 30 min
        });

        await Orchestration().RunOnceAsync(default);

        Assert.Equal([21], chaptarr.SearchedBookIds);
        Assert.True(DateTimeOffset.UtcNow - request.LastSearchAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AnOlderWishlistedBook_StillWaitsForTheNormalPace()
    {
        t.AddRequest(t.Victoria, BookRequestStatus.Searching, configure: r =>
        {
            r.ChaptarrBookId = 22;
            r.Wishlisted = true;
            r.ReleaseDate = DateTimeOffset.UtcNow.AddDays(-90);
            r.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-3);
        });

        await Orchestration().RunOnceAsync(default);

        Assert.Empty(chaptarr.SearchedBookIds);
    }

    [Fact]
    public async Task TheNormalLoopLeavesUpcomingBooksAlone()
    {
        var request = UpcomingRequest();

        await Orchestration().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Upcoming, request.Status);
        Assert.Empty(chaptarr.SearchedBookIds);
        Assert.Null(request.ChaptarrBookId);
    }

    // ---- what the app shows -------------------------------------------------------------------------------

    [Fact]
    public void TheDtoCarriesTheDateAndANextCheckOnlyWhileUpcoming()
    {
        var upcoming = UpcomingRequest(r => r.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-2));
        var requested = t.AddRequest(t.Victoria, BookRequestStatus.Requested, configure: r => r.ReleaseDate = InDays(-1));

        var dto = BookRequestDto.From(upcoming);
        Assert.Equal(upcoming.ReleaseDate, dto.ReleaseDate);
        Assert.InRange(dto.NextSearchAt!.Value, DateTimeOffset.UtcNow.AddHours(21), DateTimeOffset.UtcNow.AddHours(23)); // daily at 10 days out

        Assert.Null(BookRequestDto.From(requested).NextSearchAt);
    }

    public void Dispose() => t.Dispose();
}
