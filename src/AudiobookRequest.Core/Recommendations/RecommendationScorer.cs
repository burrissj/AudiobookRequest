using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Core.Recommendations;

public record ScoredAuthor(string Author, int PlayCount, DateTimeOffset LastPlayed);

/// <summary>
/// Pure scoring/filtering logic for recommendations, kept free of EF Core/HTTP so it's directly
/// unit-testable. The IO (reading listening history, calling Chaptarr) lives in Api.Services.RecommendationService.
/// </summary>
public static class RecommendationScorer
{
    public static IReadOnlyList<ScoredAuthor> SelectTopAuthors(IEnumerable<ListeningHistoryCache> history, int take)
    {
        return history
            .Where(h => !string.IsNullOrWhiteSpace(h.Author))
            .GroupBy(h => h.Author!)
            .Select(g => new ScoredAuthor(g.Key, g.Sum(h => h.PlayCount), g.Max(h => h.LastPlayedDate) ?? DateTimeOffset.MinValue))
            .OrderByDescending(a => a.PlayCount)
            .ThenByDescending(a => a.LastPlayed)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Filters one author's lookup results down to ones worth recommending: not already surfaced in
    /// this pass (an author's catalog can overlap another's, e.g. co-authored books), not already
    /// requested, and not something the user has already listened to. Mutates <paramref name="seenForeignBookIds"/>.
    /// </summary>
    public static IReadOnlyList<ChaptarrBookLookupResult> FilterCandidates(
        IEnumerable<ChaptarrBookLookupResult> matches,
        ISet<string> seenForeignBookIds,
        ISet<string> alreadyRequestedForeignBookIds,
        ISet<string> listenedTitles)
    {
        var result = new List<ChaptarrBookLookupResult>();
        foreach (var match in matches)
        {
            if (!seenForeignBookIds.Add(match.ForeignBookId))
            {
                continue;
            }
            if (alreadyRequestedForeignBookIds.Contains(match.ForeignBookId) || listenedTitles.Contains(match.Title))
            {
                continue;
            }
            result.Add(match);
        }
        return result;
    }
}
