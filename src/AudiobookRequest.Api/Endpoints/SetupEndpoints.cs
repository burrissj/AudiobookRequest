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
    string NamingPattern,
    string? ChaptarrFilePathPrefix = null,
    string? LocalFilePathPrefix = null,
    bool EnableRenaming = true,
    bool EnableConversion = true,
    bool EnableMoving = true,
    bool RequireApproval = false,
    int MaxRequestsPerUserPerWeek = 0,
    int MinFreeDiskGb = 5,
    int WishlistRetryHours = 12,
    bool NotifyOnReady = true,
    bool NotifyOnFailed = true,
    bool NotifyOnPendingApproval = true,
    string? NotifyDiscordWebhookUrl = null,
    string? NotifyTelegramBotToken = null,
    string? NotifyTelegramChatId = null,
    string? NotifyNtfyUrl = null,
    string? NotifyWebhookUrl = null,
    string? SmtpHost = null,
    int SmtpPort = 587,
    bool SmtpUseSsl = true,
    string? SmtpUsername = null,
    string? SmtpPassword = null,
    string? SmtpFrom = null,
    string? SmtpTo = null,
    bool NotifyOnNewRelease = true,
    bool AutoFixImports = false,
    int BackupRetentionCount = 14,
    string? JellyfinPublicUrl = null);

public record TestConnectionRequest(string Url, string ApiKey);

public static class SetupEndpoints
{
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup");

        group.MapGet("/status", async (SettingsService settingsService, CancellationToken ct) =>
        {
            var settings = await settingsService.GetAsync(ct);
            return Results.Ok(new SetupStatusResponse(settings.SetupCompleted));
        });

        // Backs the Settings tab: an admin re-opening the same form the setup wizard used, prefilled
        // with the current values. Anonymous/first-run has nothing to fetch yet, so this is admin-only.
        group.MapGet("/", async (SettingsService settingsService, CancellationToken ct) =>
        {
            var s = await settingsService.GetAsync(ct);
            return Results.Ok(new SetupRequest(
                s.ChaptarrUrl ?? "", s.ChaptarrApiKey ?? "", s.ChaptarrRootFolderPath ?? "",
                s.JellyfinUrl ?? "", s.JellyfinApiKey ?? "",
                s.StagingPath ?? "", s.LibraryPath ?? "",
                s.ScanIntervalMinutes, s.OriginalRetentionDays, s.NamingPattern,
                s.ChaptarrFilePathPrefix, s.LocalFilePathPrefix,
                s.EnableRenaming, s.EnableConversion, s.EnableMoving,
                s.RequireApproval, s.MaxRequestsPerUserPerWeek, s.MinFreeDiskGb, s.WishlistRetryHours,
                s.NotifyOnReady, s.NotifyOnFailed, s.NotifyOnPendingApproval,
                s.NotifyDiscordWebhookUrl, s.NotifyTelegramBotToken, s.NotifyTelegramChatId, s.NotifyNtfyUrl, s.NotifyWebhookUrl,
                s.SmtpHost, s.SmtpPort, s.SmtpUseSsl, s.SmtpUsername, s.SmtpPassword, s.SmtpFrom, s.SmtpTo,
                s.NotifyOnNewRelease, s.AutoFixImports, s.BackupRetentionCount,
                s.JellyfinPublicUrl));
        }).RequireAuthorization("AdminOnly");

        // Anonymous on first run (nothing to protect yet); once completed, only a signed-in admin can
        // resubmit (e.g. to switch Chaptarr instances/providers) rather than anyone on the network.
        group.MapPost("/", async (SetupRequest req, SettingsService settingsService, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var current = await settingsService.GetAsync(ct);
            if (SetupAccess.Denial(current.SetupCompleted, principal.IsInRole("Admin"), http.Connection.RemoteIpAddress) is { } denial)
            {
                return Results.Problem(denial, statusCode: 403);
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
                s.ChaptarrFilePathPrefix = req.ChaptarrFilePathPrefix;
                s.LocalFilePathPrefix = req.LocalFilePathPrefix;
                s.EnableRenaming = req.EnableRenaming;
                s.EnableConversion = req.EnableConversion;
                s.EnableMoving = req.EnableMoving;
                s.RequireApproval = req.RequireApproval;
                s.MaxRequestsPerUserPerWeek = Math.Max(0, req.MaxRequestsPerUserPerWeek);
                s.MinFreeDiskGb = Math.Max(0, req.MinFreeDiskGb);
                s.WishlistRetryHours = Math.Max(1, req.WishlistRetryHours);
                s.NotifyOnReady = req.NotifyOnReady;
                s.NotifyOnFailed = req.NotifyOnFailed;
                s.NotifyOnPendingApproval = req.NotifyOnPendingApproval;
                s.NotifyDiscordWebhookUrl = Blank(req.NotifyDiscordWebhookUrl);
                s.NotifyTelegramBotToken = Blank(req.NotifyTelegramBotToken);
                s.NotifyTelegramChatId = Blank(req.NotifyTelegramChatId);
                s.NotifyNtfyUrl = Blank(req.NotifyNtfyUrl);
                s.NotifyWebhookUrl = Blank(req.NotifyWebhookUrl);
                s.SmtpHost = Blank(req.SmtpHost);
                s.SmtpPort = req.SmtpPort > 0 ? req.SmtpPort : 587;
                s.SmtpUseSsl = req.SmtpUseSsl;
                s.SmtpUsername = Blank(req.SmtpUsername);
                s.SmtpPassword = Blank(req.SmtpPassword);
                s.SmtpFrom = Blank(req.SmtpFrom);
                s.SmtpTo = Blank(req.SmtpTo);
                s.NotifyOnNewRelease = req.NotifyOnNewRelease;
                s.AutoFixImports = req.AutoFixImports;
                s.BackupRetentionCount = Math.Max(0, req.BackupRetentionCount);
                s.JellyfinPublicUrl = Blank(req.JellyfinPublicUrl);
                s.SetupCompleted = true;
            }, ct);

            return Results.NoContent();
        });

        // These make the server connect to whatever address is typed in, so they follow the same rule as setup itself:
        // open only during first-time setup from the home network, then admin-only.
        group.MapPost("/test-chaptarr", async (TestConnectionRequest req, IChaptarrClient chaptarr, SettingsService settingsService, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var current = await settingsService.GetAsync(ct);
            if (SetupAccess.Denial(current.SetupCompleted, principal.IsInRole("Admin"), http.Connection.RemoteIpAddress) is { } denial)
            {
                return Results.Problem(denial, statusCode: 403);
            }

            var ok = await chaptarr.TestConnectionAsync(req.Url, req.ApiKey, ct);
            return Results.Ok(new { success = ok });
        });

        group.MapPost("/test-jellyfin", async (TestConnectionRequest req, IJellyfinClient jellyfin, SettingsService settingsService, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var current = await settingsService.GetAsync(ct);
            if (SetupAccess.Denial(current.SetupCompleted, principal.IsInRole("Admin"), http.Connection.RemoteIpAddress) is { } denial)
            {
                return Results.Problem(denial, statusCode: 403);
            }

            var ok = await jellyfin.TestConnectionAsync(req.Url, req.ApiKey, ct);
            return Results.Ok(new { success = ok });
        });

        return app;
    }
}
