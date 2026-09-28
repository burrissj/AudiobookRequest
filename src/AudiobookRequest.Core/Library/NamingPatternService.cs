namespace AudiobookRequest.Core.Library;

public static class NamingPatternService
{
    /// <summary>
    /// Applies a pattern like "{Author}/{Series}/{Title}" against the given tags, sanitizing each
    /// path segment for filesystem-invalid characters. A "{Series}" token that has no series value
    /// collapses away (segment and its surrounding slash are dropped) rather than leaving an empty folder.
    /// Returns a relative path with no extension.
    /// </summary>
    public static string BuildRelativePath(string pattern, string author, string title, string? series)
    {
        var replaced = pattern
            .Replace("{Author}", author, StringComparison.OrdinalIgnoreCase)
            .Replace("{Title}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{Series}", series ?? "", StringComparison.OrdinalIgnoreCase);

        var segments = replaced
            .Split('/', '\\')
            .Select(Sanitize)
            .Where(s => s.Length > 0)
            .ToArray();

        if (segments.Length == 0)
        {
            segments = [Sanitize(title)];
        }

        return Path.Combine(segments);
    }

    private static string Sanitize(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = segment.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}
