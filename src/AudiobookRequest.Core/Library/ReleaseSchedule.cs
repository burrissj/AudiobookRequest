namespace AudiobookRequest.Core.Library;

/// <summary>
/// How often to look for a book around its release. Before the release date nothing can usually be found, so
/// checks are rare and only catch an early or mis-dated release; once it is out, indexers need a little while to
/// carry it, so searches are frequent for the first days and then ease back to the normal wishlist pace.
/// </summary>
public static class ReleaseSchedule
{
    public static bool IsUnreleased(DateTimeOffset? releaseDate, DateTimeOffset now) => releaseDate is { } d && d > now;

    /// <summary>The wait between checks while a book is still unreleased: weekly when it is months away, down to every 6 hours in the last 3 days.</summary>
    public static TimeSpan UpcomingInterval(DateTimeOffset releaseDate, DateTimeOffset now)
    {
        var untilRelease = releaseDate - now;
        if (untilRelease > TimeSpan.FromDays(60)) return TimeSpan.FromDays(7);
        if (untilRelease > TimeSpan.FromDays(14)) return TimeSpan.FromDays(3);
        if (untilRelease > TimeSpan.FromDays(3)) return TimeSpan.FromDays(1);
        return TimeSpan.FromHours(6);
    }

    /// <summary>When an upcoming book is next checked: straight away the first time, then one interval after the last check.</summary>
    public static DateTimeOffset NextUpcomingCheck(DateTimeOffset? lastCheck, DateTimeOffset requestedAt, DateTimeOffset releaseDate, DateTimeOffset now) =>
        lastCheck is { } last ? last + UpcomingInterval(releaseDate, now) : requestedAt;

    /// <summary>
    /// The wait between searches for a wishlisted book that has nothing yet: every 30 minutes on release day and the
    /// day after, every 2 hours to the end of the first week, every 6 hours to the end of the first month, then the
    /// configured wishlist pace. Never slower than that pace, and unchanged for a book with no (or a future) release date.
    /// </summary>
    public static TimeSpan RetryInterval(DateTimeOffset? releaseDate, DateTimeOffset now, int wishlistRetryHours)
    {
        var normal = TimeSpan.FromHours(Math.Max(1, wishlistRetryHours));
        if (releaseDate is not { } released || released > now)
        {
            return normal;
        }

        var age = now - released;
        var fast = age < TimeSpan.FromDays(2) ? TimeSpan.FromMinutes(30)
            : age < TimeSpan.FromDays(7) ? TimeSpan.FromHours(2)
            : age < TimeSpan.FromDays(30) ? TimeSpan.FromHours(6)
            : normal;
        return fast < normal ? fast : normal;
    }
}
