using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Recommendations;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record RecommendationDto(
    string ForeignBookId,
    string Title,
    string AuthorName,
    string? SeriesTitle,
    string? CoverUrl,
    string Reason,
    string? Overview,
    DateTimeOffset? ReleaseDate,
    double? RatingValue,
    int? RatingVotes);

/// <summary>
/// Rule-based "picked for you": ranks a user's cached Jellyfin listening history by author (see
/// RecommendationScorer), then reuses the same Chaptarr lookup the search box uses, skipping anything
/// already owned, listened to or requested. Category browsing lives in CatalogBrowseService.
/// </summary>
public class RecommendationService(
    AppDbContext db,
    SettingsService settingsService,
    IChaptarrClient chaptarr,
    LibraryCatalogService libraryCatalog,
    ILogger<RecommendationService> logger)
{
    private const int TopAuthorCount = 5;
    private const int MaxResults = 20;

    public async Task<IReadOnlyList<RecommendationDto>> GetRecommendationsAsync(int userId, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
        {
            return [];
        }

        var history = await db.ListeningHistory.Where(h => h.AppUserId == userId).ToListAsync(ct);
        if (history.Count == 0)
        {
            return [];
        }

        var topAuthors = RecommendationScorer.SelectTopAuthors(history, TopAuthorCount);
        if (topAuthors.Count == 0)
        {
            return [];
        }

        var (alreadyRequestedSet, listenedTitles) = await LoadExclusionsAsync(history, ct);
        var library = await libraryCatalog.GetIndexAsync(ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<RecommendationDto>();

        foreach (var author in topAuthors)
        {
            IReadOnlyList<ChaptarrBookLookupResult> matches;
            try
            {
                matches = await chaptarr.LookupBooksAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, author.Author, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Recommendation lookup failed for author {Author}", author.Author);
                continue;
            }

            var filtered = RecommendationScorer.FilterCandidates(matches, seen, alreadyRequestedSet, listenedTitles);
            foreach (var match in filtered.Where(m => !IsInLibrary(library, m)))
            {
                results.Add(ToDto(match, $"Because you listened to {author.Author}"));
                if (results.Count >= MaxResults)
                {
                    return results;
                }
            }
        }

        return results;
    }

    private async Task<(HashSet<string> AlreadyRequested, HashSet<string> ExcludedTitles)> LoadExclusionsAsync(
        List<Core.Entities.ListeningHistoryCache> history, CancellationToken ct)
    {
        var alreadyRequestedIds = await db.BookRequests.Select(r => r.ChaptarrForeignBookId).ToListAsync(ct);
        // Don't recommend something already listened to, or already sitting in the library under a
        // different (e.g. not-yet-requested-through-us) entry.
        var excludedTitles = await libraryCatalog.GetOwnedTitlesAsync(ct);
        excludedTitles.UnionWith(history.Select(h => h.Title));
        return (new HashSet<string>(alreadyRequestedIds, StringComparer.OrdinalIgnoreCase), excludedTitles);
    }

    // Catches different editions of something already owned ("Mistborn: The Final Empire" vs "The Final Empire").
    private static bool IsInLibrary(Core.Library.LibraryIndex library, ChaptarrBookLookupResult match) =>
        library.IsOwned(match.Title, match.AuthorName) || library.FindSimilar(match.Title, match.AuthorName) is not null;

    private static RecommendationDto ToDto(ChaptarrBookLookupResult match, string reason) => new(
        match.ForeignBookId, match.Title, match.AuthorName, match.SeriesTitle, match.CoverUrl, reason,
        match.Overview, match.ReleaseDate, match.RatingValue, match.RatingVotes);
}
