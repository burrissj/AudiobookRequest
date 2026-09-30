using System.Security.Claims;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;

namespace AudiobookRequest.Api.Endpoints;

public record SetupStatusResponse(bool SetupCompleted);

public record SetupRequest(
    string ChaptarrUrl,
    string ChaptarrApiKey,
    string ChaptarrRootFolderPath,
    string JellyfinUrl,
    string JellyfinApiKey,
    string StagingPath,
    string LibraryPath,
    int ScanIntervalMinutes,
    int OriginalRetentionDays,
    string NamingPattern);

public record TestConnectionRequest(string Url, string ApiKey);

public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup");

        group.MapGet("/status", async (SettingsService settingsService, CancellationToken ct) =>
        {
            var settings = await settingsService.GetAsync(ct);
            return Results.Ok(new SetupStatusResponse(settings.SetupCompleted));
        });

        // Anonymous on first run (nothing to protect yet); once completed, only a signed-in admin can
        // resubmit (e.g. to switch Chaptarr instances/providers) rather than anyone on the network.
        group.MapPost("/", async (SetupRequest req, SettingsService settingsService, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var current = await settingsService.GetAsync(ct);
            if (current.SetupCompleted && !principal.IsInRole("Admin"))
            {
                return Results.Problem("Setup has already been completed. Sign in as an admin to change it.", statusCode: 403);
            }

            await settingsService.UpdateAsync(s =>
            {
                s.ChaptarrUrl = req.ChaptarrUrl;
                s.ChaptarrApiKey = req.ChaptarrApiKey;
                s.ChaptarrRootFolderPath = req.ChaptarrRootFolderPath;
                s.JellyfinUrl = req.JellyfinUrl;
                s.JellyfinApiKey = req.JellyfinApiKey;
                s.StagingPath = req.StagingPath;
                s.LibraryPath = req.LibraryPath;
                s.ScanIntervalMinutes = req.ScanIntervalMinutes;
                s.OriginalRetentionDays = req.OriginalRetentionDays;
                s.NamingPattern = req.NamingPattern;
                s.SetupCompleted = true;
            }, ct);

            return Results.NoContent();
        });

        group.MapPost("/test-chaptarr", async (TestConnectionRequest req, IChaptarrClient chaptarr, CancellationToken ct) =>
        {
            var ok = await chaptarr.TestConnectionAsync(req.Url, req.ApiKey, ct);
            return Results.Ok(new { success = ok });
        });

        group.MapPost("/test-jellyfin", async (TestConnectionRequest req, IJellyfinClient jellyfin, CancellationToken ct) =>
        {
            var ok = await jellyfin.TestConnectionAsync(req.Url, req.ApiKey, ct);
            return Results.Ok(new { success = ok });
        });

        return app;
    }
}
