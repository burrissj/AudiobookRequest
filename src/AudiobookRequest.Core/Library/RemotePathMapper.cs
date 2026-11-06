namespace AudiobookRequest.Core.Library;

/// <summary>
/// Translates a file path as Chaptarr reports it (its own root-folder configuration, which may not
/// match any path reachable from the machine running this service -- Chaptarr can run on an entirely
/// different host) into the equivalent path on this machine, by swapping a configured prefix. Mirrors
/// the "Remote Path Mapping" concept every *arr app has for exactly this scenario.
/// </summary>
public static class RemotePathMapper
{
    /// <summary>Returns the path unchanged if either prefix is blank or the input doesn't start with the Chaptarr-side prefix.</summary>
    public static string ToLocalPath(string chaptarrPath, string? chaptarrPrefix, string? localPrefix)
    {
        if (string.IsNullOrWhiteSpace(chaptarrPrefix) || string.IsNullOrWhiteSpace(localPrefix))
        {
            return chaptarrPath;
        }

        var trimmedPrefix = chaptarrPrefix.TrimEnd('\\', '/');
        if (!chaptarrPath.StartsWith(trimmedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return chaptarrPath;
        }

        var remainder = chaptarrPath[trimmedPrefix.Length..];
        return localPrefix.TrimEnd('\\', '/') + remainder;
    }
}
