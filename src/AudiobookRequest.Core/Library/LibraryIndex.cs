namespace AudiobookRequest.Core.Library;

public record LibraryEntry(string Title, string? Artists);

/// <summary>
/// In-memory snapshot of what Jellyfin already has, answering "do I own this?" (blocks a re-request) and
/// "is there something similar?" (warns, e.g. a different edition) without a database round-trip per book.
/// </summary>
public class LibraryIndex
{
    private readonly List<LibraryEntry> entries;
    private readonly HashSet<string> exactTitles;

    public LibraryIndex(IEnumerable<LibraryEntry> entries)
    {
        this.entries = entries.ToList();
        exactTitles = new HashSet<string>(this.entries.Select(e => e.Title), StringComparer.OrdinalIgnoreCase);
    }

    public int Count => entries.Count;

    /// <summary>Exact titles, case-insensitive -- the set the recommendation filters work against.</summary>
    public HashSet<string> ExactTitles => new(exactTitles, StringComparer.OrdinalIgnoreCase);

    /// <summary>An exact title hit, or the same normalized title with a compatible author.</summary>
    public bool IsOwned(string title, string? author)
    {
        if (exactTitles.Contains(title))
        {
            return true;
        }
        var normalized = TitleMatcher.NormalizeTitle(title);
        if (normalized.Length == 0)
        {
            return false;
        }
        return entries.Any(e => (TitleMatcher.NormalizeTitle(e.Title) == normalized || TitleMatcher.IsSameSeriesEntry(e.Title, title)) && AuthorCompatible(e, author));
    }

    /// <summary>The library title of a likely different edition of the same book, or null.</summary>
    public string? FindSimilar(string title, string? author)
    {
        if (IsOwned(title, author))
        {
            return null;
        }
        return entries.FirstOrDefault(e => TitleMatcher.IsSimilarTitle(e.Title, title) && AuthorCompatible(e, author))?.Title;
    }

    // Jellyfin's artist list is unreliable (authors and narrators mixed), so any artist matching counts;
    // when either side has no author information the title alone decides.
    private static bool AuthorCompatible(LibraryEntry entry, string? author)
    {
        if (string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(entry.Artists))
        {
            return true;
        }
        return entry.Artists.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Any(a => TitleMatcher.AuthorsMatch(a, author));
    }
}
