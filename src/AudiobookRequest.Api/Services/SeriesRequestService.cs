using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Core.Recommendations;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record SeriesExpansionResult(IReadOnlyList<BookRequestDto> Created, int SkippedByLimit);

public record SeriesMemberStatus(string Title, int Number, string State);

/// <param name="Members">Every single-book entry in the series, in order, each "owned", "requested" or "missing".</param>
public record SeriesStatus(string SeriesTitle, int Total, int Owned, int Requested, IReadOnlyList<SeriesMemberStatus> Members);

/// <summary>
/// Knows what's in a series (from Audible's catalog) and how much of it you already have or have asked for.
/// Also expands a bundle/omnibus (e.g. "The Darkwood Trilogy") into requests for its individual books.
/// Chaptarr's own search has no series data, so this leans on Audible's catalog search (same source as
/// genre discovery) to find the other books carrying the same series ASIN, keeping only ones with a
/// plain single-book sequence ("1", "2", "3" -- not "1-3"), then cross-resolves each against Chaptarr
/// the same way Browse search does, so only books your indexers can actually find get requested.
/// </summary>
public class SeriesRequestService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    IAudiobookMetadataClient metadataClient,
    LibraryCatalogService libraryCatalog,
    IExternalNotifier externalNotifier,
    INotificationSender notifications,
    ILogger<SeriesRequestService> logger)
{
    public async Task<SeriesStatus?> GetStatusAsync(string seriesTitle, string seriesAsin, string authorName, CancellationToken ct)
    {
        var members = await LoadMembersAsync(seriesTitle, seriesAsin, authorName, ct);
        if (members.Count == 0)
        {
            return null;
        }

        var library = await libraryCatalog.GetIndexAsync(ct);
        var requested = await db.BookRequests
            .Where(r => r.Status != BookRequestStatus.Denied && r.Status != BookRequestStatus.Failed)
            .Select(r => new { r.Title, r.Author })
            .ToListAsync(ct);

        var statuses = members.Select(m =>
        {
            var number = AudiobookSeries.ParseSingleBookNumber(m.SeriesSequence) ?? 0;
            var state = library.IsOwned(m.Title, authorName) || library.FindSimilar(m.Title, authorName) is not null
                ? "owned"
                : requested.Any(r => TitleMatcher.IsSimilarTitle(r.Title, m.Title) || TitleMatcher.NormalizeTitle(r.Title) == TitleMatcher.NormalizeTitle(m.Title))
                    ? "requested"
                    : "missing";
            return new SeriesMemberStatus(m.Title, number, state);
        }).ToList();

        return new SeriesStatus(
            seriesTitle, statuses.Count,
            statuses.Count(s => s.State == "owned"),
            statuses.Count(s => s.State == "requested"),
            statuses);
    }

    public async Task<SeriesExpansionResult> ExpandAndRequestSeriesAsync(
        string seriesTitle, string seriesAsin, string authorName, int requestedByUserId, RequestPolicy policy, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
        {
            return new SeriesExpansionResult([], 0);
        }

        var members = await LoadMembersAsync(seriesTitle, seriesAsin, authorName, ct);
        if (members.Count == 0)
        {
            return new SeriesExpansionResult([], 0);
        }

        var alreadyRequestedIds = await db.BookRequests.Select(r => r.ChaptarrForeignBookId).ToListAsync(ct);
        var alreadyRequestedSet = new HashSet<string>(alreadyRequestedIds, StringComparer.OrdinalIgnoreCase);
        var library = await libraryCatalog.GetIndexAsync(ct);
        var ownedTitles = library.ExactTitles;
        var existing = await db.BookRequests.Select(r => r.Title).ToListAsync(ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = new List<BookRequestDto>();
        var skippedByLimit = 0;
        var remaining = policy.Remaining;

        foreach (var member in members)
        {
            // Cheap checks first, before spending a Chaptarr lookup on a book we already have or asked for.
            if (library.IsOwned(member.Title, authorName) || library.FindSimilar(member.Title, authorName) is not null
                || existing.Any(t => TitleMatcher.NormalizeTitle(t) == TitleMatcher.NormalizeTitle(member.Title)))
            {
                continue;
            }

            IReadOnlyList<ChaptarrBookLookupResult> matches;
            try
            {
                matches = await chaptarr.LookupBooksAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, member.Title, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Series expansion Chaptarr lookup failed for '{Title}'", member.Title);
                continue;
            }

            var match = matches.FirstOrDefault(m => m.AuthorName.Contains(authorName, StringComparison.OrdinalIgnoreCase))
                ?? matches.FirstOrDefault();
            if (match is null)
            {
                continue;
            }

            var filtered = RecommendationScorer.FilterCandidates([match], seen, alreadyRequestedSet, listenedTitles: ownedTitles);
            if (filtered.Count == 0)
            {
                continue;
            }

            if (remaining <= 0)
            {
                skippedByLimit++;
                continue;
            }
            remaining--;

            var bookRequest = new BookRequest
            {
                RequestedByUserId = requestedByUserId,
                Title = match.Title,
                Author = match.AuthorName,
                Series = seriesTitle,
                CoverUrl = match.CoverUrl,
                ChaptarrForeignBookId = match.ForeignBookId,
                ReleaseDate = match.ReleaseDate,
                Status = policy.StatusFor(match.ReleaseDate)
            };
            db.BookRequests.Add(bookRequest);
            await db.SaveChangesAsync(ct);
            await db.Entry(bookRequest).Reference(r => r.RequestedByUser).LoadAsync(ct);

            var dto = BookRequestDto.From(bookRequest);
            await notifications.RequestUpdatedAsync(dto, ct);
            if (bookRequest.Status == BookRequestStatus.PendingApproval)
            {
                await externalNotifier.NotifyAsync(settings, ExternalNotifier.NeedsApproval(bookRequest), ct);
            }
            created.Add(dto);
        }

        return new SeriesExpansionResult(created, skippedByLimit);
    }

    private async Task<List<AudiobookCatalogEntry>> LoadMembersAsync(string seriesTitle, string seriesAsin, string authorName, CancellationToken ct)
    {
        var candidates = await metadataClient.GetSeriesBooksAsync(seriesTitle, authorName, ct);
        return candidates
            .Where(c => c.SeriesAsin == seriesAsin && AudiobookSeries.IsSingleBook(c.SeriesSequence))
            .GroupBy(c => c.SeriesSequence)
            .Select(g => g.First())
            .OrderBy(c => AudiobookSeries.ParseSingleBookNumber(c.SeriesSequence))
            .ToList();
    }
}
