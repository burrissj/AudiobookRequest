using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.Endpoints;

/// <summary>App-wide values any signed-in user needs: where to open Jellyfin, and the link to a specific finished book.</summary>
public static class AppEndpoints
{
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/app").RequireAuthorization();

        // Only addresses and public store links -- never keys.
        group.MapGet("/config", async (JellyfinLinkService links, CancellationToken ct) =>
            Results.Ok(await links.GetConfigAsync(ct)));

        group.MapGet("/jellyfin-link", async (string title, string author, JellyfinLinkService links, CancellationToken ct) =>
        {
            var link = await links.LinkForBookAsync(title, author, ct);
            return link is null ? Results.NotFound() : Results.Ok(link);
        });

        // The running version and what changed in each release, for the "What's new" notice every signed-in person sees.
        group.MapGet("/version", () => Results.Ok(new VersionInfo(AppInfo.Version, AppInfo.Changelog)));

        return app;
    }
}
