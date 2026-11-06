using System.Text.Json.Nodes;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record ImportFixFileDto(string FileName, long SizeBytes, int? MatchedBookId, string? MatchedTitle, IReadOnlyList<string> Rejections);

/// <param name="CanFix">False with a <paramref name="Reason"/> when there is nothing blocked to import for this request.</param>
/// <param name="AutoFixable">Whether the "fix blocked imports automatically" rules would handle this one by themselves.</param>
/// <param name="AutoFixNote">Why or why not, in plain words.</param>
public record ImportFixPreviewDto(
    bool CanFix, string? Reason, string TargetTitle, IReadOnlyList<ImportFixFileDto> Files,
    bool AutoFixable = false, string? AutoFixNote = null);

/// <param name="Outcome">"completed", "started" (still copying when we stopped waiting) or "failed".</param>
public record ImportFixResultDto(string Outcome, string Message);

public record AutoFixDecision(bool Safe, string Reason);

/// <summary>
/// Resolves "Import blocked: ... grabbed for X but import matched Y": Chaptarr downloaded the right files
/// but matched them to a different catalog entry than the one requested, so it refuses to import them.
/// This tells Chaptarr to import those same files as the requested book instead, exactly like picking the
/// book in Chaptarr's own Manual Import screen. Chaptarr copies or hardlinks per its own media-management
/// settings ("auto"), so seeding is unaffected when it is set up for that.
/// </summary>
public class ImportFixService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    ILogger<ImportFixService> logger)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(45);

    private static readonly string[] BlockedStates = ["importBlocked", "importFailed"];

    private record Located(BookRequest Request, AppSettings Settings, string DownloadId, IReadOnlyList<ChaptarrImportCandidate> Candidates);

    /// <returns>null if the request doesn't exist.</returns>
    public async Task<ImportFixPreviewDto?> PreviewAsync(int requestId, CancellationToken ct)
    {
        var request = await db.BookRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            return null;
        }

        var (located, reason) = await LocateAsync(request, ct);
        if (located is null)
        {
            return new ImportFixPreviewDto(false, reason, request.Title, []);
        }

        var files = located.Candidates
            .Select(c => new ImportFixFileDto(Path.GetFileName(c.Path), c.SizeBytes, c.MatchedBookId, c.MatchedBookTitle, c.Rejections))
            .ToList();

        // Dry run of the automatic rules, so an admin can see whether this one would have been handled alone.
        var decision = new AutoFixDecision(false, "couldn't check the requested book in Chaptarr");
        try
        {
            var book = await chaptarr.GetBookInfoAsync(located.Settings.ChaptarrUrl!, located.Settings.ChaptarrApiKey!, request.ChaptarrBookId!.Value, ct);
            if (book is not null)
            {
                decision = Evaluate(located.Candidates, book);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Auto-fix dry run failed for request {RequestId}", requestId);
        }
        return new ImportFixPreviewDto(true, null, request.Title, files, decision.Safe, decision.Reason);
    }

    /// <returns>null if the request doesn't exist.</returns>
    public async Task<ImportFixResultDto?> FixAsync(int requestId, CancellationToken ct)
    {
        var request = await db.BookRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            return null;
        }

        var (located, reason) = await LocateAsync(request, ct);
        if (located is null)
        {
            return new ImportFixResultDto("failed", reason ?? "Nothing to import.");
        }

        var s = located.Settings;
        var targetBookId = request.ChaptarrBookId!.Value;
        var book = await chaptarr.GetBookInfoAsync(s.ChaptarrUrl!, s.ChaptarrApiKey!, targetBookId, ct);
        if (book is null)
        {
            return new ImportFixResultDto("failed", "Chaptarr no longer has the requested book, so there is nothing to import the files as.");
        }

        var editionId = PickEdition(book);
        var files = located.Candidates.Select(c => BuildImportFile(c, book, editionId, located.DownloadId)).ToList();

        int commandId;
        try
        {
            commandId = await chaptarr.StartManualImportAsync(s.ChaptarrUrl!, s.ChaptarrApiKey!, files, "auto", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Manual import call failed for request {RequestId}", requestId);
            return new ImportFixResultDto("failed", $"Chaptarr rejected the import: {ex.Message}");
        }

        logger.LogInformation("Started manual import command {CommandId} for request {RequestId}: {Count} file(s) as book {BookId}",
            commandId, requestId, files.Count, targetBookId);

        return await WaitForCommandAsync(s, commandId, MaxWait, ct);
    }

    /// <summary>
    /// The safety rules for importing without asking: Chaptarr matched every file to one sibling entry (not
    /// some other book), by the same author, whose title is the requested title ignoring "(Part 2 of 2)" /
    /// "[Dramatized Adaptation]" style suffixes, and it listed no other rejection. Anything less is left
    /// for an admin to look at.
    /// </summary>
    public static AutoFixDecision Evaluate(IReadOnlyList<ChaptarrImportCandidate> candidates, ChaptarrBookInfo target)
    {
        if (candidates.Count == 0)
        {
            return new AutoFixDecision(false, "no files to import");
        }
        if (candidates.Any(c => c.Rejections.Count > 0))
        {
            return new AutoFixDecision(false, "Chaptarr listed a rejection other than the book mismatch");
        }

        var matchedIds = candidates.Select(c => c.MatchedBookId).Distinct().ToList();
        if (matchedIds.Count != 1 || matchedIds[0] is null)
        {
            return new AutoFixDecision(false, "the files matched different books");
        }
        if (matchedIds[0] == target.Id)
        {
            return new AutoFixDecision(false, "the files already match the requested book");
        }
        if (candidates.Any(c => c.MatchedAuthorId != target.AuthorId))
        {
            return new AutoFixDecision(false, "the matched book is by a different author");
        }

        var matchedTitle = candidates[0].MatchedBookTitle ?? "";
        return TitleMatcher.IsSimilarTitle(matchedTitle, target.Title)
            ? new AutoFixDecision(true, $"'{matchedTitle}' is the same book as '{target.Title}'")
            : new AutoFixDecision(false, $"'{matchedTitle}' does not look like the same title as '{target.Title}'");
    }

    /// <summary>
    /// Imports a blocked download on its own when <see cref="Evaluate"/> says it is unambiguous. Returns the
    /// note to show on the request, or null when it was left alone. Waits only briefly: the orchestrator
    /// calling this runs every 20 seconds and picks the finished import up on a later pass.
    /// </summary>
    public async Task<string?> TryAutoFixAsync(BookRequest request, AppSettings s, ChaptarrQueueItem item, CancellationToken ct)
    {
        if (request.ChaptarrBookId is null || string.IsNullOrEmpty(item.DownloadId))
        {
            return null;
        }

        try
        {
            var candidates = await chaptarr.GetManualImportCandidatesAsync(s.ChaptarrUrl!, s.ChaptarrApiKey!, item.DownloadId, ct);
            var book = await chaptarr.GetBookInfoAsync(s.ChaptarrUrl!, s.ChaptarrApiKey!, request.ChaptarrBookId.Value, ct);
            if (book is null)
            {
                return null;
            }

            var decision = Evaluate(candidates, book);
            if (!decision.Safe)
            {
                logger.LogInformation("Not auto-fixing request {RequestId}: {Reason}", request.Id, decision.Reason);
                return null;
            }

            var files = candidates.Select(c => BuildImportFile(c, book, PickEdition(book), item.DownloadId)).ToList();
            var commandId = await chaptarr.StartManualImportAsync(s.ChaptarrUrl!, s.ChaptarrApiKey!, files, "auto", ct);
            logger.LogInformation("Auto-fixing request {RequestId}: {Reason}; manual import command {CommandId}", request.Id, decision.Reason, commandId);

            var result = await WaitForCommandAsync(s, commandId, TimeSpan.FromSeconds(8), ct);
            return result.Outcome == "failed"
                ? $"Auto-fix tried to import this but Chaptarr said: {result.Message}"
                : "Auto-fixed: Chaptarr had matched the download to a sibling edition, so it was imported as the requested book.";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Auto-fix failed for request {RequestId}", request.Id);
            return null;
        }
    }

    private async Task<(Located? Located, string? Reason)> LocateAsync(BookRequest request, CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(s.ChaptarrUrl) || string.IsNullOrWhiteSpace(s.ChaptarrApiKey))
        {
            return (null, "Chaptarr isn't configured.");
        }
        if (request.ChaptarrBookId is null)
        {
            return (null, "This request hasn't been added to Chaptarr yet.");
        }
        if (request.Status is BookRequestStatus.Ready or BookRequestStatus.Processing or BookRequestStatus.Denied or BookRequestStatus.PendingApproval)
        {
            return (null, "This request has already been imported or isn't waiting on a download.");
        }

        var queue = await chaptarr.GetQueueAsync(s.ChaptarrUrl, s.ChaptarrApiKey, ct);
        var item = queue.FirstOrDefault(q => q.BookId == request.ChaptarrBookId
                                             && !string.IsNullOrEmpty(q.DownloadId)
                                             && BlockedStates.Contains(q.TrackedDownloadState ?? ""));
        if (item is null)
        {
            return (null, "Chaptarr isn't holding a blocked download for this request.");
        }

        var candidates = await chaptarr.GetManualImportCandidatesAsync(s.ChaptarrUrl, s.ChaptarrApiKey, item.DownloadId!, ct);
        return candidates.Count == 0
            ? (null, "Chaptarr lists no files to import for that download.")
            : (new Located(request, s, item.DownloadId!, candidates), null);
    }

    // Prefer the audiobook edition Chaptarr is monitoring; an ebook edition is never right for this app.
    private static int? PickEdition(ChaptarrBookInfo book) =>
        book.Editions.Where(e => !e.IsEbook).OrderByDescending(e => e.Monitored).Select(e => (int?)e.Id).FirstOrDefault()
        ?? book.Editions.Select(e => (int?)e.Id).FirstOrDefault();

    // Sends Chaptarr's own record back (quality, flags) with the book/author/edition swapped to the requested one.
    internal static JsonObject BuildImportFile(ChaptarrImportCandidate candidate, ChaptarrBookInfo target, int? editionId, string downloadId)
    {
        var file = new JsonObject
        {
            ["path"] = candidate.Path,
            ["authorId"] = target.AuthorId,
            ["bookId"] = target.Id,
            ["downloadId"] = downloadId,
            ["quality"] = candidate.Raw["quality"]?.DeepClone(),
            ["indexerFlags"] = candidate.Raw["indexerFlags"]?.DeepClone() ?? 0,
            ["additionalFile"] = candidate.Raw["additionalFile"]?.DeepClone() ?? false,
            ["replaceExistingFiles"] = candidate.Raw["replaceExistingFiles"]?.DeepClone() ?? false,
            // Stops Chaptarr "helpfully" switching back to the book it matched on its own.
            ["disableReleaseSwitching"] = true,
        };
        if (editionId is not null)
        {
            file["editionId"] = editionId;
        }
        return file;
    }

    private async Task<ImportFixResultDto> WaitForCommandAsync(AppSettings s, int commandId, TimeSpan maxWait, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + maxWait;
        ChaptarrCommandStatus? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                last = await chaptarr.GetCommandStatusAsync(s.ChaptarrUrl!, s.ChaptarrApiKey!, commandId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't read manual import command {CommandId}", commandId);
                break;
            }

            if (last.IsFinished)
            {
                return last.Succeeded
                    ? new ImportFixResultDto("completed", "Chaptarr imported the files. This request will move on in a moment.")
                    : new ImportFixResultDto("failed", $"Chaptarr couldn't import the files: {last.Message ?? last.Result ?? last.Status}");
            }
            await Task.Delay(PollInterval, ct);
        }

        return new ImportFixResultDto("started",
            "Chaptarr is still copying the files. The request will update on its own when it finishes.");
    }
}
