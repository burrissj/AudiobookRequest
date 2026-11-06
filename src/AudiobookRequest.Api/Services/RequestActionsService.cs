using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public enum RequestActionOutcome { Ok, NotFound, InvalidState }

public record RequestActionResult(RequestActionOutcome Outcome, BookRequestDto? Request = null, string? Error = null)
{
    public static RequestActionResult NotFound() => new(RequestActionOutcome.NotFound);
    public static RequestActionResult Invalid(string error) => new(RequestActionOutcome.InvalidState, null, error);
    public static RequestActionResult Done(BookRequestDto dto) => new(RequestActionOutcome.Ok, dto);
}

/// <summary>State changes on an existing request beyond create/cancel: approve/deny, search again, keep-trying (wishlist).</summary>
public class RequestActionsService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    IExternalNotifier externalNotifier,
    INotificationSender notifications,
    UpcomingRequestService upcoming,
    ILogger<RequestActionsService> logger)
{
    public async Task<RequestActionResult> ApproveAsync(int id, CancellationToken ct)
    {
        var request = await Load(id, ct);
        if (request is null) return RequestActionResult.NotFound();
        if (request.Status != BookRequestStatus.PendingApproval) return RequestActionResult.Invalid("This request isn't waiting for approval.");

        // A book that isn't out yet waits in the upcoming queue instead of starting a search that can only fail.
        request.Status = ReleaseSchedule.IsUnreleased(request.ReleaseDate, DateTimeOffset.UtcNow)
            ? BookRequestStatus.Upcoming
            : BookRequestStatus.Requested;
        request.RequestedAt = DateTimeOffset.UtcNow;
        return await SaveAndPush(request, ct);
    }

    public async Task<RequestActionResult> DenyAsync(int id, string? note, CancellationToken ct)
    {
        var request = await Load(id, ct);
        if (request is null) return RequestActionResult.NotFound();
        if (request.Status != BookRequestStatus.PendingApproval) return RequestActionResult.Invalid("This request isn't waiting for approval.");

        request.Status = BookRequestStatus.Denied;
        request.FailureReason = string.IsNullOrWhiteSpace(note) ? "Declined by an admin." : $"Declined: {note.Trim()}";
        var result = await SaveAndPush(request, ct);

        var settings = await settingsService.GetAsync(ct);
        await externalNotifier.NotifyAsync(settings, ExternalNotifier.Denied(request, note), ct);
        return result;
    }

    /// <summary>
    /// Runs the search again for a request that is failed or hasn't found anything yet, and restarts its
    /// 24h patience clock. If Chaptarr never got the book, it goes back to Requested so the normal flow adds it.
    /// </summary>
    public async Task<RequestActionResult> SearchAgainAsync(int id, CancellationToken ct)
    {
        var request = await Load(id, ct);
        if (request is null) return RequestActionResult.NotFound();
        if (request.Status is not (BookRequestStatus.Failed or BookRequestStatus.Searching or BookRequestStatus.Requested or BookRequestStatus.Upcoming))
        {
            return RequestActionResult.Invalid("Only a failed or still-searching request can be searched again.");
        }

        var settings = await settingsService.GetAsync(ct);
        if (request.Status == BookRequestStatus.Upcoming)
        {
            return await upcoming.CheckNowAsync(request, settings, ct);
        }

        request.FailureReason = null;
        request.DiagnosticMessage = null;
        request.AutoFixAttemptedAt = null;
        request.RequestedAt = DateTimeOffset.UtcNow;

        if (request.ChaptarrBookId is null)
        {
            request.Status = BookRequestStatus.Requested;
        }
        else
        {
            try
            {
                await chaptarr.TriggerBookSearchAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, request.ChaptarrBookId.Value, ct);
                request.Status = BookRequestStatus.Searching;
                request.LastSearchAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Search-again failed for request {RequestId}", id);
                return RequestActionResult.Invalid("Couldn't reach Chaptarr to start the search. Try again in a moment.");
            }
        }

        return await SaveAndPush(request, ct);
    }

    /// <summary>
    /// Wishlisting keeps a request searching on a schedule instead of failing after 24h with nothing found.
    /// Turning it on for a failed request revives it.
    /// </summary>
    public async Task<RequestActionResult> SetWishlistedAsync(int id, bool enabled, CancellationToken ct)
    {
        var request = await Load(id, ct);
        if (request is null) return RequestActionResult.NotFound();
        if (request.Status == BookRequestStatus.Upcoming)
        {
            return RequestActionResult.Invalid("This book isn't out yet; it is searched for automatically and sped up once it is released.");
        }
        if (request.Status is BookRequestStatus.Ready or BookRequestStatus.Denied or BookRequestStatus.PendingApproval)
        {
            return RequestActionResult.Invalid("That request can't be wishlisted right now.");
        }

        request.Wishlisted = enabled;
        if (enabled && request.Status == BookRequestStatus.Failed)
        {
            return await SearchAgainWithoutLoadAsync(request, ct);
        }
        return await SaveAndPush(request, ct);
    }

    private async Task<RequestActionResult> SearchAgainWithoutLoadAsync(BookRequest request, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        return await SearchAgainAsync(request.Id, ct);
    }

    private Task<BookRequest?> Load(int id, CancellationToken ct) =>
        db.BookRequests.Include(r => r.RequestedByUser).FirstOrDefaultAsync(r => r.Id == id, ct);

    private async Task<RequestActionResult> SaveAndPush(BookRequest request, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var dto = BookRequestDto.From(request);
        await notifications.RequestUpdatedAsync(dto, ct);
        return RequestActionResult.Done(dto);
    }
}
