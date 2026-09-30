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
    ILogger<BookProcessingService> logger)
{
    public async Task ProcessReadyRequestsAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey)
            || string.IsNullOrWhiteSpace(settings.StagingPath) || string.IsNullOrWhiteSpace(settings.LibraryPath))
        {
            return;
        }

        var pending = await db.BookRequests
            .Where(r => r.Status == BookRequestStatus.Processing)
            .ToListAsync(ct);

        foreach (var request in pending)
        {
            try
            {
                await ProcessOneAsync(request, settings, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process request {RequestId} ({Title})", request.Id, request.Title);
            }
        }
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
                var dest = Path.Combine(stagingDir, Path.GetFileName(file.Path));
                File.Copy(file.Path, dest, overwrite: true);
                stagedFiles.Add(dest);
            }

            var coverPath = await coverArtDownloader.DownloadAsync(request.CoverUrl, stagingDir, ct);

            var relativePath = NamingPatternService.BuildRelativePath(settings.NamingPattern, request.Author, request.Title, request.Series);
            var finalPath = Path.Combine(settings.LibraryPath!, relativePath + ".m4b");
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            var combinedOutput = Path.Combine(stagingDir, "output.m4b");
            var tags = new BookTags(request.Title, request.Author, request.Series);
            await audioCombiner.CombineAsync(stagedFiles, combinedOutput, tags, coverPath, ct);

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
                OriginalDeleted = false
            });

            request.Status = BookRequestStatus.Ready;
            request.ReadyAt = now;
            await db.SaveChangesAsync(ct);
            await notifications.RequestUpdatedAsync(BookRequestDto.From(request), ct);

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
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
