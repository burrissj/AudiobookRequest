using System.Text.RegularExpressions;

namespace AudiobookRequest.Core.Recommendations;

/// <summary>
/// Interprets Audible's "series sequence" string, confirmed live: "3" means the 3rd book in the
/// series, "1-3" means an omnibus spanning books 1 through 3. Used to tell a real single book apart
/// from a bundle, and to figure out how many books a bundle actually contains.
/// </summary>
public static partial class AudiobookSeries
{
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex SingleBookPattern();

    [GeneratedRegex(@"^(\d+)-(\d+)$")]
    private static partial Regex BundleRangePattern();

    public static bool IsSingleBook(string? sequence) => sequence is not null && SingleBookPattern().IsMatch(sequence);

    public static int? ParseSingleBookNumber(string? sequence) =>
        IsSingleBook(sequence) ? int.Parse(sequence!) : null;

    /// <summary>Number of books a bundle covers (e.g. 3 for "1-3"), or null if this isn't a bundle sequence.</summary>
    public static int? ParseBundleCount(string? sequence)
    {
        if (sequence is null)
        {
            return null;
        }
        var match = BundleRangePattern().Match(sequence);
        if (!match.Success)
        {
            return null;
        }
        var start = int.Parse(match.Groups[1].Value);
        var end = int.Parse(match.Groups[2].Value);
        return end >= start ? end - start + 1 : null;
    }
}
