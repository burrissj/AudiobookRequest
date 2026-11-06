using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Recommendations;

namespace AudiobookRequest.Api.Endpoints;

public record ResolveBookRequest(string Title, string Author);

/// <summary>The For You page: category sections, a category's full list, category search, and matching a catalog book to Chaptarr.</summary>
public static class BrowseEndpoints
{
    public static IEndpointRouteBuilder MapBrowseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/browse").RequireAuthorization();

        // A different seed is a "shuffle"; omitting it gives everyone the same page for the day.
        group.MapGet("/home", async (int? seed, int? count, CatalogBrowseService browse, CancellationToken ct) =>
            Results.Ok(await browse.HomeAsync(seed ?? DateOnly.FromDateTime(DateTime.UtcNow).DayNumber, Math.Clamp(count ?? 6, 1, 12), ct)));

        // The whole curated list, small enough to search in the browser as you type.
        group.MapGet("/categories", () =>
            Results.Ok(CategoryCatalog.All.Select(c => new
            {
                Category = CatalogBrowseService.Describe(c),
                Tags = c.Tags ?? [],
            })));

        // id "q" is a free-text search and id "a" is one author's books (q carries the name); both are shown
        // with the same screen as a curated category.
        group.MapGet("/category/{id}", async (string id, string? sort, int? page, string? q, CatalogBrowseService browse, CancellationToken ct) =>
        {
            BrowseCategory? category = id switch
            {
                BrowseCategory.CustomId => string.IsNullOrWhiteSpace(q) ? null : BrowseCategory.Custom(q),
                BrowseCategory.AuthorId => string.IsNullOrWhiteSpace(q) ? null : BrowseCategory.ForAuthor(q),
                _ => CategoryCatalog.Find(id),
            };
            return category is null
                ? Results.NotFound()
                : Results.Ok(await browse.PageAsync(category, sort, page ?? 0, ct));
        });

        group.MapPost("/resolve", async (ResolveBookRequest req, CatalogBrowseService browse, CancellationToken ct) =>
        {
            var match = await browse.ResolveAsync(req.Title, req.Author, ct);
            return match is null
                ? Results.Problem($"Chaptarr doesn't know \"{req.Title}\" by {req.Author}, so it can't be requested yet.", statusCode: 404)
                : Results.Ok(match);
        });

        return app;
    }
}
