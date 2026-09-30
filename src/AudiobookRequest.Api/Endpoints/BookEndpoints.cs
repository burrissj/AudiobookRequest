using System.Security.Claims;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Endpoints;

public record BookSearchResultDto(
    string ForeignBookId,
    string Title,
    string AuthorName,
    string? SeriesTitle,
    string? Overview,
    string? CoverUrl);

public record CreateBookRequestRequest(
    string ForeignBookId,
    string Title,
    string AuthorName,
    string? SeriesTitle,
    string? CoverUrl);

public static class BookEndpoints
{
    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/books").RequireAuthorization();

        group.MapGet("/search", async (
            string term,
            SettingsService settingsService,
            IChaptarrClient chaptarr,
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
            var dtos = results.Select(r => new BookSearchResultDto(
                r.ForeignBookId, r.Title, r.AuthorName, r.SeriesTitle, r.Overview, r.CoverUrl));
            return Results.Ok(dtos);
        });

        group.MapGet("/requests", async (AppDbContext db, CancellationToken ct) =>
        {
            // SQLite can't translate ORDER BY on DateTimeOffset server-side, so sort client-side after fetching.
            var requests = await db.BookRequests
                .Include(r => r.RequestedByUser)
                .ToListAsync(ct);
            return Results.Ok(requests.OrderByDescending(r => r.RequestedAt).Select(BookRequestDto.From));
        });

        group.MapPost("/requests", async (
            CreateBookRequestRequest req,
            ClaimsPrincipal principal,
            AppDbContext db,
            INotificationSender notifications,
            CancellationToken ct) =>
        {
            var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var alreadyRequested = await db.BookRequests.AnyAsync(r => r.ChaptarrForeignBookId == req.ForeignBookId, ct);
            if (alreadyRequested)
            {
                return Results.Problem("This book has already been requested.", statusCode: 409);
            }

            var bookRequest = new BookRequest
            {
                RequestedByUserId = userId,
                Title = req.Title,
                Author = req.AuthorName,
                Series = req.SeriesTitle,
                CoverUrl = req.CoverUrl,
                ChaptarrForeignBookId = req.ForeignBookId,
                Status = BookRequestStatus.Requested
            };
            db.BookRequests.Add(bookRequest);
            await db.SaveChangesAsync(ct);

            await db.Entry(bookRequest).Reference(r => r.RequestedByUser).LoadAsync(ct);
            var dto = BookRequestDto.From(bookRequest);
            await notifications.RequestUpdatedAsync(dto, ct);

            return Results.Ok(dto);
        });

        return app;
    }
}
