using System.Text.Json;
using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Import;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record ImportFolderDto(string? Folder, string? LibraryPath, string NamingPattern, string? Problem);

public record ImportCandidateDto(string Id, string Kind, string Name, string RelativePath, int FileCount, long TotalBytes, bool Imported, string? ImportedAs);

public record ImportScanDto(string? Folder, IReadOnlyList<ImportCandidateDto> Candidates);

public record ImportFileDto(string Name, long Bytes, double Seconds, string? Codec, string? Problem);

public record ImportMatchDto(
    string Asin,
    string Title,
    string Author,
    string? Series,
    string? SeriesNumber,
    string? CoverUrl,
    int? RuntimeMinutes,
    IReadOnlyList<string> Narrators,
    double Score,
    string? LengthNote);

public record ImportIdentifyRequest(string? Query, string? Author);

public record ImportIdentifyDto(
    string Id,
    string Name,
    IReadOnlyList<ImportFileDto> Files,
    double TotalSeconds,
    string GuessTitle,
    string? GuessAuthor,
    string Query,
    IReadOnlyList<ImportMatchDto> Matches,
    IReadOnlyList<string> Problems);

public record ImportPreviewDto(string RelativePath, string FullPath, bool FileExists, bool AlreadyInJellyfin);

/// <param name="SourceAction">"keep" leaves the original files where they are; "move" moves them into an "_imported" folder inside the import folder.</param>
public record ImportRequest(string Title, string Author, string? Series, string? CoverUrl, string SourceAction, bool Overwrite);

public record ImportResultDto(string FinalPath, bool MovedOriginals, string? Note);

/// <summary>Something wrong with the request that the admin can fix: becomes a plain error message with this status.</summary>
public class ImportException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// The admin's tool for gathering loose audiobooks: finds them in a chosen folder, helps identify each one, then
/// converts it to a tagged .m4b with its cover, files it in the library under the naming pattern, and tells Jellyfin.
/// The original files are never deleted -- at most they are moved into an "_imported" folder.
/// </summary>
public class ImportService(
    AppDbContext db,
    SettingsService settingsService,
    IAudioProbe probe,
    IAudiobookMetadataClient metadata,
    ICoverArtDownloader coverArt,
    IAudioCombiner combiner,
    IJellyfinClient jellyfin,
    LibraryCatalogService library,
    ILogger<ImportService> logger)
{
    private const int MaxMatches = 8;

    // ---- the folder ----

    public async Task<ImportFolderDto> GetFolderAsync(CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        string? problem = null;
        if (string.IsNullOrWhiteSpace(s.LibraryPath) || string.IsNullOrWhiteSpace(s.StagingPath))
        {
            problem = "Finish setup first: the import tool files books into your library folder and needs a staging folder.";
        }
        return new ImportFolderDto(s.ImportFolderPath, s.LibraryPath, s.NamingPattern, problem);
    }

    public async Task<ImportFolderDto> SetFolderAsync(string? path, CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        var cleaned = CleanPath(path);
        if (cleaned is null)
        {
            await settingsService.UpdateAsync(x => x.ImportFolderPath = null, ct);
            return await GetFolderAsync(ct);
        }

        var full = Path.GetFullPath(cleaned);
        if (!Directory.Exists(full))
        {
            throw new ImportException(400, $"The folder '{full}' doesn't exist on this server. Use a path as this computer sees it, such as D:\\Audiobooks\\ToSort or \\\\nas\\share\\books.");
        }
        EnsureNotLibrary(full, s);

        await settingsService.UpdateAsync(x => x.ImportFolderPath = full, ct);
        return await GetFolderAsync(ct);
    }

    /// <summary>Strips the quotes Windows adds when copying a path as text, and whitespace. Null if nothing is left.</summary>
    public static string? CleanPath(string? path)
    {
        var t = path?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>The import folder must be separate from the library and staging: scanning the library would offer finished books for re-import.</summary>
    public static void EnsureNotLibrary(string importFolder, AppSettings s)
    {
        foreach (var (label, other) in new[] { ("library", s.LibraryPath), ("staging", s.StagingPath) })
        {
            if (string.IsNullOrWhiteSpace(other))
            {
                continue;
            }
            if (IsSameOrInside(importFolder, other) || IsSameOrInside(other, importFolder))
            {
                throw new ImportException(400, $"The import folder can't be the {label} folder, or contain it or sit inside it. Choose a separate folder for the books you want to sort.");
            }
        }
    }

    private static bool IsSameOrInside(string child, string parent)
    {
        var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(AppSettings Settings, string Root)> RequireFolderAsync(CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        var root = CleanPath(s.ImportFolderPath) ?? throw new ImportException(400, "Choose the folder to import from first.");
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
        {
            throw new ImportException(400, $"The import folder '{root}' isn't there any more.");
        }
        EnsureNotLibrary(root, s);
        return (s, root);
    }

    // ---- finding books ----

    public async Task<ImportScanDto> ScanAsync(CancellationToken ct)
    {
        var (_, root) = await RequireFolderAsync(ct);
        var candidates = ImportScanner.Scan(root);
        var done = await db.ImportedSources.AsNoTracking().ToDictionaryAsync(i => i.SourceKey, i => i.Title, ct);

        var dtos = candidates.Select(c =>
        {
            var key = c.SourcePath(root).ToLowerInvariant();
            var imported = done.TryGetValue(key, out var title);
            return new ImportCandidateDto(c.Id, c.Kind.ToString(), c.DisplayName, c.RelativePath, c.Files.Count, c.TotalBytes, imported, imported ? title : null);
        }).ToList();
        return new ImportScanDto(root, dtos);
    }

    private async Task<(AppSettings Settings, string Root, ImportCandidate Candidate)> FindAsync(string id, CancellationToken ct)
    {
        var (s, root) = await RequireFolderAsync(ct);
        var candidate = ImportScanner.Scan(root).FirstOrDefault(c => c.Id == id)
                        ?? throw new ImportException(404, "That book isn't in the folder any more. It may have been moved or renamed; scan again.");
        return (s, root, candidate);
    }

    // ---- identifying one ----

    public async Task<ImportIdentifyDto> IdentifyAsync(string id, ImportIdentifyRequest req, CancellationToken ct)
    {
        var (_, _, candidate) = await FindAsync(id, ct);

        var files = new List<ImportFileDto>();
        var infos = new List<AudioFileInfo>();
        var problems = new List<string>();
        foreach (var file in candidate.Files)
        {
            long bytes = 0;
            try { bytes = new FileInfo(file).Length; } catch (IOException) { /* reported below if unreadable */ }
            try
            {
                var info = await probe.ProbeAsync(file, ct);
                infos.Add(info);
                files.Add(new ImportFileDto(Path.GetFileName(file), bytes, info.DurationSeconds, info.Codec, null));
            }
            catch (InvalidOperationException ex)
            {
                files.Add(new ImportFileDto(Path.GetFileName(file), bytes, 0, null, ex.Message));
                problems.Add(ex.Message);
            }
        }

        var totalSeconds = infos.Sum(i => i.DurationSeconds);
        var guess = ImportGuessBuilder.Build(candidate.RelativePath, candidate.Kind == CandidateKind.Folder, infos);

        var title = string.IsNullOrWhiteSpace(req.Query) ? guess.Title : req.Query.Trim();
        var author = req.Author is null ? guess.Author : (string.IsNullOrWhiteSpace(req.Author) ? null : req.Author.Trim());

        var listings = await SearchAsync(title, author, ct);
        var ranked = ImportMatchRanker.Rank(listings, title, author, totalSeconds)
            .Take(MaxMatches)
            .Select(m => new ImportMatchDto(
                m.Listing.Asin, m.Listing.Title, m.Listing.Author, m.Listing.SeriesTitle, m.Listing.SeriesSequence,
                m.Listing.CoverUrl, m.Listing.RuntimeMinutes, m.Listing.Narrators, m.Score, m.LengthNote))
            .ToList();

        return new ImportIdentifyDto(candidate.Id, candidate.DisplayName, files, totalSeconds, guess.Title, guess.Author, title, ranked, problems);
    }

    // Audible's title search finds the exact book; a keyword search then fills any gap (a title with odd punctuation, say).
    // If the author guess is wrong it finds nothing, so look again without it.
    private async Task<IReadOnlyList<AudiobookListing>> SearchAsync(string title, string? author, CancellationToken ct)
    {
        var found = await SearchOnceAsync(title, author, ct);
        if (found.Count == 0 && !string.IsNullOrWhiteSpace(author))
        {
            found = await SearchOnceAsync(title, null, ct);
        }
        return found;
    }

    private async Task<IReadOnlyList<AudiobookListing>> SearchOnceAsync(string title, string? author, CancellationToken ct)
    {
        var byTitle = (await metadata.SearchAsync(title, author, 30, ct)).Items.ToList();
        if (byTitle.Count >= 3)
        {
            return byTitle;
        }

        var byKeyword = (await metadata.BrowseAsync(null, title, "Relevance", 0, 40, author, ct)).Items;
        return byTitle.Concat(byKeyword.Where(k => byTitle.All(t => t.Asin != k.Asin))).ToList();
    }

    // ---- where it will go ----

    public async Task<ImportPreviewDto> PreviewAsync(string title, string author, string? series, CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(s.LibraryPath))
        {
            throw new ImportException(400, "Finish setup first: there's no library folder to file books into.");
        }

        var (relative, full) = FinalPath(s, title, author, series);
        var index = await library.GetIndexAsync(ct);
        return new ImportPreviewDto(relative, full, File.Exists(full), index.IsOwned(title, author) || index.FindSimilar(title, author) is not null);
    }

    /// <summary>The file's place in the library under the naming pattern, always inside the library folder.</summary>
    public static (string Relative, string Full) FinalPath(AppSettings s, string title, string author, string? series)
    {
        var pattern = NamingPatternService.BuildRelativePath(s.NamingPattern, author, title, series);
        var relative = pattern + ".m4b";
        var root = Path.GetFullPath(s.LibraryPath!);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsSameOrInside(full, root) || full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ImportException(400, "That title and author would put the file outside the library folder.");
        }
        return (relative, full);
    }

    // ---- doing it ----

    public async Task<ImportResultDto> ImportAsync(string id, ImportRequest req, Action<string> stage, CancellationToken ct)
    {
        stage("Checking the files");
        var (s, root, candidate) = await FindAsync(id, ct);

        var title = req.Title?.Trim() ?? "";
        var author = req.Author?.Trim() ?? "";
        var series = string.IsNullOrWhiteSpace(req.Series) ? null : req.Series.Trim();
        if (title.Length == 0 || author.Length == 0 || title.Length > 200 || author.Length > 200)
        {
            throw new ImportException(400, "Both a title and an author are needed (up to 200 characters each).");
        }
        if (req.SourceAction is not ("keep" or "move"))
        {
            throw new ImportException(400, "Choose whether to keep the original files or move them aside.");
        }
        if (string.IsNullOrWhiteSpace(s.LibraryPath) || string.IsNullOrWhiteSpace(s.StagingPath))
        {
            throw new ImportException(400, "Finish setup first: the library and staging folders are needed.");
        }

        var sourceKey = candidate.SourcePath(root).ToLowerInvariant();
        if (!req.Overwrite && await db.ImportedSources.AnyAsync(i => i.SourceKey == sourceKey, ct))
        {
            throw new ImportException(409, "This book was already imported. Tick 'import again' to do it a second time.");
        }

        var (_, finalPath) = FinalPath(s, title, author, series);
        if (File.Exists(finalPath) && !req.Overwrite)
        {
            throw new ImportException(409, $"A book already exists at '{finalPath}'. Tick 'replace it' to overwrite, or change the title or author.");
        }

        CheckDiskSpace(s, candidate);

        // Every file must be readable audio before anything is converted.
        foreach (var file in candidate.Files)
        {
            try
            {
                await probe.ProbeAsync(file, ct);
            }
            catch (InvalidOperationException ex)
            {
                throw new ImportException(422, ex.Message);
            }
        }

        var stagingDir = Path.Combine(s.StagingPath, "import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        try
        {
            stage("Downloading the cover");
            var cover = await coverArt.DownloadAsync(req.CoverUrl, stagingDir, ct);

            stage(candidate.Files.Count > 1
                ? $"Joining {candidate.Files.Count} files into one audiobook (long books take several minutes)"
                : "Converting to an audiobook file (long books take several minutes)");
            var output = Path.Combine(stagingDir, "output.m4b");
            await combiner.CombineAsync(candidate.Files, output, new BookTags(title, author, series), cover, embedTags: true, ct, requireAac: true);

            stage("Filing it in your library");
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(output, finalPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch { /* best-effort scratch cleanup */ }
        }

        var now = DateTimeOffset.UtcNow;
        db.ProcessedBooks.Add(new ProcessedBook
        {
            BookRequestId = null,
            FinalPath = finalPath,
            OriginalSourcePathsJson = JsonSerializer.Serialize(candidate.Files),
            CopiedAt = now,
            ProcessedAt = now,
            OriginalCleanupAt = now,
            // The originals are the admin's own files: the automatic originals cleanup must never touch them.
            OriginalDeleted = true,
        });
        var existing = await db.ImportedSources.FirstOrDefaultAsync(i => i.SourceKey == sourceKey, ct);
        if (existing is null)
        {
            db.ImportedSources.Add(new ImportedSource { SourceKey = sourceKey, SourcePath = candidate.SourcePath(root), FinalPath = finalPath, Title = title, Author = author, ImportedAt = now });
        }
        else
        {
            existing.FinalPath = finalPath;
            existing.Title = title;
            existing.Author = author;
            existing.ImportedAt = now;
        }
        await db.SaveChangesAsync(ct);

        string? note = null;
        var moved = false;
        if (req.SourceAction == "move")
        {
            try
            {
                MoveOriginalsAside(root, candidate);
                moved = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Imported '{Title}' but couldn't move its original files aside", title);
                note = $"The book was imported, but the original files couldn't be moved aside ({ex.Message}). They're still where they were.";
            }
        }

        stage("Telling Jellyfin");
        if (!string.IsNullOrWhiteSpace(s.JellyfinUrl) && !string.IsNullOrWhiteSpace(s.JellyfinApiKey))
        {
            try
            {
                await jellyfin.RefreshLibraryAsync(s.JellyfinUrl, s.JellyfinApiKey, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Imported '{Title}' but Jellyfin's library refresh failed", title);
                note ??= "The book was imported, but Jellyfin couldn't be told to rescan. It will show up at its next scheduled scan.";
            }
        }

        return new ImportResultDto(finalPath, moved, note);
    }

    private static void CheckDiskSpace(AppSettings s, ImportCandidate candidate)
    {
        // Converting writes roughly as much as the sources hold, first to staging and then onto the library drive.
        var needed = (long)(candidate.TotalBytes * 1.1) + s.MinFreeDiskGb * 1024L * 1024 * 1024;
        foreach (var path in new[] { s.StagingPath, s.LibraryPath }.Distinct())
        {
            var free = DiskSpace.GetFreeBytes(path);
            if (free is not null && free < needed)
            {
                throw new ImportException(507, $"Not enough free space on '{path}': this book needs about {needed / 1024.0 / 1024 / 1024:0.0} GB (including the {s.MinFreeDiskGb} GB reserve) and {free.Value / 1024.0 / 1024 / 1024:0.0} GB is free.");
            }
        }
    }

    /// <summary>Moves the book's audio files into "_imported" beside where they were, keeping the folder layout, so the folder shows only what's left to do. Nothing is deleted.</summary>
    public static void MoveOriginalsAside(string root, ImportCandidate candidate)
    {
        foreach (var file in candidate.Files)
        {
            var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(root, file)) ?? "";
            var targetDir = Path.Combine(root, ImportScanner.ImportedFolderName, relativeDir);
            Directory.CreateDirectory(targetDir);

            var target = Path.Combine(targetDir, Path.GetFileName(file));
            for (var n = 2; File.Exists(target); n++)
            {
                target = Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(file)} ({n}){Path.GetExtension(file)}");
            }
            File.Move(file, target);
        }

        // Tidy up folders the move left empty -- only empty ones, and never the import folder itself.
        foreach (var dir in candidate.Files.Select(Path.GetDirectoryName).Distinct().OfType<string>().OrderByDescending(d => d.Length))
        {
            for (var d = dir; !d.Equals(root, StringComparison.OrdinalIgnoreCase) && IsSameOrInside(d, root); d = Path.GetDirectoryName(d)!)
            {
                if (Directory.EnumerateFileSystemEntries(d).Any())
                {
                    break;
                }
                Directory.Delete(d);
            }
        }
    }
}
