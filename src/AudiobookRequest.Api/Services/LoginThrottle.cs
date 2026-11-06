namespace AudiobookRequest.Api.Services;

/// <summary>
/// Slows down password guessing against the sign-in page. Sign-in is checked against Jellyfin, so without this anyone
/// who can reach the page can try passwords as fast as the server will answer.
///
/// Failed attempts are remembered for a while, three ways: for one address trying one username (the common case: a
/// person mistyping), for one username from anywhere (someone guessing at an account from many addresses), and for one
/// address trying anything (someone working through a list of usernames). Once any of them is over its limit, further
/// attempts are refused without asking Jellyfin until enough time has passed.
///
/// The limit for one address on one username is the strictest, and the one a person typing is likely to meet, so a
/// stranger who keeps failing against your name cannot easily lock you out from your own phone: they only fill their
/// own address's allowance, and the wider limits are set well above it. State is in memory; a restart clears it.
/// </summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public const int MaxPerAddressAndUser = 5;
    public const int MaxPerUser = 15;
    public const int MaxPerAddress = 30;

    // Bounds the memory an attacker can make this use by trying endless different usernames.
    private const int MaxKeys = 20_000;

    private readonly object gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> failures = [];

    private static string Normalise(string username)
    {
        var name = username.Trim().ToLowerInvariant();
        return name.Length > 100 ? name[..100] : name;
    }

    private static (string Pair, string User, string Address) Keys(string address, string username)
    {
        var user = Normalise(username);
        return ($"p|{address}|{user}", $"u|{user}", $"a|{address}");
    }

    /// <returns>How long to wait before trying again, or null when this attempt may go ahead.</returns>
    public TimeSpan? BlockedFor(string address, string username)
    {
        var (pair, user, addr) = Keys(address, username);
        lock (gate)
        {
            var now = clock.GetUtcNow();
            var waits = new[] { WaitFor(pair, MaxPerAddressAndUser, now), WaitFor(user, MaxPerUser, now), WaitFor(addr, MaxPerAddress, now) };
            var longest = waits.Max();
            return longest > TimeSpan.Zero ? longest : null;
        }
    }

    public void RecordFailure(string address, string username)
    {
        var (pair, user, addr) = Keys(address, username);
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var key in new[] { pair, user, addr })
            {
                if (!failures.TryGetValue(key, out var list))
                {
                    failures[key] = list = [];
                }
                Prune(list, now);
                list.Add(now);
            }
            if (failures.Count > MaxKeys)
            {
                Evict(now);
            }
        }
    }

    /// <summary>A correct password: forget the failures against that account, so earlier typos don't count against it.</summary>
    public void RecordSuccess(string address, string username)
    {
        var (pair, user, _) = Keys(address, username);
        lock (gate)
        {
            failures.Remove(pair);
            failures.Remove(user);
        }
    }

    // Caller holds the gate. Zero when the key is within its limit, otherwise how long until it is.
    private TimeSpan WaitFor(string key, int limit, DateTimeOffset now)
    {
        if (!failures.TryGetValue(key, out var list))
        {
            return TimeSpan.Zero;
        }
        Prune(list, now);
        if (list.Count < limit)
        {
            return TimeSpan.Zero;
        }
        // Back under the limit once enough of the oldest failures have aged out of the window.
        return list[list.Count - limit] + Window - now;
    }

    private static void Prune(List<DateTimeOffset> list, DateTimeOffset now) => list.RemoveAll(t => now - t >= Window);

    private void Evict(DateTimeOffset now)
    {
        foreach (var key in failures.Keys.ToList())
        {
            Prune(failures[key], now);
            if (failures[key].Count == 0)
            {
                failures.Remove(key);
            }
        }

        // Still too many (a flood within one window): let go of the keys with the fewest failures first, then the
        // stalest. Those are one-off typos and probes; the keys of someone really hammering the page are the last to go.
        if (failures.Count > MaxKeys)
        {
            var drop = failures.OrderBy(kv => kv.Value.Count).ThenBy(kv => kv.Value[^1]).Take(failures.Count - MaxKeys * 9 / 10);
            foreach (var key in drop.Select(kv => kv.Key).ToList())
            {
                failures.Remove(key);
            }
        }
    }

    /// <summary>"3 minutes", "1 minute", "40 seconds" -- for telling a person how long to wait.</summary>
    public static string Describe(TimeSpan wait)
    {
        var seconds = (int)Math.Ceiling(wait.TotalSeconds);
        if (seconds < 90)
        {
            return $"{Math.Max(1, seconds)} second{(seconds == 1 ? "" : "s")}";
        }
        var minutes = (int)Math.Ceiling(seconds / 60.0);
        return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
    }
}
