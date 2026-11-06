using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Tests;

public class ListeningStatsTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private const string ItemA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ItemB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static ListeningDay Row(string day, string item, double content, double wall, string title = "Book", string? author = "Author One") => new()
    {
        Day = day,
        JellyfinItemId = item,
        Title = title,
        Author = author,
        ContentSeconds = content,
        WallSeconds = wall,
    };

    private static string D(int daysAgo) => Today.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Record_AccumulatesIntoOneRowPerBookPerDay()
    {
        using var db = new TestDb();
        var stats = new ListeningStatsService(db.Db);

        await stats.RecordAsync(db.Victoria.Id, ItemA, new ProgressRequest(10, false, "progress", null, 60, 30, "2026-10-09", "Dune", "Frank Herbert"), Today, default);
        await stats.RecordAsync(db.Victoria.Id, ItemA, new ProgressRequest(70, false, "progress", null, 60, 30, "2026-10-09", "Dune", "Frank Herbert"), Today, default);

        var row = await db.Db.ListeningDays.SingleAsync();
        Assert.Equal(120, row.ContentSeconds);
        Assert.Equal(60, row.WallSeconds);
        Assert.Equal("Dune", row.Title);
        Assert.Equal("2026-10-09", row.Day);
    }

    [Fact]
    public async Task Record_IgnoresReportsWithNothingToCount()
    {
        using var db = new TestDb();
        var stats = new ListeningStatsService(db.Db);

        await stats.RecordAsync(db.Victoria.Id, ItemA, new ProgressRequest(10, false, "progress", null), Today, default);
        await stats.RecordAsync(db.Victoria.Id, ItemA, new ProgressRequest(10, false, "progress", null, 0, 0), Today, default);

        Assert.Empty(db.Db.ListeningDays);
    }

    [Fact]
    public async Task Record_BoundsWhatItIsTold()
    {
        using var db = new TestDb();
        var stats = new ListeningStatsService(db.Db);

        // Negative, NaN and faster-than-3x claims can't inflate the totals.
        await stats.RecordAsync(db.Victoria.Id, ItemA, new ProgressRequest(0, false, null, null, -500, -500), Today, default);
        await stats.RecordAsync(db.Victoria.Id, ItemA, new ProgressRequest(0, false, null, null, double.NaN, double.NaN), Today, default);
        await stats.RecordAsync(db.Victoria.Id, ItemB, new ProgressRequest(0, false, null, null, 100_000, 10), Today, default);

        var row = await db.Db.ListeningDays.SingleAsync();
        Assert.Equal(ItemB, row.JellyfinItemId);
        // 10s of real time can cover at most ~3.25x that (+2s of slack).
        Assert.InRange(row.ContentSeconds, 0, 40);
    }

    [Theory]
    [InlineData("2026-10-08", "2026-10-08")]
    [InlineData("2026-10-10", "2026-10-10")] // ahead of UTC, e.g. New Zealand
    [InlineData("2026-10-07", "2026-10-07")]
    [InlineData("2026-09-01", "2026-10-09")] // implausible: falls back to UTC's today
    [InlineData("2027-01-01", "2026-10-09")]
    [InlineData("not a date", "2026-10-09")]
    [InlineData(null, "2026-10-09")]
    public void ResolveDay_TrustsOnlyPlausibleLocalDates(string? sent, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ListeningStatsService.ResolveDay(sent, Today));

    [Fact]
    public void Compute_TotalsPeriodsAndTimeSaved()
    {
        var rows = new List<ListeningDay>
        {
            Row(D(0), ItemA, 7200, 3600),          // today: 2h of book in 1h
            Row(D(3), ItemA, 1800, 1800),
            Row(D(20), ItemB, 3600, 3600),
            Row(D(60), ItemB, 3600, 3600),          // outside 30 days, still this year
        };

        var s = ListeningStatsService.Compute(rows, Today, 4, 11);

        Assert.Equal(7200, s.TodaySeconds);
        Assert.Equal(9000, s.Last7DaysSeconds);
        Assert.Equal(12600, s.Last30DaysSeconds);
        Assert.Equal(16200, s.AllTimeSeconds);
        Assert.Equal(3600, s.TimeSavedSeconds);
        Assert.Equal(4, s.ActiveDays);
        Assert.Equal(4050, s.AverageSecondsPerActiveDay);
        Assert.Equal(4, s.BooksFinishedThisYear);
        Assert.Equal(11, s.BooksFinishedTotal);
        Assert.Equal(30, s.Last30Days.Count);
        Assert.Equal(D(29), s.Last30Days[0].Day);
        Assert.Equal(7200, s.Last30Days[^1].Seconds);
    }

    [Fact]
    public void Compute_YearOnlyCountsTheCurrentYear()
    {
        var rows = new List<ListeningDay> { Row("2025-12-31", ItemA, 5000, 5000), Row("2026-01-01", ItemA, 100, 100) };

        var s = ListeningStatsService.Compute(rows, Today, null, null);

        Assert.Equal(100, s.ThisYearSeconds);
        Assert.Equal(5100, s.AllTimeSeconds);
        Assert.Null(s.BooksFinishedTotal);
    }

    [Fact]
    public void Compute_StreakContinuesThroughYesterday()
    {
        // Listened the last three days but not yet today.
        var rows = new List<ListeningDay> { Row(D(1), ItemA, 600, 600), Row(D(2), ItemA, 600, 600), Row(D(3), ItemA, 600, 600), Row(D(5), ItemA, 600, 600) };

        var s = ListeningStatsService.Compute(rows, Today, null, null);

        Assert.Equal(3, s.CurrentStreakDays);
        Assert.Equal(3, s.LongestStreakDays);
    }

    [Fact]
    public void Compute_StreakBreaksAfterAMissedDay()
    {
        var rows = new List<ListeningDay> { Row(D(2), ItemA, 600, 600), Row(D(3), ItemA, 600, 600) };

        var s = ListeningStatsService.Compute(rows, Today, null, null);

        Assert.Equal(0, s.CurrentStreakDays);
        Assert.Equal(2, s.LongestStreakDays);
    }

    [Fact]
    public void Compute_ABriefBrushDoesNotCountAsAStreakDay()
    {
        var rows = new List<ListeningDay> { Row(D(0), ItemA, 20, 20), Row(D(1), ItemA, 3600, 3600) };

        var s = ListeningStatsService.Compute(rows, Today, null, null);

        Assert.Equal(1, s.CurrentStreakDays);
        Assert.Equal(1, s.ActiveDays);
    }

    [Fact]
    public void Compute_TopBooksAndAuthors()
    {
        var rows = new List<ListeningDay>
        {
            Row(D(0), ItemA, 1000, 1000, "Big Book", "Ann Author, Nick Narrator"),
            Row(D(1), ItemA, 1000, 1000, "Big Book", "Ann Author, Nick Narrator"),
            Row(D(0), ItemB, 500, 500, "Small Book", "ann author"),
            Row(D(0), "cccccccccccccccccccccccccccccccc", 100, 100, "Other", "Zed"),
        };

        var s = ListeningStatsService.Compute(rows, Today, null, null);

        Assert.Equal(["Big Book", "Small Book", "Other"], s.TopBooks.Select(b => b.Title));
        Assert.Equal(2000, s.TopBooks[0].Seconds);
        // Authors are grouped by their first name listed, case-insensitively, so a narrator doesn't become an "author".
        Assert.Equal("Ann Author", s.TopAuthors[0].Author);
        Assert.Equal(2500, s.TopAuthors[0].Seconds);
    }

    [Fact]
    public void Compute_NothingListenedIsAllZeros()
    {
        var s = ListeningStatsService.Compute([], Today, null, null);

        Assert.Equal(0, s.AllTimeSeconds);
        Assert.Equal(0, s.CurrentStreakDays);
        Assert.Equal(0, s.AverageSecondsPerActiveDay);
        Assert.Empty(s.TopBooks);
        Assert.Equal(30, s.Last30Days.Count);
    }

    [Fact]
    public async Task Get_OnlyCountsThatPersonsListening()
    {
        using var db = new TestDb();
        var stats = new ListeningStatsService(db.Db);
        await stats.RecordAsync(db.Victoria.Id, ItemA, new ProgressRequest(0, false, null, null, 600, 600, D(0), "Mine"), Today, default);
        await stats.RecordAsync(db.Admin.Id, ItemA, new ProgressRequest(0, false, null, null, 9000, 9000, D(0), "Theirs"), Today, default);

        var mine = await stats.GetAsync(db.Victoria.Id, Today, null, null, default);

        Assert.Equal(600, mine.AllTimeSeconds);
    }
}

public class DeviceNamesTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari on iPhone")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 9 Pro XL) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/130.0.0.0 Mobile/15E148 Safari/604.1", "Chrome on iPhone")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Gecko/20100101 Firefox/131.0", "Firefox on Mac")]
    [InlineData("curl/8.0", "Web Player")]
    [InlineData("", "Web Player")]
    [InlineData(null, "Web Player")]
    public void FromUserAgent_NamesTheBrowserAndDevice(string? ua, string expected) => Assert.Equal(expected, DeviceNames.FromUserAgent(ua));

    [Fact]
    public void NewIds_AreValidAndDifferent()
    {
        var a = DeviceNames.NewId();
        var b = DeviceNames.NewId();

        Assert.True(DeviceNames.IsValidId(a));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("abr-0123456789abcdef01234567", true)]
    [InlineData("abr-0123456789ABCDEF01234567", false)]
    [InlineData("abr-short", false)]
    [InlineData("0123456789abcdef01234567", false)]
    [InlineData("abr-0123456789abcdef0123456\"", false)]
    [InlineData(null, false)]
    public void IsValidId_AcceptsOnlyOurOwnFormat(string? id, bool valid) => Assert.Equal(valid, DeviceNames.IsValidId(id));
}
