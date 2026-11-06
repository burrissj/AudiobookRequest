using System.Security.Claims;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Recommendations;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Endpoints;

public record BookSearchResultDto(
    string ForeignBookId,
    string Title,
    string AuthorName,
    string? SeriesTitle,
    string? Overview,
    string? CoverUrl,
    DateTimeOffset? ReleaseDate,
    double? RatingValue,
    int? RatingVotes,
    bool AlreadyInLibrary,
    string? SimilarInLibraryTitle = null,
    int? RuntimeMinutes = null,
    IReadOnlyList<string>? Narrators = null);

public record AudiobookMetadataDto(
    int? RuntimeMinutes,
    IReadOnlyList<string> Narrators,
    IReadOnlyList<string> Genres,
    string? Description,
    string? FormatType,
    string? Language,
    string? PublisherName,
    string? SeriesTitle,
    string? SeriesAsin,
    int? BundleBookCount);

public record CreateBookRequestRequest(
    string ForeignBookId,
    string Title,
    string AuthorName,
    string? SeriesTitle,
    string? CoverUrl,
    DateTimeOffset? ReleaseDate = null);

public record ExpandSeriesRequest(string SeriesTitle, string SeriesAsin, string AuthorName);
public record ExpandSeriesResponse(IReadOnlyList<BookRequestDto> Created, int SkippedByLimit);
public record WishlistRequest(bool Enabled);
public record DenyRequestRequest(string? Note);

public static class BookEndpoints
{
    // Runtime/narrator filters need an external lookup per result, so only this many top results are checked.
    private const int MaxFilteredResults = 12;

    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/books").RequireAuthorization();

        group.MapGet("/search", async (
            string term,
            string? narrator,
            int? minHours,
            int? maxHours,
            SettingsService settingsService,
            IChaptarrClient chaptarr,
            LibraryCatalogService libraryCatalog,
            MetadataLookupService metadataLookup,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Results.Ok(Array.Empty<BookSearchResultDto>());
            }

            var settings = await settingsService.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
            {
                return Results.Problem("Chaptarr is not configured.", statusCode: 400);
            }

            var results = await chaptarr.LookupBooksAsync(settings.ChaptarrUrl, settings.ChaptarrApiKey, term, ct);
            var library = await libraryCatalog.GetIndexAsync(ct);

            var filtering = !string.IsNullOrWhiteSpace(narrator) || minHours is > 0 || maxHours is > 0;
            if (!filtering)
            {
                return Results.Ok(results.Select(r => ToDto(r, library, null)));
            }

            // Narrator and length aren't in Chaptarr's lookup, so look them up for the top results only
            // (cached, a few at a time) and drop anything that doesn't match -- or can't be determined.
            var top = results.Take(MaxFilteredResults).ToList();
            var metadata = await metadataLookup.GetManyAsync(top.Select(r => (r.Title, r.AuthorName)).ToList(), ct);
            var matched = new List<BookSearchResultDto>();
            for (var i = 0; i < top.Count; i++)
            {
                var m = metadata[i];
                if (m is null)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(narrator) &&
                    !m.Narrators.Any(n => n.Contains(narrator.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                if ((minHours is > 0 || maxHours is > 0) && m.RuntimeMinutes is null)
                {
                    continue;
                }
                if (minHours is > 0 && m.RuntimeMinutes < minHours * 60)
                {
                    continue;
                }
                if (maxHours is > 0 && m.RuntimeMinutes > maxHours * 60)
                {
                    continue;
                }
                matched.Add(ToDto(top[i], library, m));
            }
            return Results.Ok(matched);
        });

        group.MapGet("/recommendations", async (
            ClaimsPrincipal principal,
            RecommendationService recommendations,
            CancellationToken ct) =>
        {
            var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var results = await recommendations.GetRecommendationsAsync(userId, ct);
            return Results.Ok(results);
        });

        group.MapGet("/metadata", async (
            string title,
            string? author,
            MetadataLookupService metadataLookup,
            CancellationToken ct) =>
        {
            // Some library books have no author on file; the lookup then goes by title alone.
            var metadata = await metadataLookup.GetAsync(title, author ?? "", ct);
            return Results.Ok(new AudiobookMetadataDto(
                metadata?.RuntimeMinutes, metadata?.Narrators ?? [], metadata?.Genres ?? [],
                metadata?.Description, metadata?.FormatType, metadata?.Language, metadata?.PublisherName,
                metadata?.SeriesTitle, metadata?.SeriesAsin, AudiobookSeries.ParseBundleCount(metadata?.SeriesSequence)));
        });

        // How much of a series you already have / have asked for -- backs the progress block on the detail page.
        group.MapGet("/series/status", async (
            string seriesTitle,
            string seriesAsin,
            string author,
            SeriesRequestService seriesService,
            CancellationToken ct) =>
        {
            var status = await seriesService.GetStatusAsync(seriesTitle, seriesAsin, author, ct);
            return status is null ? Results.NotFound() : Results.Ok(status);
        });

        // Requests every book of a series (or splits a bundle/omnibus into its members). Books already
        // owned or requested are skipped, so this doubles as "request the missing ones".
        group.MapPost("/series/expand", async (
            ExpandSeriesRequest req,
            ClaimsPrincipal principal,
            SeriesRequestService seriesService,
            RequestPolicyService policyService,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var policy = await policyService.EvaluateAsync(userId, IsAdmin(principal), ct);
            if (policy.Remaining <= 0)
            {
                return Results.Problem(policy.LimitMessage, statusCode: 429);
            }

            var result = await seriesService.ExpandAndRequestSeriesAsync(req.SeriesTitle, req.SeriesAsin, req.AuthorName, userId, policy, ct);
            return Results.Ok(new ExpandSeriesResponse(result.Created, result.SkippedByLimit));
        });

        group.MapGet("/requests", async (AppDbContext db, CancellationToken ct) =>
        {
            // SQLite can't translate ORDER BY on DateTimeOffset server-side, so sort client-side after fetching.
            var requests = await db.BookRequests
                .Include(r => r.RequestedByUser)
                .ToListAsync(ct);
            return Results.Ok(requests.OrderByDescending(r => r.RequestedAt).Select(BookRequestDto.From));
        });

        // Backs the manual "Refresh" button: runs one orchestration pass (poll Chaptarr queue/history,
        // advance statuses) and one processing pass (pick up anything Chaptarr already imported) right
        // now, instead of waiting for the next background-service tick, then returns the fresh list.
        group.MapPost("/requests/refresh", async (
            ChaptarrOrchestrationService orchestration,
            BookProcessingService processing,
            AppDbContext db,
            CancellationToken ct) =>
        {
            await orchestration.RunOnceAsync(ct);
            await processing.ProcessReadyRequestsAsync(ct);

            var requests = await db.BookRequests
                .Include(r => r.RequestedByUser)
                .ToListAsync(ct);
            return Results.Ok(requests.OrderByDescending(r => r.RequestedAt).Select(BookRequestDto.From));
        });

        group.MapPost("/requests", async (
            CreateBookRequestRequest req,
            ClaimsPrincipal principal,
            RequestCreationService creation,
            CancellationToken ct) =>
        {
            var result = await creation.CreateAsync(
                UserId(principal), IsAdmin(principal), req.ForeignBookId, req.Title, req.AuthorName, req.SeriesTitle, req.CoverUrl, req.ReleaseDate, ct);

            return result.Outcome switch
            {
                CreateOutcome.Created => Results.Ok(result.Request),
                CreateOutcome.LimitReached => Results.Problem(result.Message, statusCode: 429),
                _ => Results.Problem(result.Message, statusCode: 409),
            };
        });

        // Lets a failed request be retried against a different Chaptarr match -- e.g. the original title
        // didn't turn up any indexer results, so the user picks a better-matching search result instead.
        // Resets the request back to Requested with the new book's identity; the orchestration service
        // (or the "Refresh" button) picks it up from there like any other fresh request.
        group.MapPut("/requests/{id:int}/retry", async (
            int id,
            CreateBookRequestRequest req,
            ClaimsPrincipal principal,
            AppDbContext db,
            SettingsService settingsService,
            IChaptarrClient chaptarr,
            INotificationSender notifications,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var request = await db.BookRequests.Include(r => r.RequestedByUser).FirstOrDefaultAsync(r => r.Id == id, ct);
            if (request is null)
            {
                return Results.NotFound();
            }
            if (!CanManage(principal, request))
            {
                return Results.Forbid();
            }
            if (request.Status != BookRequestStatus.Failed)
            {
                return Results.Problem("Only a failed request can be retried.", statusCode: 400);
            }

            var alreadyRequested = await db.BookRequests.AnyAsync(r => r.ChaptarrForeignBookId == req.ForeignBookId && r.Id != id, ct);
            if (alreadyRequested)
            {
                return Results.Problem("This book has already been requested.", statusCode: 409);
            }

            if (request.ChaptarrBookId is not null && request.ChaptarrForeignBookId != req.ForeignBookId)
            {
                // Clean up the old Chaptarr entry before switching to a different book, so it doesn't
                // keep searching/downloading a match the user explicitly rejected.
                var settings = await settingsService.GetAsync(ct);
                if (!string.IsNullOrWhiteSpace(settings.ChaptarrUrl) && !string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
                {
                    try
                    {
                        await chaptarr.DeleteBookAsync(settings.ChaptarrUrl, settings.ChaptarrApiKey, request.ChaptarrBookId.Value, ct);
                    }
                    catch (Exception ex)
                    {
                        loggerFactory.CreateLogger("BookEndpoints").LogWarning(ex,
                            "Failed to remove old Chaptarr book {ChaptarrBookId} before retrying request {RequestId}", request.ChaptarrBookId, id);
                    }
                }
            }

            request.Title = req.Title;
            request.Author = req.AuthorName;
            request.Series = req.SeriesTitle;
            request.CoverUrl = req.CoverUrl;
            request.ChaptarrForeignBookId = req.ForeignBookId;
            request.ChaptarrBookId = null;
            request.ReleaseDate = req.ReleaseDate;
            request.Status = Core.Library.ReleaseSchedule.IsUnreleased(req.ReleaseDate, DateTimeOffset.UtcNow)
                ? BookRequestStatus.Upcoming
                : BookRequestStatus.Requested;
            request.FailureReason = null;
            request.DiagnosticMessage = null;
            request.AutoFixAttemptedAt = null;
            request.LastSearchAt = null;
            request.SnatchedAt = null;
            request.DownloadProgressPercent = null;
            request.DownloadSizeBytes = null;
            request.DownloadSizeLeftBytes = null;
            request.DownloadClientName = null;
            request.RequestedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var dto = BookRequestDto.From(request);
            await notifications.RequestUpdatedAsync(dto, ct);
            return Results.Ok(dto);
        });

        // Re-runs the search for the same book -- for a failed request, or one that hasn't found anything yet.
        group.MapPost("/requests/{id:int}/search-again", async (
            int id,
            ClaimsPrincipal principal,
            AppDbContext db,
            RequestActionsService actions,
            CancellationToken ct) =>
        {
            var guard = await GuardOwnerAsync(db, principal, id, ct);
            return guard ?? ToHttp(await actions.SearchAgainAsync(id, ct));
        });

        // Wishlist = keep searching on a schedule instead of failing when nothing turns up.
        group.MapPut("/requests/{id:int}/wishlist", async (
            int id,
            WishlistRequest req,
            ClaimsPrincipal principal,
            AppDbContext db,
            RequestActionsService actions,
            CancellationToken ct) =>
        {
            var guard = await GuardOwnerAsync(db, principal, id, ct);
            return guard ?? ToHttp(await actions.SetWishlistedAsync(id, req.Enabled, ct));
        });

        // Admin-only because it makes Chaptarr import (move/copy) files: tells it to import a blocked
        // download as the requested book. GET previews what would happen; POST does it.
        group.MapGet("/requests/{id:int}/fix-import", async (int id, ImportFixService fixer, CancellationToken ct) =>
        {
            var preview = await fixer.PreviewAsync(id, ct);
            return preview is null ? Results.NotFound() : Results.Ok(preview);
        }).RequireAuthorization("AdminOnly");

        group.MapPost("/requests/{id:int}/fix-import", async (int id, ImportFixService fixer, ChaptarrOrchestrationService orchestration, CancellationToken ct) =>
        {
            var result = await fixer.FixAsync(id, ct);
            if (result is null)
            {
                return Results.NotFound();
            }
            // Pick up the outcome now rather than on the next 20s tick.
            await orchestration.RunOnceAsync(ct);
            return Results.Ok(result);
        }).RequireAuthorization("AdminOnly");

        group.MapPost("/requests/{id:int}/approve", async (int id, RequestActionsService actions, CancellationToken ct) =>
            ToHttp(await actions.ApproveAsync(id, ct))).RequireAuthorization("AdminOnly");

        group.MapPost("/requests/{id:int}/deny", async (int id, DenyRequestRequest req, RequestActionsService actions, CancellationToken ct) =>
            ToHttp(await actions.DenyAsync(id, req.Note, ct))).RequireAuthorization("AdminOnly");

        // Cancels a request outright so it can be requested again from scratch -- best-effort removes
        // the book from Chaptarr too (see RequestCancellationService), never touches files on disk.
        group.MapDelete("/requests/{id:int}", async (
            int id,
            ClaimsPrincipal principal,
            AppDbContext db,
            RequestCancellationService cancellation,
            CancellationToken ct) =>
        {
            var guard = await GuardOwnerAsync(db, principal, id, ct);
            if (guard is not null)
            {
                return guard;
            }
            var found = await cancellation.CancelAsync(id, ct);
            return found ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    internal static BookSearchResultDto ToDto(ChaptarrBookLookupResult r, Core.Library.LibraryIndex library, AudiobookMetadata? metadata) => new(
        r.ForeignBookId, r.Title, r.AuthorName, r.SeriesTitle, r.Overview, r.CoverUrl,
        r.ReleaseDate, r.RatingValue, r.RatingVotes,
        library.IsOwned(r.Title, r.AuthorName),
        library.FindSimilar(r.Title, r.AuthorName),
        metadata?.RuntimeMinutes,
        metadata?.Narrators);

    private static int UserId(ClaimsPrincipal principal) => int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static bool IsAdmin(ClaimsPrincipal principal) => principal.IsInRole("Admin");

    // Anyone can see all requests (that's how "already requested" works), but only the requester or an
    // admin may change one.
    private static bool CanManage(ClaimsPrincipal principal, BookRequest request) =>
        IsAdmin(principal) || request.RequestedByUserId == UserId(principal);

    private static async Task<IResult?> GuardOwnerAsync(AppDbContext db, ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var owner = await db.BookRequests.Where(r => r.Id == id).Select(r => (int?)r.RequestedByUserId).FirstOrDefaultAsync(ct);
        if (owner is null)
        {
            return Results.NotFound();
        }
        return IsAdmin(principal) || owner == UserId(principal) ? null : Results.Forbid();
    }

    private static IResult ToHttp(RequestActionResult result) => result.Outcome switch
    {
        RequestActionOutcome.Ok => Results.Ok(result.Request),
        RequestActionOutcome.NotFound => Results.NotFound(),
        _ => Results.Problem(result.Error, statusCode: 400),
    };
}
