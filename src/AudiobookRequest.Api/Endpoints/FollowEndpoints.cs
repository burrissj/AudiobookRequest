using System.Security.Claims;
using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.Endpoints;

public record SetAutoRequestRequest(bool AutoRequest);

/// <summary>Following authors and series: list, follow, change auto-request, unfollow, and the "new for you" finds.</summary>
public static class FollowEndpoints
{
    public static IEndpointRouteBuilder MapFollowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/follows").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, FollowService follows, CancellationToken ct) =>
            Results.Ok(await follows.ListAsync(UserId(principal), ct)));

        group.MapPost("/", async (CreateFollowRequest req, ClaimsPrincipal principal, FollowService follows, CancellationToken ct) =>
        {
            var created = await follows.CreateAsync(UserId(principal), req, ct);
            return created is null
                ? Results.Problem("A follow needs a kind (Author or Series) and a name; a series also needs its id and author.", statusCode: 400)
                : Results.Ok(created);
        });

        group.MapPut("/{id:int}", async (int id, SetAutoRequestRequest req, ClaimsPrincipal principal, FollowService follows, CancellationToken ct) =>
        {
            var updated = await follows.SetAutoRequestAsync(UserId(principal), id, req.AutoRequest, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal principal, FollowService follows, CancellationToken ct) =>
            await follows.DeleteAsync(UserId(principal), id, ct) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/finds", async (ClaimsPrincipal principal, FollowService follows, CancellationToken ct) =>
            Results.Ok(await follows.FindsAsync(UserId(principal), ct)));

        group.MapPost("/finds/{id:int}/dismiss", async (int id, ClaimsPrincipal principal, FollowService follows, CancellationToken ct) =>
            await follows.DismissFindAsync(UserId(principal), id, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }

    private static int UserId(ClaimsPrincipal principal) => int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
