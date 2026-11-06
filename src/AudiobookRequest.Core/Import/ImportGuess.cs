using System.Text.RegularExpressions;

namespace AudiobookRequest.Core.Import;

/// <summary>What a file says about itself (embedded tags), as read by ffprobe.</summary>
public record AudioFileInfo(string Path, double DurationSeconds, string? Codec, string? Title, string? Artist, string? AlbumArtist, string? Album);

public record ImportGuess(string Title, string? Author);

/// <summary>
/// Works out a likely title and author for a candidate from its tags and where it sits on disk, to pre-fill
/// the search. It is only ever a starting point -- the person picks the real match.
/// </summary>
public static partial class ImportGuessBuilder
{
    [GeneratedRegex(@"\s*[\(\[][^)\]]*\b(unabridged|abridged|mp3|m4b|audiobook|dramati[sz]ed|retail|\d+\s*kbps)\b[^)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex EditionNoise();

    [GeneratedRegex(@"^\s*(\d{1,3}\s*[-._)]\s*|\d{1,3}\s+)")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex Dash();

    [GeneratedRegex(@"[_]+")]
    private static partial Regex Underscores();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static ImportGuess Build(string relativePath, bool isFolder, IReadOnlyList<AudioFileInfo> files)
    {
        var first = files.Count > 0 ? files[0] : null;
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ownName = isFolder ? segments.LastOrDefault() ?? "" : System.IO.Path.GetFileNameWithoutExtension(segments.LastOrDefault() ?? "");
        var parentName = segments.Length >= 2 ? segments[^2] : null;

        // Tags: parts of one book carry the book's name in "album" and the chapter in "title"; a lone file is the reverse --
        // unless its "title" is plainly a chapter or track ("Chapter 1", "Track 07"), when the album is the better name.
        var chapterTitles = files.Count > 1 || LooksLikeChapter(first?.Title);
        var tagTitle = chapterTitles ? Clean(first?.Album) ?? Clean(first?.Title) : Clean(first?.Title) ?? Clean(first?.Album);
        var tagAuthor = Clean(first?.AlbumArtist) ?? Clean(first?.Artist);

        string? nameAuthor = null;
        var nameTitle = Tidy(ownName);
        // "Author - Title" in the folder or file name.
        var parts = Dash().Split(nameTitle, 2);
        if (parts.Length == 2 && LooksLikePersonName(parts[0]))
        {
            nameAuthor = Tidy(parts[0]);
            nameTitle = Tidy(parts[1]);
        }
        // "Author/Title/..." -- the folder above the book is often the author.
        else if (parentName is not null && LooksLikePersonName(Tidy(parentName)))
        {
            nameAuthor = Tidy(parentName);
        }

        var title = tagTitle ?? nameTitle;
        var author = tagAuthor ?? nameAuthor;
        return new ImportGuess(string.IsNullOrWhiteSpace(title) ? ownName : title, string.IsNullOrWhiteSpace(author) ? null : author);
    }

    [GeneratedRegex(@"^\s*((chapter|track|part|section|disc|cd)\s*\d+|\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterTitle();

    private static bool LooksLikeChapter(string? title) => title is not null && ChapterTitle().IsMatch(title);

    private static string? Clean(string? value)
    {
        var t = Tidy(value ?? "");
        return t.Length == 0 ? null : t;
    }

    /// <summary>Underscores to spaces, edition noise and leading track numbers removed, whitespace collapsed.</summary>
    public static string Tidy(string value)
    {
        var s = Underscores().Replace(value, " ");
        s = EditionNoise().Replace(s, "");
        s = LeadingNumber().Replace(s, "");
        return Spaces().Replace(s, " ").Trim();
    }

    // Two to four words, no digits: "Amber V. Nicole" yes; "Book 3" or "The Dawn of the Cursed Queen" no.
    private static bool LooksLikePersonName(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 4 || value.Any(char.IsDigit))
        {
            return false;
        }
        var lower = new HashSet<string>(["the", "of", "and", "a", "an", "to", "in", "book", "part", "vol", "volume"], StringComparer.OrdinalIgnoreCase);
        return words.Count(w => lower.Contains(w.Trim('.'))) == 0;
    }
}
