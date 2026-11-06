using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Cancels a request so it can be requested again from a clean slate: best-effort removes the book
/// from Chaptarr (if it ever got added there) so a fresh request doesn't collide with a leftover entry,
/// then deletes the local BookRequest row. Never touches files already on disk -- cancelling a request
/// that already finished (Ready) just removes its record, not the audiobook file in the library.
/// </summary>
public class RequestCancellationService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    INotificationSender notifications,
    ILogger<RequestCancellationService> logger)
{
    /// <returns>false if no request with that id exists.</returns>
    public async Task<bool> CancelAsync(int id, CancellationToken ct)
    {
        var request = await db.BookRequests.Include(r => r.ProcessedBook).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return false;
        }

        if (request.ChaptarrBookId is not null)
        {
            var settings = await settingsService.GetAsync(ct);
            if (!string.IsNullOrWhiteSpace(settings.ChaptarrUrl) && !string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
            {
                try
                {
                    await chaptarr.DeleteBookAsync(settings.ChaptarrUrl, settings.ChaptarrApiKey, request.ChaptarrBookId.Value, ct);
                }
                catch (Exception ex)
                {
                    // Not fatal to cancelling locally -- the book may already be gone from Chaptarr, or
                    // Chaptarr may be briefly unreachable. Worst case a fresh request hits the same
                    // "already exists" collision this was meant to avoid, which the user can retry.
                    logger.LogWarning(ex, "Failed to remove book {ChaptarrBookId} from Chaptarr while cancelling request {RequestId}", request.ChaptarrBookId, id);
                }
            }
        }

        db.BookRequests.Remove(request);
        await db.SaveChangesAsync(ct);
        await notifications.RequestRemovedAsync(id, ct);
        return true;
    }
}
