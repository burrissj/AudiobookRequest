using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public enum CreateOutcome { Created, AlreadyRequested, AlreadyInLibrary, LimitReached }

public record CreateResult(CreateOutcome Outcome, BookRequestDto? Request = null, string? Message = null);

/// <summary>
/// The one place a book request is created -- from the Request button, and from follows that request
/// automatically -- so the same rules always apply: not already requested, not already in the library,
/// within the user's weekly limit, and held for approval when that's switched on.
/// </summary>
public class RequestCreationService(
    AppDbContext db,
    SettingsService settingsService,
    LibraryCatalogService libraryCatalog,
    RequestPolicyService policyService,
    IExternalNotifier externalNotifier,
    INotificationSender notifications)
{
    public async Task<CreateResult> CreateAsync(
        int userId, bool isAdmin, string foreignBookId, string title, string author, string? series, string? coverUrl, DateTimeOffset? releaseDate, CancellationToken ct)
    {
        if (await db.BookRequests.AnyAsync(r => r.ChaptarrForeignBookId == foreignBookId, ct))
        {
            return new CreateResult(CreateOutcome.AlreadyRequested, Message: "This book has already been requested.");
        }

        var library = await libraryCatalog.GetIndexAsync(ct);
        if (library.IsOwned(title, author))
        {
            return new CreateResult(CreateOutcome.AlreadyInLibrary, Message: "This book is already in your Jellyfin library.");
        }

        var policy = await policyService.EvaluateAsync(userId, isAdmin, ct);
        if (policy.Remaining <= 0)
        {
            return new CreateResult(CreateOutcome.LimitReached, Message: policy.LimitMessage);
        }

        var bookRequest = new BookRequest
        {
            RequestedByUserId = userId,
            Title = title,
            Author = author,
            Series = series,
            CoverUrl = coverUrl,
            ChaptarrForeignBookId = foreignBookId,
            ReleaseDate = releaseDate,
            Status = policy.StatusFor(releaseDate)
        };
        db.BookRequests.Add(bookRequest);
        await db.SaveChangesAsync(ct);

        await db.Entry(bookRequest).Reference(r => r.RequestedByUser).LoadAsync(ct);
        var dto = BookRequestDto.From(bookRequest);
        await notifications.RequestUpdatedAsync(dto, ct);

        if (bookRequest.Status == BookRequestStatus.PendingApproval)
        {
            await externalNotifier.NotifyAsync(await settingsService.GetAsync(ct), ExternalNotifier.NeedsApproval(bookRequest), ct);
        }

        return new CreateResult(CreateOutcome.Created, dto);
    }
}
