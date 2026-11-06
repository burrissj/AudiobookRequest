using System.Diagnostics;
using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <param name="Status">"ok", "warn", "fail" or "skip" (not configured / not applicable).</param>
public record HealthCheck(string Name, string Status, string Detail);

public record HealthReport(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<HealthCheck> Checks,
    IReadOnlyList<JobRun> Jobs,
    IReadOnlyDictionary<string, int> RequestCounts,
    int StuckRequests,
    LibraryStatus Library,
    IReadOnlyList<ChecklistItem> Checklist);

/// <summary>
/// One place that answers "is everything wired up?": reachability of Chaptarr/Jellyfin, ffmpeg, the
/// staging/library folders (writable, enough free space), whether Chaptarr's files are reachable from
/// this machine, plus when each background job last ran and where requests are piling up.
/// </summary>
public class HealthService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    IJellyfinClient jellyfin,
    IProcessRunner processRunner,
    LibraryCatalogService libraryCatalog,
    DatabaseBackupService backups,
    BackgroundJobStatus jobStatus,
    AppPaths paths)
{
    /// <param name="request">The admin's own request, used to say how they are connected (HTTPS or not).</param>
    public async Task<HealthReport> RunAsync(Microsoft.AspNetCore.Http.HttpRequest? request, CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        var checks = new List<HealthCheck>();

        checks.Add(await CheckChaptarrAsync(s, ct));
        checks.Add(await CheckJellyfinAsync(s, ct));
        checks.Add(await CheckToolAsync("ffmpeg", paths.Ffmpeg, ct));
        checks.Add(await CheckToolAsync("ffprobe", paths.Ffprobe, ct));
        checks.Add(CheckFolder("Staging folder", s.StagingPath, s.MinFreeDiskGb));
        checks.Add(CheckFolder("Library folder", s.LibraryPath, s.MinFreeDiskGb));
        checks.Add(CheckChaptarrFilesReachable(s));
        if (request is not null)
        {
            checks.Add(ConnectionCheck.Describe(request));
        }

        var rows = await db.BookRequests.Select(r => new { r.Status, r.DiagnosticMessage }).ToListAsync(ct);
        var counts = rows.GroupBy(r => r.Status.ToString()).ToDictionary(g => g.Key, g => g.Count());
        var stuck = rows.Count(r => r.DiagnosticMessage != null && r.Status != BookRequestStatus.Ready);

        var library = await libraryCatalog.GetStatusAsync(ct);
        var checklist = SetupChecklist.Build(checks, s, library, backups.List().Count, request?.IsHttps == true);
        return new HealthReport(DateTimeOffset.UtcNow, checks, jobStatus.All(), counts, stuck, library, checklist);
    }

    private async Task<HealthCheck> CheckChaptarrAsync(AppSettings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.ChaptarrUrl) || string.IsNullOrWhiteSpace(s.ChaptarrApiKey))
        {
            return new HealthCheck("Chaptarr", "skip", "Not configured.");
        }

        try
        {
            var timer = Stopwatch.StartNew();
            var ok = await chaptarr.TestConnectionAsync(s.ChaptarrUrl, s.ChaptarrApiKey, ct);
            if (!ok)
            {
                return new HealthCheck("Chaptarr", "fail", $"{s.ChaptarrUrl} answered but rejected the API key.");
            }

            var root = await chaptarr.FindRootFolderAsync(s.ChaptarrUrl, s.ChaptarrApiKey, s.ChaptarrRootFolderPath ?? "", ct);
            return root is null
                ? new HealthCheck("Chaptarr", "fail", $"Connected, but it has no root folder at '{s.ChaptarrRootFolderPath}'. New requests can't be added.")
                : new HealthCheck("Chaptarr", "ok", $"Connected in {timer.ElapsedMilliseconds} ms; root folder '{root.Path}' found.");
        }
        catch (Exception ex)
        {
            return new HealthCheck("Chaptarr", "fail", $"Can't reach {s.ChaptarrUrl}: {ex.Message}");
        }
    }

    private async Task<HealthCheck> CheckJellyfinAsync(AppSettings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.JellyfinUrl) || string.IsNullOrWhiteSpace(s.JellyfinApiKey))
        {
            return new HealthCheck("Jellyfin", "skip", "Not configured.");
        }

        try
        {
            var timer = Stopwatch.StartNew();
            var ok = await jellyfin.TestConnectionAsync(s.JellyfinUrl, s.JellyfinApiKey, ct);
            return ok
                ? new HealthCheck("Jellyfin", "ok", $"Connected in {timer.ElapsedMilliseconds} ms.")
                : new HealthCheck("Jellyfin", "fail", $"{s.JellyfinUrl} answered but rejected the API key.");
        }
        catch (Exception ex)
        {
            return new HealthCheck("Jellyfin", "fail", $"Can't reach {s.JellyfinUrl}: {ex.Message}");
        }
    }

    private async Task<HealthCheck> CheckToolAsync(string name, string path, CancellationToken ct)
    {
        try
        {
            var result = await processRunner.RunAsync(path, ["-version"], ct);
            if (!result.Success)
            {
                return new HealthCheck(name, "fail", $"'{path}' exited with code {result.ExitCode}.");
            }
            var firstLine = result.StandardOutput.Split('\n', 2)[0].Trim();
            return new HealthCheck(name, "ok", firstLine.Length > 0 ? firstLine : path);
        }
        catch (Exception ex)
        {
            return new HealthCheck(name, "fail", $"Couldn't run '{path}': {ex.Message}");
        }
    }

    private static HealthCheck CheckFolder(string name, string? path, int minFreeGb)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new HealthCheck(name, "skip", "Not configured.");
        }

        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".abr-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            return new HealthCheck(name, "fail", $"'{path}' isn't writable by this service: {ex.Message}");
        }

        var free = DiskSpace.GetFreeBytes(path);
        if (free is null)
        {
            return new HealthCheck(name, "ok", $"'{path}' is writable (free space unknown).");
        }

        var freeGb = free.Value / 1024.0 / 1024 / 1024;
        var detail = $"'{path}' is writable; {freeGb:0.0} GB free.";
        return minFreeGb > 0 && freeGb < minFreeGb
            ? new HealthCheck(name, "warn", $"{detail} Below your {minFreeGb} GB minimum, so processing is paused.")
            : new HealthCheck(name, "ok", detail);
    }

    // The lesson from the first install: Chaptarr reports paths from ITS filesystem. If this service can't
    // see them (wrong mapping, mapped drive letter under a service account), copies fail with "not found".
    private static HealthCheck CheckChaptarrFilesReachable(AppSettings s)
    {
        const string name = "Chaptarr files reachable";
        if (string.IsNullOrWhiteSpace(s.ChaptarrRootFolderPath))
        {
            return new HealthCheck(name, "skip", "No Chaptarr root folder configured.");
        }

        var local = RemotePathMapper.ToLocalPath(s.ChaptarrRootFolderPath, s.ChaptarrFilePathPrefix, s.LocalFilePathPrefix);
        try
        {
            return Directory.Exists(local)
                ? new HealthCheck(name, "ok", $"This service can see '{local}'.")
                : new HealthCheck(name, "warn",
                    $"'{local}' isn't visible from this machine, so finished books can't be picked up. Check Remote path mapping in Settings (use a UNC path like \\\\server\\share, not a mapped drive letter, for a Windows service).");
        }
        catch (Exception ex)
        {
            return new HealthCheck(name, "warn", $"Couldn't check '{local}': {ex.Message}");
        }
    }
}
