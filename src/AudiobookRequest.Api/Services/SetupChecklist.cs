using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Services;

/// <param name="Status">"done", "todo" (required for the app to work properly) or "optional" (recommended, not required).</param>
/// <param name="Action">Where to go to fix it, in the app's own words.</param>
public record ChecklistItem(string Key, string Title, string Status, string Detail, string? Action = null);

/// <summary>
/// The "is everything set up?" list shown on the Admin → Health page. Built purely from what the health checks
/// and settings already say, so it can't disagree with them: required items are what the request pipeline needs
/// to run end to end, optional ones are what makes it pleasant (alerts, HTTPS, backups).
/// </summary>
public static class SetupChecklist
{
    public static IReadOnlyList<ChecklistItem> Build(
        IReadOnlyList<HealthCheck> checks, AppSettings settings, LibraryStatus library, int backupCount, bool isHttps)
    {
        HealthCheck? Find(string name) => checks.FirstOrDefault(c => c.Name == name);
        bool Ok(string name) => Find(name)?.Status == "ok";
        string Detail(string name, string fallback) => Find(name)?.Detail ?? fallback;

        var items = new List<ChecklistItem>
        {
            Required("chaptarr", "Chaptarr is connected", Ok("Chaptarr"), Detail("Chaptarr", "Not checked."), "Settings → Chaptarr"),
            Required("jellyfin", "Jellyfin is connected", Ok("Jellyfin"), Detail("Jellyfin", "Not checked."), "Settings → Jellyfin"),
            Required("ffmpeg", "ffmpeg is installed", Ok("ffmpeg") && Ok("ffprobe"),
                Ok("ffmpeg") && Ok("ffprobe") ? "ffmpeg and ffprobe both run." : $"{Detail("ffmpeg", "ffmpeg missing")} / {Detail("ffprobe", "ffprobe missing")}",
                "Reinstall the app, which bundles ffmpeg"),
            Required("folders", "Staging and library folders work", Ok("Staging folder") && Ok("Library folder"),
                Ok("Staging folder") && Ok("Library folder") ? "Both folders are writable with enough free space." : $"{Detail("Staging folder", "Staging folder not set")} {Detail("Library folder", "Library folder not set")}".Trim(),
                "Settings → File handling"),
            Required("files", "This server can see Chaptarr's files", Ok("Chaptarr files reachable"),
                Detail("Chaptarr files reachable", "Not checked."), "Settings → Remote path mapping (use a UNC path, not a mapped drive)"),
            Required("library", "The Jellyfin library has been scanned", library.BookCount > 0,
                library.BookCount > 0 ? $"{library.BookCount} audiobooks known." : "No audiobooks known yet, so already-owned books can't be recognized.",
                "Admin → Health → Rescan library now"),

            Optional("notifications", "Notifications are set up", HasHouseholdChannel(settings),
                HasHouseholdChannel(settings) ? "At least one channel is configured." : "Nobody gets told when a book is ready. Households can use a shared channel, and everyone can add their own under Account.",
                "Settings → Notifications"),
            Optional("https", "Using HTTPS", isHttps,
                isHttps ? "This page was loaded over HTTPS." : "This page was loaded over plain HTTP. HTTPS is needed to install the app on a phone.",
                "Put a reverse proxy with a certificate in front of the app"),
            Optional("backups", "Database backups are on",
                settings.BackupRetentionCount > 0 && backupCount > 0,
                settings.BackupRetentionCount <= 0 ? "Backups are turned off."
                    : backupCount == 0 ? "On, waiting for the first backup (taken a few minutes after the service starts)."
                    : $"{backupCount} backup(s) kept.",
                settings.BackupRetentionCount <= 0 ? "Settings → Backups" : "Admin → Health → Back up now"),
            Optional("jellyfin-link", "\"Open in Jellyfin\" works from outside your home",
                !string.IsNullOrWhiteSpace(settings.JellyfinPublicUrl),
                !string.IsNullOrWhiteSpace(settings.JellyfinPublicUrl) ? $"Links use {settings.JellyfinPublicUrl}." : "Links use the server's own Jellyfin address, which may only work on your home network.",
                "Settings → Jellyfin → Jellyfin address for users"),
        };
        return items;
    }

    private static ChecklistItem Required(string key, string title, bool done, string detail, string action) =>
        new(key, title, done ? "done" : "todo", detail, done ? null : action);

    private static ChecklistItem Optional(string key, string title, bool done, string detail, string action) =>
        new(key, title, done ? "done" : "optional", detail, done ? null : action);

    private static bool HasHouseholdChannel(AppSettings s) =>
        !string.IsNullOrWhiteSpace(s.NotifyDiscordWebhookUrl)
        || (!string.IsNullOrWhiteSpace(s.NotifyTelegramBotToken) && !string.IsNullOrWhiteSpace(s.NotifyTelegramChatId))
        || !string.IsNullOrWhiteSpace(s.NotifyNtfyUrl)
        || !string.IsNullOrWhiteSpace(s.NotifyWebhookUrl)
        || (!string.IsNullOrWhiteSpace(s.SmtpHost) && !string.IsNullOrWhiteSpace(s.SmtpFrom) && !string.IsNullOrWhiteSpace(s.SmtpTo));
}
