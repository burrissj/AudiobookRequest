using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public class RequestPolicyServiceTests
{
    [Fact]
    public async Task Admin_BypassesApprovalAndLimits()
    {
        using var t = new TestDb();
        t.CurrentSettings.RequireApproval = true;
        t.CurrentSettings.MaxRequestsPerUserPerWeek = 1;
        t.Db.SaveChanges();
        t.AddRequest(t.Admin, BookRequestStatus.Requested);
        var service = new RequestPolicyService(t.Db, t.Settings);

        var policy = await service.EvaluateAsync(t.Admin.Id, isAdmin: true, default);

        Assert.Equal(BookRequestStatus.Requested, policy.InitialStatus);
        Assert.Equal(int.MaxValue, policy.Remaining);
    }

    [Fact]
    public async Task RegularUser_GoesToApprovalQueueWhenRequired()
    {
        using var t = new TestDb();
        t.CurrentSettings.RequireApproval = true;
        t.Db.SaveChanges();

        var policy = await new RequestPolicyService(t.Db, t.Settings).EvaluateAsync(t.Victoria.Id, false, default);

        Assert.Equal(BookRequestStatus.PendingApproval, policy.InitialStatus);
        Assert.Equal(int.MaxValue, policy.Remaining);
    }

    [Fact]
    public async Task WeeklyLimit_CountsRecentRequestsButNotOldOrDeniedOrOtherUsers()
    {
        using var t = new TestDb();
        t.CurrentSettings.MaxRequestsPerUserPerWeek = 3;
        t.Db.SaveChanges();
        t.AddRequest(t.Victoria, BookRequestStatus.Ready);
        t.AddRequest(t.Victoria, BookRequestStatus.Failed);
        t.AddRequest(t.Victoria, BookRequestStatus.Denied);
        t.AddRequest(t.Victoria, BookRequestStatus.Ready, configure: r => r.RequestedAt = DateTimeOffset.UtcNow.AddDays(-8));
        t.AddRequest(t.Admin, BookRequestStatus.Requested);

        var policy = await new RequestPolicyService(t.Db, t.Settings).EvaluateAsync(t.Victoria.Id, false, default);

        Assert.Equal(1, policy.Remaining);
    }

    [Fact]
    public async Task WeeklyLimit_ReachedMeansNothingRemaining()
    {
        using var t = new TestDb();
        t.CurrentSettings.MaxRequestsPerUserPerWeek = 2;
        t.Db.SaveChanges();
        t.AddRequest(t.Victoria, BookRequestStatus.Requested);
        t.AddRequest(t.Victoria, BookRequestStatus.PendingApproval);

        var policy = await new RequestPolicyService(t.Db, t.Settings).EvaluateAsync(t.Victoria.Id, false, default);

        Assert.Equal(0, policy.Remaining);
    }
}
