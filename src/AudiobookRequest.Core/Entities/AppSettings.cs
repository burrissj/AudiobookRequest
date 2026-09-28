namespace AudiobookRequest.Core.Entities;

/// <summary>
/// Single-row table holding all runtime configuration, populated by the first-run setup wizard.
/// </summary>
public class AppSettings
{
    public int Id { get; set; }

    public bool SetupCompleted { get; set; }

    public string? ReadarrUrl { get; set; }
    public string? ReadarrApiKey { get; set; }
    public string? ReadarrRootFolderPath { get; set; }
    public int? ReadarrQualityProfileId { get; set; }
    public int? ReadarrMetadataProfileId { get; set; }

    public string? JellyfinUrl { get; set; }
    public string? JellyfinApiKey { get; set; }

    public string? StagingPath { get; set; }
    public string? LibraryPath { get; set; }

    public int ScanIntervalMinutes { get; set; } = 15;
    public int OriginalRetentionDays { get; set; } = 90;

    /// <summary>Tokens: {Author}, {Series}, {Title}. Example: "{Author}/{Series}/{Title}/{Title}".</summary>
    public string NamingPattern { get; set; } = "{Author}/{Title}/{Title}";
}
