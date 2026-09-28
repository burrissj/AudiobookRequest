using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;

namespace AudiobookRequest.Api.Endpoints;

public record SetupStatusResponse(bool SetupCompleted);

public record SetupRequest(
    string ReadarrUrl,
    string ReadarrApiKey,
    string ReadarrRootFolderPath,
    int ReadarrQualityProfileId,
    int ReadarrMetadataProfileId,
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

        // First run only: once SetupCompleted is true, this endpoint requires Admin (settings edits move
        // behind auth in Phase 2 alongside the rest of the authenticated app).
        group.MapPost("/", async (SetupRequest req, SettingsService settingsService, CancellationToken ct) =>
        {
            var current = await settingsService.GetAsync(ct);
            if (current.SetupCompleted)
            {
                return Results.Problem("Setup has already been completed.", statusCode: 400);
            }

            await settingsService.UpdateAsync(s =>
            {
                s.ReadarrUrl = req.ReadarrUrl;
                s.ReadarrApiKey = req.ReadarrApiKey;
                s.ReadarrRootFolderPath = req.ReadarrRootFolderPath;
                s.ReadarrQualityProfileId = req.ReadarrQualityProfileId;
                s.ReadarrMetadataProfileId = req.ReadarrMetadataProfileId;
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

        group.MapPost("/test-readarr", async (TestConnectionRequest req, IReadarrClient readarr, CancellationToken ct) =>
        {
            var ok = await readarr.TestConnectionAsync(req.Url, req.ApiKey, ct);
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
