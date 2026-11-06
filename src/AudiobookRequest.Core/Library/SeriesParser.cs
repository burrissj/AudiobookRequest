using System.Globalization;
using System.Text.RegularExpressions;

namespace AudiobookRequest.Core.Library;

/// <summary>A book's place in a series as far as its title says: the series (normalised, so spellings compare equal), its number, and the series as the title spells it.</summary>
public record SeriesPosition(string SeriesKey, decimal Number, string Name = "");

/// <summary>
/// Works out "book 3 of the Moonfall series" from a Jellyfin title, because Jellyfin keeps no reliable series
/// information for audiobooks. Handles the title shapes seen in real libraries -- "Title (Series, #1)",
/// "Series Series #1", "Title, Book 3", "Series 06 - Title", "Title (1 of 2)" -- and returns null for everything
/// else rather than guessing.
/// </summary>
public static partial class SeriesParser
{
    // "(Dragon of 23rd Street, #1)", "(Fae & Alchemy #1)", "(Gods and Monsters, Book 3)"
    [GeneratedRegex(@"\((?<s>[^()]+?)[,:]?\s*(?:#|book\s+|vol(?:ume)?\.?\s*)(?<n>\d+(?:\.\d+)?)\)", RegexOptions.IgnoreCase)]
    private static partial Regex Parenthesised();

    // "...: Moonfall Series #1", "Title - Moonfall Series Book 1"
    [GeneratedRegex(@"(?::|(?<=\s)[\-–])\s*(?<s>[^:\-–()]+?)\s+series\s*(?:#|book\s+)?(?<n>\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesWord();

    // "The Dawn of the Cursed Queen: Gods and Monsters, Book 3"
    [GeneratedRegex(@"(?::|(?<=\s)[\-–])\s*(?<s>[^:\-–()]+?),?\s+(?:#|book\s+)(?<n>\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AfterColon();

    // "David Wolf 06 - Smoked Out"
    [GeneratedRegex(@"^(?<s>.+?)\s+(?<n>\d{1,3})\s+[-–]\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingNumber();

    // "The Throne of Broken Gods (2 of 2) [Dramatized Adaptation]"
    [GeneratedRegex(@"^(?<s>.+?)\s*\((?<n>\d+)\s+of\s+\d+\)", RegexOptions.IgnoreCase)]
    private static partial Regex PartOf();

    public static SeriesPosition? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        foreach (var regex in new[] { Parenthesised(), SeriesWord(), AfterColon(), PartOf(), LeadingNumber() })
        {
            var m = regex.Match(title);
            if (!m.Success)
            {
                continue;
            }

            var key = Normalise(m.Groups["s"].Value);
            if (key.Length < 2 || !decimal.TryParse(m.Groups["n"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }
            return new SeriesPosition(key, number, m.Groups["s"].Value.Trim(' ', ',', ':', '-', '–'));
        }

        return null;
    }

    /// <summary>Lower-case letters and digits only, no leading "the", no trailing "series": "The Moonfall Series" and "moonfall" are one series.</summary>
    public static string Normalise(string series)
    {
        var words = NonAlphanumeric().Replace(series.ToLowerInvariant().Replace("&", " and "), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (words.Count > 1 && words[0] == "the")
        {
            words.RemoveAt(0);
        }
        if (words.Count > 1 && words[^1] == "series")
        {
            words.RemoveAt(words.Count - 1);
        }
        return string.Join(' ', words);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumeric();
}
