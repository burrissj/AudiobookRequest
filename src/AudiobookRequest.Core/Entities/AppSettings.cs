namespace AudiobookRequest.Core.Entities;

/// <summary>
/// Single-row table holding all runtime configuration, populated by the first-run setup wizard.
/// </summary>
public class AppSettings
{
    public int Id { get; set; }

    public bool SetupCompleted { get; set; }

    public string? ChaptarrUrl { get; set; }
    public string? ChaptarrApiKey { get; set; }
    // Quality/metadata profile ids aren't configured here -- Chaptarr's root folders carry their
    // own audiobook-specific defaults (see IChaptarrClient.FindRootFolderAsync), so we just need the path.
    public string? ChaptarrRootFolderPath { get; set; }

    public string? JellyfinUrl { get; set; }
    public string? JellyfinApiKey { get; set; }
    /// <summary>
    /// The Jellyfin address people use from their own devices (often an HTTPS hostname). The "Open in Jellyfin"
    /// links use it; <see cref="JellyfinUrl"/> is the address this server uses, which can be a LAN IP that
    /// means nothing outside the house. Blank = use the server address.
    /// </summary>
    public string? JellyfinPublicUrl { get; set; }

    public string? StagingPath { get; set; }

    /// <summary>The folder the admin scans for loose audiobooks to tidy up and add to the library (the Import tab).</summary>
    public string? ImportFolderPath { get; set; }
    public string? LibraryPath { get; set; }

    // Remote Path Mapping: Chaptarr can run on a different machine than this service, so the file
    // paths it reports (via its own root-folder configuration) may not be reachable here directly.
    // When both are set, BookProcessingService swaps ChaptarrFilePathPrefix for LocalFilePathPrefix
    // before touching a reported file. Leave blank if Chaptarr's paths are already reachable as-is.
    public string? ChaptarrFilePathPrefix { get; set; }
    public string? LocalFilePathPrefix { get; set; }

    public int ScanIntervalMinutes { get; set; } = 15;
    public int OriginalRetentionDays { get; set; } = 90;

    /// <summary>Tokens: {Author}, {Series}, {Title}. Example: "{Author}/{Series}/{Title}/{Title}".</summary>
    public string NamingPattern { get; set; } = "{Author}/{Title}/{Title}";

    // Processing toggles: let Chaptarr's own file handling stand instead of BookProcessingService's.
    // EnableMoving off: originals are never cleaned up (copy-only). With EnableConversion also off,
    // nothing is copied at all and ProcessOneWithoutMovingAsync just waits for Chaptarr's own final
    // file to appear on disk, then marks the request Ready and triggers a Jellyfin rescan.
    public bool EnableRenaming { get; set; } = true;
    /// <summary>Embeds ID3-style tags (title/author/genre) and cover art. Chapter markers are unaffected -- they're structural, not metadata, and are always embedded when moving is on.</summary>
    public bool EnableConversion { get; set; } = true;
    public bool EnableMoving { get; set; } = true;

    // Request policy. Admins always bypass both.
    public bool RequireApproval { get; set; }
    /// <summary>Per non-admin user, rolling 7 days. 0 = unlimited.</summary>
    public int MaxRequestsPerUserPerWeek { get; set; }

    /// <summary>Pause processing when staging/library has less free space than this (GB). 0 = off.</summary>
    public int MinFreeDiskGb { get; set; } = 5;

    /// <summary>How often a wishlisted request re-triggers its Chaptarr search.</summary>
    public int WishlistRetryHours { get; set; } = 12;

    // External notifications -- any subset of channels may be configured.
    public bool NotifyOnReady { get; set; } = true;
    public bool NotifyOnFailed { get; set; } = true;
    public bool NotifyOnPendingApproval { get; set; } = true;
    public string? NotifyDiscordWebhookUrl { get; set; }
    public string? NotifyTelegramBotToken { get; set; }
    public string? NotifyTelegramChatId { get; set; }
    /// <summary>Full ntfy topic URL, e.g. https://ntfy.sh/my-audiobooks.</summary>
    public string? NotifyNtfyUrl { get; set; }
    /// <summary>Receives a JSON POST: { event, title, author, user, message }.</summary>
    public string? NotifyWebhookUrl { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpFrom { get; set; }
    public string? SmtpTo { get; set; }

    /// <summary>Tell the notification channels when an author or series someone follows has a new book.</summary>
    public bool NotifyOnNewRelease { get; set; } = true;

    /// <summary>
    /// When Chaptarr blocks a finished download only because it matched a sibling edition of the requested
    /// book (same author, same title ignoring "(Part 2 of 2)"-style suffixes), import it as the requested
    /// book automatically instead of waiting for an admin to press Fix import. Off by default.
    /// </summary>
    public bool AutoFixImports { get; set; }

    /// <summary>How many daily database backups to keep. 0 turns backups off.</summary>
    public int BackupRetentionCount { get; set; } = 14;
}
