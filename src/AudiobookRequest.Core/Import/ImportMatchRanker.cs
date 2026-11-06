using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Import;

public record RankedMatch(AudiobookListing Listing, double Score, string? LengthNote);

/// <summary>
/// Orders catalog search results by how likely each is to be the book in the folder: a matching title counts most,
/// then the author, then how close the edition's length is to the files'. The length is the best tiebreaker between
/// editions of the same book (abridged vs unabridged, different narrators), so it is shown to the person as well.
/// </summary>
public static class ImportMatchRanker
{
    public static IReadOnlyList<RankedMatch> Rank(IEnumerable<AudiobookListing> listings, string guessTitle, string? guessAuthor, double fileSeconds)
    {
        return listings
            .Select(l => Score(l, guessTitle, guessAuthor, fileSeconds))
            .OrderByDescending(m => m.Score)
            .ToList();
    }

    private static RankedMatch Score(AudiobookListing listing, string guessTitle, string? guessAuthor, double fileSeconds)
    {
        double score = 0;

        if (TitleMatcher.IsSimilarTitle(listing.Title, guessTitle))
        {
            score += 4;
        }
        else
        {
            score += 3 * WordOverlap(listing.Title, guessTitle);
        }

        if (!string.IsNullOrWhiteSpace(guessAuthor))
        {
            var authors = (listing.AllAuthors is { Count: > 0 } ? listing.AllAuthors : [listing.Author]);
            if (authors.Any(a => TitleMatcher.AuthorsMatch(a, guessAuthor)))
            {
                score += 2;
            }
        }

        string? note = null;
        if (fileSeconds > 0 && listing.RuntimeMinutes is > 0)
        {
            var fileMinutes = fileSeconds / 60;
            var gap = Math.Abs(fileMinutes - listing.RuntimeMinutes.Value) / listing.RuntimeMinutes.Value;
            (score, note) = gap switch
            {
                <= 0.02 => (score + 3, $"Same length ({Hours(fileMinutes)} here, {Hours(listing.RuntimeMinutes.Value)} listed)"),
                <= 0.06 => (score + 1.5, $"Close in length ({Hours(fileMinutes)} here, {Hours(listing.RuntimeMinutes.Value)} listed)"),
                > 0.25 => (score - 2, $"Very different length ({Hours(fileMinutes)} here, {Hours(listing.RuntimeMinutes.Value)} listed)"),
                _ => (score, $"{Hours(fileMinutes)} here, {Hours(listing.RuntimeMinutes.Value)} listed"),
            };
        }

        return new RankedMatch(listing, Math.Round(score, 2), note);
    }

    private static double WordOverlap(string a, string b)
    {
        var wa = TitleMatcher.NormalizeTitle(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wb = TitleMatcher.NormalizeTitle(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (wa.Count == 0 || wb.Count == 0)
        {
            return 0;
        }
        return (double)wa.Intersect(wb).Count() / wa.Union(wb).Count();
    }

    public static string Hours(double minutes)
    {
        var total = (int)Math.Round(minutes);
        return total >= 60 ? $"{total / 60}h {total % 60:00}m" : $"{total}m";
    }
}
