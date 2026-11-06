using System.Text.RegularExpressions;

namespace AudiobookRequest.Core.Library;

/// <summary>
/// Fuzzy title/author helpers for "do I already have this?" -- tolerant of edition noise
/// ("(Unabridged)", "[Graphic Audio]"), punctuation, and a leading "The".
/// </summary>
public static partial class TitleMatcher
{
    [GeneratedRegex(@"\b(unabridged|abridged|dramati[sz]ed|audiobook|graphic audio|full cast|m4b|mp3)\b|\ba novel\s*$")]
    private static partial Regex EditionWordsRegex();

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex BracketedRegex();

    [GeneratedRegex(@"[^a-z0-9 ]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>Lowercased, bracketed text and edition words removed, punctuation dropped, leading article dropped.</summary>
    public static string NormalizeTitle(string title)
    {
        var s = BracketedRegex().Replace(title.ToLowerInvariant().Replace("&", " and "), " ");
        s = EditionWordsRegex().Replace(s.TrimEnd(), " ");
        s = NonAlphanumericRegex().Replace(s, " ");
        s = WhitespaceRegex().Replace(s, " ").Trim();
        foreach (var article in new[] { "the ", "a ", "an " })
        {
            if (s.StartsWith(article, StringComparison.Ordinal))
            {
                return s[article.Length..];
            }
        }
        return s;
    }

    /// <summary>The normalized text after a ":" or " - " subtitle separator, or null when there isn't one.</summary>
    public static string? NormalizedSubtitle(string title)
    {
        var cleaned = BracketedRegex().Replace(title, " ");
        var idx = cleaned.IndexOf(':');
        var length = 1;
        if (idx < 0)
        {
            idx = cleaned.IndexOf(" - ", StringComparison.Ordinal);
            length = 3;
        }
        if (idx < 0)
        {
            return null;
        }
        var sub = NormalizeTitle(cleaned[(idx + length)..]);
        return sub.Length >= 3 ? sub : null;
    }

    /// <summary>
    /// True when the two titles are the same book under different packaging: equal after normalization, or
    /// one's subtitle is the other's whole title ("Mistborn: The Final Empire" vs "The Final Empire").
    /// A shared series prefix alone ("Dune" vs "Dune: Messiah") deliberately does not count.
    /// </summary>
    public static bool IsSimilarTitle(string a, string b)
    {
        var na = NormalizeTitle(a);
        var nb = NormalizeTitle(b);
        if (na.Length == 0 || nb.Length == 0)
        {
            return false;
        }
        if (na == nb)
        {
            return true;
        }
        var sa = NormalizedSubtitle(a);
        var sb = NormalizedSubtitle(b);
        return (sa is not null && sa == nb) || (sb is not null && sb == na) || (sa is not null && sa == sb)
               || IsSameSeriesEntry(a, b);
    }

    /// <summary>The normalized text before the first ":", " - " or bracket: the book's own name without any series tag.</summary>
    public static string NormalizedHead(string title)
    {
        var cut = title.Length;
        foreach (var marker in new[] { ":", " - ", "(", "[" })
        {
            var i = title.IndexOf(marker, StringComparison.Ordinal);
            if (i > 0 && i < cut)
            {
                cut = i;
            }
        }
        return NormalizeTitle(title[..cut]);
    }

    /// <summary>
    /// The same numbered book of the same series written two ways -- "The Dawn of the Cursed Queen (Gods &amp; Monsters, #3)"
    /// and "The Dawn of the Cursed Queen: Gods and Monsters, Book 3". Needs the same series, the same number
    /// and the same book name, so two different books in one series never match.
    /// </summary>
    public static bool IsSameSeriesEntry(string a, string b)
    {
        var pa = SeriesParser.Parse(a);
        var pb = SeriesParser.Parse(b);
        if (pa is null || pb is null || pa.SeriesKey != pb.SeriesKey || pa.Number != pb.Number)
        {
            return false;
        }
        var head = NormalizedHead(a);
        return head.Length > 0 && head == NormalizedHead(b);
    }

    public static string NormalizeAuthor(string author) =>
        WhitespaceRegex().Replace(NonAlphanumericRegex().Replace(author.ToLowerInvariant(), " "), " ").Trim();

    private static readonly HashSet<string> NameSuffixes = ["jr", "sr", "ii", "iii", "iv"];

    /// <summary>
    /// What makes two spellings of an author's name the same author for sorting books into piles: first and last name,
    /// ignoring punctuation, case, middle initials and a "Jr."/"III" -- so "Amber Nicole", "Amber V. Nicole" and
    /// "amber v nicole" are one author, and so are "J.R. Ward" and "J. R. Ward". Different first names stay different.
    /// Empty when the name has no letters or digits.
    /// </summary>
    public static string AuthorGroupKey(string author)
    {
        var tokens = NormalizeAuthor(author).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokens.Count > 1 && NameSuffixes.Contains(tokens[^1]))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }
        if (tokens.Count > 2)
        {
            tokens = [tokens[0], .. tokens[1..^1].Where(t => t.Length > 1), tokens[^1]];
        }
        return string.Join(' ', tokens);
    }

    /// <summary>Same surname and same first initial, so "J. R. R. Tolkien" matches "J.R.R. Tolkien" and "John Smith" matches "J Smith".</summary>
    public static bool AuthorsMatch(string a, string b)
    {
        var ta = NormalizeAuthor(a).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tb = NormalizeAuthor(b).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ta.Length == 0 || tb.Length == 0)
        {
            return false;
        }
        if (string.Join(' ', ta) == string.Join(' ', tb))
        {
            return true;
        }
        return ta[^1] == tb[^1] && ta[0][0] == tb[0][0];
    }
}
