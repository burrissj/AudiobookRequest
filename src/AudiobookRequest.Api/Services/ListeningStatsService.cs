using System.Globalization;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record ListenDayPoint(string Day, int Seconds);

public record ListenTopBook(string ItemId, string Title, string? Author, int Seconds);

public record ListenTopAuthor(string Author, int Seconds);

/// <param name="TimeSavedSeconds">Book time covered minus real time spent: what listening faster bought you.</param>
public record ListenStatsDto(
    int TodaySeconds,
    int Last7DaysSeconds,
    int Last30DaysSeconds,
    int ThisYearSeconds,
    long AllTimeSeconds,
    long TimeSavedSeconds,
    int CurrentStreakDays,
    int LongestStreakDays,
    int ActiveDays,
    int AverageSecondsPerActiveDay,
    IReadOnlyList<ListenDayPoint> Last30Days,
    IReadOnlyList<ListenTopBook> TopBooks,
    IReadOnlyList<ListenTopAuthor> TopAuthors,
    int? BooksFinishedThisYear,
    int? BooksFinishedTotal);

/// <summary>Turns the player's listening reports into per-day totals, and the totals into the numbers the Stats page shows.</summary>
public class ListeningStatsService(AppDbContext db)
{
    // A day only counts toward a streak if it had at least this much listening, so brushing play doesn't keep one alive.
    public const int StreakMinimumSeconds = 60;

    private const double MaxSecondsPerReport = 4 * 3600;
    private const double MaxSpeed = 3.25;
    private const double MaxContentPerBookPerDay = 72 * 3600;
    private const double MaxWallPerBookPerDay = 24 * 3600;

    /// <summary>
    /// Adds a report to the person's totals. The client sends what it hasn't yet delivered, so the numbers are
    /// bounded rather than trusted: nothing negative, nothing faster than 3x real time, nothing absurd for a day.
    /// </summary>
    public async Task RecordAsync(int userId, string itemId, ProgressRequest req, DateOnly utcToday, CancellationToken ct)
    {
        var wall = Clamp(req.WallSeconds, MaxSecondsPerReport);
        var content = Math.Min(Clamp(req.ListenedSeconds, MaxSecondsPerReport), wall * MaxSpeed + 2);
        if (content < 0.5 && wall < 0.5)
        {
            return;
        }

        var day = ResolveDay(req.LocalDate, utcToday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var row = await db.ListeningDays.FirstOrDefaultAsync(d => d.AppUserId == userId && d.Day == day && d.JellyfinItemId == itemId, ct);
        if (row is null)
        {
            row = new ListeningDay { AppUserId = userId, Day = day, JellyfinItemId = itemId, Title = Trim(req.Title, 200) ?? "Unknown book" };
            db.ListeningDays.Add(row);
        }

        row.ContentSeconds = Math.Min(MaxContentPerBookPerDay, row.ContentSeconds + content);
        row.WallSeconds = Math.Min(MaxWallPerBookPerDay, row.WallSeconds + wall);
        row.Author = Trim(req.Author, 200) ?? row.Author;
        if (Trim(req.Title, 200) is { } title)
        {
            row.Title = title;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ListenStatsDto> GetAsync(int userId, DateOnly today, int? finishedThisYear, int? finishedTotal, CancellationToken ct)
    {
        var rows = await db.ListeningDays.AsNoTracking().Where(d => d.AppUserId == userId).ToListAsync(ct);
        return Compute(rows, today, finishedThisYear, finishedTotal);
    }

    /// <summary>The listener's own date if it's plausible (anywhere on Earth is within a day of UTC), otherwise UTC's.</summary>
    public static DateOnly ResolveDay(string? localDate, DateOnly utcToday)
    {
        if (DateOnly.TryParseExact(localDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d >= utcToday.AddDays(-2) && d <= utcToday.AddDays(1))
        {
            return d;
        }
        return utcToday;
    }

    public static ListenStatsDto Compute(IReadOnlyList<ListeningDay> rows, DateOnly today, int? finishedThisYear, int? finishedTotal)
    {
        var perDay = rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.ContentSeconds));

        double OnDay(DateOnly d) => perDay.GetValueOrDefault(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        double Since(DateOnly first) => perDay.Where(kv => string.CompareOrdinal(kv.Key, first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0
                                                          && string.CompareOrdinal(kv.Key, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) <= 0).Sum(kv => kv.Value);

        var last30 = Enumerable.Range(0, 30)
            .Select(i => today.AddDays(i - 29))
            .Select(d => new ListenDayPoint(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (int)Math.Round(OnDay(d))))
            .ToList();

        var activeDays = perDay
            .Where(kv => kv.Value >= StreakMinimumSeconds)
            .Select(kv => DateOnly.ParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet();

        var allTime = rows.Sum(r => r.ContentSeconds);
        var saved = rows.GroupBy(r => r.Day).Sum(g => Math.Max(0, g.Sum(r => r.ContentSeconds) - g.Sum(r => r.WallSeconds)));

        var topBooks = rows
            .GroupBy(r => r.JellyfinItemId)
            .Select(g => new ListenTopBook(g.Key, g.OrderByDescending(r => r.UpdatedAt).First().Title, g.OrderByDescending(r => r.UpdatedAt).First().Author, (int)Math.Round(g.Sum(r => r.ContentSeconds))))
            .OrderByDescending(b => b.Seconds)
            .Take(5)
            .ToList();

        var topAuthors = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Author))
            .GroupBy(r => r.Author!.Split(',')[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ListenTopAuthor(g.Key, (int)Math.Round(g.Sum(r => r.ContentSeconds))))
            .OrderByDescending(a => a.Seconds)
            .Take(5)
            .ToList();

        return new ListenStatsDto(
            (int)Math.Round(OnDay(today)),
            (int)Math.Round(Since(today.AddDays(-6))),
            (int)Math.Round(Since(today.AddDays(-29))),
            (int)Math.Round(Since(new DateOnly(today.Year, 1, 1))),
            (long)Math.Round(allTime),
            (long)Math.Round(saved),
            CurrentStreak(activeDays, today),
            LongestStreak(activeDays),
            activeDays.Count,
            activeDays.Count == 0 ? 0 : (int)Math.Round(allTime / activeDays.Count),
            last30,
            topBooks,
            topAuthors,
            finishedThisYear,
            finishedTotal);
    }

    /// <summary>Consecutive active days ending today -- or yesterday, so a streak doesn't read as broken before you've listened today.</summary>
    public static int CurrentStreak(HashSet<DateOnly> active, DateOnly today)
    {
        var day = active.Contains(today) ? today : today.AddDays(-1);
        var count = 0;
        while (active.Contains(day))
        {
            count++;
            day = day.AddDays(-1);
        }
        return count;
    }

    public static int LongestStreak(HashSet<DateOnly> active)
    {
        var longest = 0;
        foreach (var start in active.Where(d => !active.Contains(d.AddDays(-1))))
        {
            var length = 0;
            for (var d = start; active.Contains(d); d = d.AddDays(1))
            {
                length++;
            }
            longest = Math.Max(longest, length);
        }
        return longest;
    }

    private static double Clamp(double? value, double max) =>
        value is { } v && double.IsFinite(v) ? Math.Clamp(v, 0, max) : 0;

    private static string? Trim(string? value, int max)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            return null;
        }
        return t.Length > max ? t[..max] : t;
    }
}
