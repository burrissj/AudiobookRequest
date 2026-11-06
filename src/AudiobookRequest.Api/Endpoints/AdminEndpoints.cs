using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.Endpoints;

public record NotificationTestResponse(IReadOnlyList<ChannelResult> Results, bool AnyConfigured);

public record BackupListResponse(IReadOnlyList<BackupInfo> Backups, int RetentionCount);

/// <summary>Admin-only operational tools: health, logs, library rescan, notification test.</summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("AdminOnly");

        group.MapGet("/health", async (HttpContext http, HealthService health, CancellationToken ct) =>
            Results.Ok(await health.RunAsync(http.Request, ct)));

        group.MapGet("/logs", (LogViewerService logs, string? file, string? level, string? search, int? limit) =>
            Results.Ok(logs.Read(file, level ?? "all", search, limit ?? 200)));

        group.MapGet("/library", async (LibraryCatalogService library, CancellationToken ct) =>
            Results.Ok(await library.GetStatusAsync(ct)));

        // Forces an immediate re-read of the Jellyfin library instead of waiting for the 6-hourly refresh.
        group.MapPost("/library/rescan", async (LibraryCatalogService library, BackgroundJobStatus jobStatus, CancellationToken ct) =>
        {
            try
            {
                var ran = await library.RefreshAsync(ct);
                if (!ran)
                {
                    return Results.Problem("Jellyfin isn't configured yet.", statusCode: 400);
                }
                jobStatus.Success(BackgroundServices.LibraryCatalogRefreshBackgroundService.JobName);
            }
            catch (Exception ex)
            {
                jobStatus.Failure(BackgroundServices.LibraryCatalogRefreshBackgroundService.JobName, ex);
                return Results.Problem($"Couldn't read the Jellyfin library: {ex.Message}", statusCode: 502);
            }
            return Results.Ok(await library.GetStatusAsync(ct));
        });

        // Finished requests whose book was deleted from Jellyfin: list them (live check), then remove the chosen records.
        group.MapGet("/library/stale", async (LibraryCleanupService cleanup, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await cleanup.ScanAsync(ct));
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem($"Couldn't read the Jellyfin library: {ex.Message}", statusCode: 502);
            }
        });

        group.MapPost("/library/stale/remove", async (RemoveStaleRequest req, LibraryCleanupService cleanup, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await cleanup.RemoveAsync(req.RequestIds, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem($"Couldn't read the Jellyfin library: {ex.Message}", statusCode: 502);
            }
        });

        group.MapGet("/version", () => Results.Ok(new VersionInfo(AppInfo.Version, AppInfo.Changelog)));

        // Database backups: list, take one now, download one. Restoring is manual (see DatabaseBackupService).
        group.MapGet("/backups", async (DatabaseBackupService backups, SettingsService settingsService, CancellationToken ct) =>
            Results.Ok(new BackupListResponse(backups.List(), (await settingsService.GetAsync(ct)).BackupRetentionCount)));

        group.MapPost("/backups", async (DatabaseBackupService backups, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await backups.CreateAsync(ct));
            }
            catch (Exception ex)
            {
                return Results.Problem($"The backup failed: {ex.Message}", statusCode: 500);
            }
        });

        group.MapGet("/backups/{fileName}", (string fileName, DatabaseBackupService backups) =>
        {
            var path = backups.PathFor(fileName);
            return path is null ? Results.NotFound() : Results.File(path, "application/octet-stream", fileName);
        });

        group.MapPost("/notifications/test", async (SettingsService settingsService, IExternalNotifier notifier, CancellationToken ct) =>
        {
            var settings = await settingsService.GetAsync(ct);
            var results = await notifier.TestAsync(settings, ct);
            return Results.Ok(new NotificationTestResponse(results, results.Count > 0));
        });

        return app;
    }
}
