using System.Text.RegularExpressions;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record BackupInfo(string FileName, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>
/// Dated copies of the SQLite database -- every request, user, follow and setting lives in that one
/// file. Uses SQLite's own VACUUM INTO, which writes a consistent snapshot while the app keeps running
/// (copying the file directly could catch it mid-write). Restoring is deliberately manual: stop the
/// service, replace data\app.db with a backup, start it again.
/// </summary>
public partial class DatabaseBackupService(AppDbContext db, SettingsService settingsService, AppPaths paths, ILogger<DatabaseBackupService> logger)
{
    [GeneratedRegex(@"^app-\d{8}-\d{6}\.db$")]
    private static partial Regex BackupNameRegex();

    public async Task<BackupInfo> CreateAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(paths.BackupDirectory);
        var name = $"app-{DateTime.Now:yyyyMMdd-HHmmss}.db";
        var path = Path.Combine(paths.BackupDirectory, name);

        // Parameterized: the path is a bound value, never spliced into the SQL.
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {path}", ct);
        logger.LogInformation("Database backup written to {Path}", path);

        await PruneAsync(ct);
        var info = new FileInfo(path);
        return new BackupInfo(name, info.Length, new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero));
    }

    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(paths.BackupDirectory))
        {
            return [];
        }

        return new DirectoryInfo(paths.BackupDirectory)
            .EnumerateFiles("app-*.db")
            .Where(f => BackupNameRegex().IsMatch(f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.Name, f.Length, new DateTimeOffset(f.CreationTimeUtc, TimeSpan.Zero)))
            .ToList();
    }

    /// <summary>The full path of a backup by name, or null. Only names this service produced are accepted, so no path can be smuggled in.</summary>
    public string? PathFor(string fileName)
    {
        if (!BackupNameRegex().IsMatch(fileName))
        {
            return null;
        }
        var path = Path.Combine(paths.BackupDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>True when automatic backups are on and none is less than <paramref name="minAge"/> old.</summary>
    public async Task<bool> IsDueAsync(TimeSpan minAge, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (settings.BackupRetentionCount <= 0)
        {
            return false;
        }
        var newest = List().FirstOrDefault();
        return newest is null || DateTimeOffset.UtcNow - newest.CreatedAt >= minAge;
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        var keep = (await settingsService.GetAsync(ct)).BackupRetentionCount;
        if (keep <= 0)
        {
            return;
        }

        foreach (var old in List().Skip(keep))
        {
            try
            {
                File.Delete(Path.Combine(paths.BackupDirectory, old.FileName));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete old backup {File}", old.FileName);
            }
        }
    }
}
