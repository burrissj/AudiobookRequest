using System.Text.Json;
using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Takes a request that Chaptarr has finished downloading and importing (Status == Processing) and turns
/// it into a finished library entry: copies the imported files to staging (originals stay untouched in
/// Chaptarr's root folder), combines/tags/embeds cover art via ffmpeg, files the result into the library
/// under the configured naming pattern, and flips the request to Ready.
/// </summary>
public class BookProcessingService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    IJellyfinClient jellyfin,
    ICoverArtDownloader coverArtDownloader,
    IAudioCombiner audioCombiner,
    INotificationSender notifications,
    IExternalNotifier externalNotifier,
    ILogger<BookProcessingService> logger)
{
    private const string PausedPrefix = "Paused:";

    public async Task ProcessReadyRequestsAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey)
            || string.IsNullOrWhiteSpace(settings.StagingPath) || string.IsNullOrWhiteSpace(settings.LibraryPath))
        {
            return;
        }

        var pending = await db.BookRequests
            .Include(r => r.RequestedByUser)
            .Where(r => r.Status == BookRequestStatus.Processing)
            .ToListAsync(ct);

        // Detection-only mode needs both switches off; either one alone still runs the copy/merge pipeline.
        var copies = settings.EnableMoving || settings.EnableConversion;
        if (copies && pending.Count > 0 && await IsLowOnDiskAsync(settings, pending, ct))
        {
            return;
        }

        foreach (var request in pending)
        {
            try
            {
                if (copies)
                {
                    await ProcessOneAsync(request, settings, ct);
                }
                else
                {
                    await ProcessOneWithoutMovingAsync(request, settings, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process request {RequestId} ({Title})", request.Id, request.Title);
                // Retried every tick, so surface the reason on the request instead of leaving it silently stuck.
                var message = $"Processing error (will retry): {ex.Message}";
                if (request.DiagnosticMessage != message)
                {
                    request.DiagnosticMessage = message;
                    await db.SaveChangesAsync(ct);
                    await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
                }
            }
        }
    }

    /// <summary>
    /// Pauses the whole pass (and tells the waiting requests why) when staging or the library drive is
    /// below the configured free-space floor, instead of failing halfway through a copy.
    /// </summary>
    private async Task<bool> IsLowOnDiskAsync(AppSettings settings, List<BookRequest> pending, CancellationToken ct)
    {
        string? problem = null;
        if (settings.MinFreeDiskGb > 0)
        {
            var floor = settings.MinFreeDiskGb * 1024L * 1024 * 1024;
            foreach (var path in new[] { settings.StagingPath, settings.LibraryPath }.Distinct())
            {
                var free = DiskSpace.GetFreeBytes(path);
                if (free is not null && free < floor)
                {
                    problem = $"{PausedPrefix} only {free.Value / 1024.0 / 1024 / 1024:0.0} GB free on '{path}' (minimum {settings.MinFreeDiskGb} GB). Free up space and it will continue automatically.";
                    break;
                }
            }
        }

        foreach (var request in pending)
        {
            var current = request.DiagnosticMessage;
            if (problem is not null && current != problem)
            {
                request.DiagnosticMessage = problem;
            }
            else if (problem is null && current is not null && current.StartsWith(PausedPrefix, StringComparison.Ordinal))
            {
                request.DiagnosticMessage = null;
            }
            else
            {
                continue;
            }
            await db.SaveChangesAsync(ct);
            await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
        }

        if (problem is not null)
        {
            logger.LogWarning("{Problem}", problem);
        }
        return problem is not null;
    }

    private async Task CompleteAsync(BookRequest request, AppSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        request.Status = BookRequestStatus.Ready;
        request.ReadyAt = now;
        request.DiagnosticMessage = null;
        await db.SaveChangesAsync(ct);
        await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);
        await externalNotifier.NotifyAsync(settings, ExternalNotifier.Ready(request), ct);

        if (!string.IsNullOrWhiteSpace(settings.JellyfinUrl) && !string.IsNullOrWhiteSpace(settings.JellyfinApiKey))
        {
            try
            {
                await jellyfin.RefreshLibraryAsync(settings.JellyfinUrl, settings.JellyfinApiKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Jellyfin library refresh failed after processing request {RequestId}", request.Id);
            }
        }
    }

    /// <summary>
    /// EnableMoving and EnableConversion both false: Chaptarr is left to organize/name the final file
    /// itself, so there's nothing for this app to copy, tag, or rename. Just wait for the file(s) Chaptarr reported to
    /// actually exist on disk (its history can report an import slightly before the file is fully
    /// settled), then mark Ready and trigger Jellyfin. No ProcessedBook row is created -- there's no
    /// copy of our own to clean up later, and OriginalCleanupBackgroundService must never touch these
    /// paths since they're Chaptarr's only copy of the file.
    /// </summary>
    private async Task ProcessOneWithoutMovingAsync(BookRequest request, AppSettings settings, CancellationToken ct)
    {
        if (request.ChaptarrBookId is null)
        {
            logger.LogWarning("Request {RequestId} is Processing but has no ChaptarrBookId; skipping", request.Id);
            return;
        }

        var files = await chaptarr.GetBookFilesAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, request.ChaptarrBookId.Value, ct);
        if (files.Count == 0)
        {
            return;
        }

        var localPaths = files
            .Select(f => RemotePathMapper.ToLocalPath(f.Path, settings.ChaptarrFilePathPrefix, settings.LocalFilePathPrefix))
            .ToList();
        if (!localPaths.All(File.Exists))
        {
            // Chaptarr's history says imported but the file isn't visible here yet; retry next tick.
            return;
        }

        await CompleteAsync(request, settings, DateTimeOffset.UtcNow, ct);
    }

    private async Task ProcessOneAsync(BookRequest request, AppSettings settings, CancellationToken ct)
    {
        if (request.ChaptarrBookId is null)
        {
            logger.LogWarning("Request {RequestId} is Processing but has no ChaptarrBookId; skipping", request.Id);
            return;
        }

        var files = await chaptarr.GetBookFilesAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, request.ChaptarrBookId.Value, ct);
        if (files.Count == 0)
        {
            // Chaptarr's history reported an import, but the bookfile API hasn't caught up yet. Retry next tick.
            return;
        }

        var stagingDir = Path.Combine(settings.StagingPath!, $"request-{request.Id}");
        Directory.CreateDirectory(stagingDir);

        try
        {
            var stagedFiles = new List<string>();
            foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = RemotePathMapper.ToLocalPath(file.Path, settings.ChaptarrFilePathPrefix, settings.LocalFilePathPrefix);
                var dest = Path.Combine(stagingDir, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, dest, overwrite: true);
                stagedFiles.Add(dest);
            }

            var coverPath = settings.EnableConversion
                ? await coverArtDownloader.DownloadAsync(request.CoverUrl, stagingDir, ct)
                : null;

            var patternPath = NamingPatternService.BuildRelativePath(settings.NamingPattern, request.Author, request.Title, request.Series);
            var relativePath = settings.EnableRenaming
                ? patternPath
                : Path.Combine(Path.GetDirectoryName(patternPath) ?? "", Path.GetFileNameWithoutExtension(stagedFiles[0]));
            var finalPath = Path.Combine(settings.LibraryPath!, relativePath + ".m4b");
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            var combinedOutput = Path.Combine(stagingDir, "output.m4b");
            var tags = new BookTags(request.Title, request.Author, request.Series);
            await audioCombiner.CombineAsync(stagedFiles, combinedOutput, tags, coverPath, settings.EnableConversion, ct);

            File.Move(combinedOutput, finalPath, overwrite: true);

            var now = DateTimeOffset.UtcNow;
            db.ProcessedBooks.Add(new ProcessedBook
            {
                BookRequestId = request.Id,
                FinalPath = finalPath,
                OriginalSourcePathsJson = JsonSerializer.Serialize(files.Select(f => f.Path)),
                CopiedAt = now,
                ProcessedAt = now,
                OriginalCleanupAt = now.AddDays(settings.OriginalRetentionDays),
                // Not moving means Chaptarr's files are left alone for good; pre-marking them deleted keeps
                // OriginalCleanupBackgroundService from ever removing them.
                OriginalDeleted = !settings.EnableMoving
            });

            await CompleteAsync(request, settings, now, ct);
        }
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
