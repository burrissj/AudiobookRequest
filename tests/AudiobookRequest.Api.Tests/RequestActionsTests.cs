using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public class RequestActionsTests
{
    private static (RequestActionsService Service, FakeChaptarrClient Chaptarr, FakeExternalNotifier External) Build(TestDb t)
    {
        var chaptarr = new FakeChaptarrClient();
        var external = new FakeExternalNotifier();
        var push = new FakeNotificationSender();
        var upcoming = new UpcomingRequestService(t.Db, t.Settings, chaptarr, push, Nulls.Logger<UpcomingRequestService>());
        var service = new RequestActionsService(t.Db, t.Settings, chaptarr, external, push, upcoming, Nulls.Logger<RequestActionsService>());
        return (service, chaptarr, external);
    }

    [Fact]
    public async Task Approve_MovesPendingRequestIntoTheNormalFlow()
    {
        using var t = new TestDb();
        var (service, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.PendingApproval);

        var result = await service.ApproveAsync(request.Id, default);

        Assert.Equal(RequestActionOutcome.Ok, result.Outcome);
        Assert.Equal(BookRequestStatus.Requested, request.Status);
    }

    [Fact]
    public async Task Approve_RejectsARequestThatIsNotPending()
    {
        using var t = new TestDb();
        var (service, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Searching);

        var result = await service.ApproveAsync(request.Id, default);

        Assert.Equal(RequestActionOutcome.InvalidState, result.Outcome);
        Assert.Equal(BookRequestStatus.Searching, request.Status);
    }

    [Fact]
    public async Task Deny_RecordsTheReasonAndNotifies()
    {
        using var t = new TestDb();
        var (service, _, external) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.PendingApproval);

        await service.DenyAsync(request.Id, "Too many already", default);

        Assert.Equal(BookRequestStatus.Denied, request.Status);
        Assert.Equal("Declined: Too many already", request.FailureReason);
        Assert.Equal(NotificationKind.RequestDenied, Assert.Single(external.Sent).Kind);
    }

    [Fact]
    public async Task SearchAgain_ReTriggersTheSearchAndRestartsThe24HourClock()
    {
        using var t = new TestDb();
        var (service, chaptarr, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Failed, configure: r =>
        {
            r.ChaptarrBookId = 11;
            r.FailureReason = "nothing";
            r.RequestedAt = DateTimeOffset.UtcNow.AddDays(-2);
        });

        var result = await service.SearchAgainAsync(request.Id, default);

        Assert.Equal(RequestActionOutcome.Ok, result.Outcome);
        Assert.Equal(BookRequestStatus.Searching, request.Status);
        Assert.Null(request.FailureReason);
        Assert.Equal([11], chaptarr.SearchedBookIds);
        Assert.True(DateTimeOffset.UtcNow - request.RequestedAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SearchAgain_WithoutAChaptarrBookGoesBackToRequested()
    {
        using var t = new TestDb();
        var (service, chaptarr, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Failed);

        await service.SearchAgainAsync(request.Id, default);

        Assert.Equal(BookRequestStatus.Requested, request.Status);
        Assert.Empty(chaptarr.SearchedBookIds);
    }

    [Fact]
    public async Task SearchAgain_IsRefusedWhileDownloading()
    {
        using var t = new TestDb();
        var (service, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Downloading);

        var result = await service.SearchAgainAsync(request.Id, default);

        Assert.Equal(RequestActionOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Wishlist_OnAFailedRequestRevivesIt()
    {
        using var t = new TestDb();
        var (service, chaptarr, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Failed, configure: r => r.ChaptarrBookId = 3);

        await service.SetWishlistedAsync(request.Id, true, default);

        Assert.True(request.Wishlisted);
        Assert.Equal(BookRequestStatus.Searching, request.Status);
        Assert.Equal([3], chaptarr.SearchedBookIds);
    }

    [Fact]
    public async Task Wishlist_CanBeTurnedOff()
    {
        using var t = new TestDb();
        var (service, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Searching, configure: r => r.Wishlisted = true);

        await service.SetWishlistedAsync(request.Id, false, default);

        Assert.False(request.Wishlisted);
    }

    [Fact]
    public async Task UnknownRequest_IsNotFound()
    {
        using var t = new TestDb();
        var (service, _, _) = Build(t);

        Assert.Equal(RequestActionOutcome.NotFound, (await service.ApproveAsync(999, default)).Outcome);
        Assert.Equal(RequestActionOutcome.NotFound, (await service.SearchAgainAsync(999, default)).Outcome);
    }
}
