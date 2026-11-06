using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Recommendations;

namespace AudiobookRequest.Core.Tests;

public class RecommendationScorerTests
{
    [Fact]
    public void SelectTopAuthors_RanksByPlayCountThenRecency_AndIgnoresBlankAuthors()
    {
        var history = new List<ListeningHistoryCache>
        {
            Item("Andy Weir", playCount: 2, lastPlayed: 10),
            Item("Andy Weir", playCount: 1, lastPlayed: 5),
            Item("Brandon Sanderson", playCount: 5, lastPlayed: 1),
            Item(null, playCount: 9, lastPlayed: 20),
        };

        var top = RecommendationScorer.SelectTopAuthors(history, take: 5);

        Assert.Equal(2, top.Count);
        Assert.Equal("Brandon Sanderson", top[0].Author);
        Assert.Equal(5, top[0].PlayCount);
        Assert.Equal("Andy Weir", top[1].Author);
        Assert.Equal(3, top[1].PlayCount);
    }

    [Fact]
    public void SelectTopAuthors_RespectsTakeLimit()
    {
        var history = Enumerable.Range(0, 10)
            .Select(i => Item($"Author {i}", playCount: i, lastPlayed: i))
            .ToList();

        var top = RecommendationScorer.SelectTopAuthors(history, take: 3);

        Assert.Equal(3, top.Count);
        Assert.Equal("Author 9", top[0].Author);
    }

    [Fact]
    public void FilterCandidates_ExcludesAlreadyRequestedAndAlreadyListenedAndCrossAuthorDuplicates()
    {
        var matches = new[]
        {
            Book("gr:1", "Project Hail Mary"),
            Book("gr:2", "Artemis"),
            Book("gr:3", "The Martian"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gr:3" }; // already surfaced by another author
        var alreadyRequested = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gr:1" };
        var listened = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Artemis" };

        var filtered = RecommendationScorer.FilterCandidates(matches, seen, alreadyRequested, listened);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterCandidates_KeepsNewUnrequestedUnlistenedBooks_AndMarksThemSeen()
    {
        var matches = new[] { Book("gr:1", "Project Hail Mary") };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var filtered = RecommendationScorer.FilterCandidates(
            matches, seen,
            alreadyRequestedForeignBookIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            listenedTitles: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Single(filtered);
        Assert.Contains("gr:1", seen);
    }

    private static ListeningHistoryCache Item(string? author, int playCount, int lastPlayed) => new()
    {
        JellyfinItemId = Guid.NewGuid().ToString(),
        Title = "Some Title " + Guid.NewGuid(),
        Author = author,
        PlayCount = playCount,
        LastPlayedDate = DateTimeOffset.UnixEpoch.AddDays(lastPlayed),
    };

    private static ChaptarrBookLookupResult Book(string foreignBookId, string title) => new()
    {
        ForeignBookId = foreignBookId,
        Title = title,
        AuthorName = "Some Author",
    };
}
