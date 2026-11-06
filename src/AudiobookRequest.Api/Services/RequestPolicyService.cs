using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <param name="InitialStatus">Requested, or PendingApproval when an admin has to sign off first.</param>
/// <param name="Remaining">New requests this user may still create this week; int.MaxValue when unlimited.</param>
public record RequestPolicy(BookRequestStatus InitialStatus, int Remaining)
{
    /// <summary>
    /// The status a new request starts in: held for approval if that's required, otherwise Upcoming when the book isn't
    /// out yet (so nothing tries to download it), otherwise Requested.
    /// </summary>
    public BookRequestStatus StatusFor(DateTimeOffset? releaseDate) =>
        InitialStatus == BookRequestStatus.Requested && Core.Library.ReleaseSchedule.IsUnreleased(releaseDate, DateTimeOffset.UtcNow)
            ? BookRequestStatus.Upcoming
            : InitialStatus;

    public string LimitMessage => "You've reached this week's request limit. Ask an admin, or try again once an earlier request is more than 7 days old.";
}

/// <summary>Decides, for one user, whether new requests need approval and how many more they may make this week. Admins bypass both.</summary>
public class RequestPolicyService(AppDbContext db, SettingsService settingsService)
{
    public const int WindowDays = 7;

    public async Task<RequestPolicy> EvaluateAsync(int userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin)
        {
            return new RequestPolicy(BookRequestStatus.Requested, int.MaxValue);
        }

        var settings = await settingsService.GetAsync(ct);
        var status = settings.RequireApproval ? BookRequestStatus.PendingApproval : BookRequestStatus.Requested;

        if (settings.MaxRequestsPerUserPerWeek <= 0)
        {
            return new RequestPolicy(status, int.MaxValue);
        }

        // SQLite can't compare DateTimeOffset server-side, so count client-side. Denied requests don't use up the allowance.
        var since = DateTimeOffset.UtcNow.AddDays(-WindowDays);
        var times = await db.BookRequests
            .Where(r => r.RequestedByUserId == userId && r.Status != BookRequestStatus.Denied)
            .Select(r => r.RequestedAt)
            .ToListAsync(ct);
        var used = times.Count(t => t >= since);
        return new RequestPolicy(status, Math.Max(0, settings.MaxRequestsPerUserPerWeek - used));
    }
}
