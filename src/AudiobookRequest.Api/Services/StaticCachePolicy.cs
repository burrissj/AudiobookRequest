namespace AudiobookRequest.Api.Services;

/// <summary>
/// Cache rules for the app's own files. Without an explicit rule a browser guesses how long to keep index.html from
/// its age, and can keep showing the previous version of the whole app (and the old service worker) for days after
/// an update. The page, service worker and manifest are therefore revalidated every time (a cheap 304 when
/// unchanged); the build's content-hashed files can safely be kept forever because their names change with them.
/// </summary>
public static class StaticCachePolicy
{
    public const string Revalidate = "no-cache";
    public const string Immutable = "public, max-age=31536000, immutable";
    public const string Icons = "public, max-age=86400";

    public static string For(string? requestPath)
    {
        var path = (requestPath ?? "").ToLowerInvariant();
        if (path.StartsWith("/assets/"))
        {
            return Immutable;
        }
        if (path.StartsWith("/icons/"))
        {
            return Icons;
        }
        return Revalidate;
    }
}
