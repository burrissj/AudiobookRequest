using System.Net;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Tests;

public class ListenHlsAndSeriesTests
{
    private const string BookId = "0123456789abcdef0123456789abcdef";
    private static readonly JellyfinSession Session = new("tok", "uid", "abr-0123456789abcdef01234567", "Safari on iPhone");

    private static JellyfinPlayerItem Book(string id = BookId, string name = "Dune", string codec = "aac", string author = "Frank Herbert", bool played = false) => new()
    {
        Id = id,
        Name = name,
        ArtistItems = [new JellyfinNameId { Name = author }],
        RunTimeTicks = 36_000_000_000,
        UserData = new JellyfinPlayerUserData { Played = played },
        MediaSources = [new JellyfinMediaSource { Id = "ms1", Container = "m4b", MediaStreams = [new JellyfinMediaStream { Type = "Audio", Codec = codec }] }],
    };

    private static (ListenService Service, FakeJellyfinPlayerClient Player, TestDb Db) Make()
    {
        var db = new TestDb();
        var player = new FakeJellyfinPlayerClient();
        return (new ListenService(db.Settings, player, new MemoryCache(new MemoryCacheOptions())), player, db);
    }

    // ---- playback plan ----

    [Theory]
    [InlineData("aac", PlaybackMode.Direct)]
    [InlineData("mp3", PlaybackMode.Direct)]
    [InlineData("eac3", PlaybackMode.Hls)]
    [InlineData("ac3", PlaybackMode.Hls)]
    [InlineData("dts", PlaybackMode.Hls)]
    public void Plan_PlaysAsIsWhenBrowsersCan_ElseConverts(string codec, PlaybackMode expected) =>
        Assert.Equal(expected, ListenService.Plan(Book(codec: codec).MediaSources![0]).Mode);

    [Fact]
    public void Plan_NoFileCannotBePlayed() => Assert.Equal(PlaybackMode.None, ListenService.Plan(null).Mode);

    [Fact]
    public void Detail_ConvertedBooksAreStreamedAsHls_AndCannotBeDownloaded()
    {
        var detail = ListenService.ToDetail(Book(codec: "eac3"));

        Assert.True(detail.CanPlay);
        Assert.Equal("hls", detail.StreamMode);
        Assert.False(detail.CanDownload);
    }

    [Fact]
    public void Detail_PlainBooksStreamDirectlyAndCanBeDownloaded()
    {
        var detail = ListenService.ToDetail(Book());

        Assert.Equal("direct", detail.StreamMode);
        Assert.True(detail.CanDownload);
    }

    // ---- HLS ----

    [Fact]
    public async Task Playlist_AsksJellyfinForThirtySecondAacSegments_AsThatDevice()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book(codec: "eac3"));

        (await service.OpenHlsPlaylistAsync(Session, BookId, default)).Dispose();

        var path = player.Opened.Single().Path;
        Assert.StartsWith($"/Audio/{BookId}/main.m3u8?", path);
        Assert.Contains("AudioCodec=aac", path);
        Assert.Contains("TranscodingProtocol=hls", path);
        Assert.Contains("SegmentLength=30", path);
        Assert.Contains("UserId=uid", path);
        Assert.Contains("DeviceId=abr-0123456789abcdef01234567", path);
        Assert.Contains("MediaSourceId=ms1", path);
        Assert.Null(player.Opened.Single().Range);
    }

    [Fact]
    public async Task Playlist_RefusesBooksThatPlayAsIs()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book());

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.OpenHlsPlaylistAsync(Session, BookId, default));

        Assert.Equal(409, ex.StatusCode);
        Assert.Empty(player.Opened);
    }

    [Fact]
    public async Task Playlist_JellyfinRefusingToConvertBecomesAPlainError()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book(codec: "eac3"));
        player.OpenStatus = HttpStatusCode.Forbidden;

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.OpenHlsPlaylistAsync(Session, BookId, default));

        Assert.Equal(502, ex.StatusCode);
        Assert.Contains("Open it in Jellyfin", ex.Message);
    }

    [Theory]
    [InlineData("hls1/main/0.ts", true)]
    [InlineData("hls1/main/11542.ts", true)]
    [InlineData("hls1/main/7.aac", true)]
    [InlineData("main.m3u8", false)]
    [InlineData("hls1/main/../../../etc/passwd", false)]
    [InlineData("hls1/main/0.ts/extra", false)]
    [InlineData("../hls1/main/0.ts", false)]
    [InlineData("hls1/ma in/0.ts", false)]
    [InlineData("hls1/main/x.ts", false)]
    [InlineData("Items/abc/Download", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SegmentPaths_AreStrictlyValidated(string? path, bool valid) => Assert.Equal(valid, ListenService.IsValidHlsSegment(path));

    [Fact]
    public async Task Segment_IsFetchedFromTheSameBookWithJellyfinsOwnQuery()
    {
        var (service, player, db) = Make();
        using var _ = db;

        (await service.OpenHlsSegmentAsync(Session, BookId, "hls1/main/12.ts", "?PlaySessionId=abc&runtimeTicks=1", default)).Dispose();

        Assert.Equal($"/Audio/{BookId}/hls1/main/12.ts?PlaySessionId=abc&runtimeTicks=1", player.Opened.Single().Path);
    }

    [Fact]
    public async Task Segment_RejectsOddPathsAndHugeQueries_WithoutCallingJellyfin()
    {
        var (service, player, db) = Make();
        using var _ = db;

        await Assert.ThrowsAsync<ListenException>(() => service.OpenHlsSegmentAsync(Session, BookId, "../x", "", default));
        await Assert.ThrowsAsync<ListenException>(() => service.OpenHlsSegmentAsync(Session, BookId, "hls1/main/1.ts", "?" + new string('a', 3000), default));

        Assert.Empty(player.Opened);
    }

    [Fact]
    public async Task StopHls_StopsTheConversionItStarted_OnlyOnce()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book(codec: "eac3"));
        (await service.OpenHlsPlaylistAsync(Session, BookId, default)).Dispose();

        await service.StopHlsAsync(Session, BookId, default);
        await service.StopHlsAsync(Session, BookId, default);

        Assert.Single(player.StoppedTranscodes);
        Assert.Contains(player.StoppedTranscodes[0], player.Opened.Single().Path);
    }

    [Fact]
    public async Task StopHls_WithNothingStarted_DoesNothing()
    {
        var (service, player, db) = Make();
        using var _ = db;

        await service.StopHlsAsync(Session, BookId, default);

        Assert.Empty(player.StoppedTranscodes);
    }

    // ---- finished filter ----

    [Fact]
    public async Task Library_Finished_AsksForPlayedBooksNewestFirst()
    {
        var (service, player, db) = Make();
        using var _ = db;

        await service.GetLibraryAsync(Session, ListenFilter.Finished, null, 0, 20, default);

        var q = player.Queries.Single();
        Assert.True(q.PlayedOnly);
        Assert.False(q.ResumableOnly);
        Assert.Equal("DatePlayed", q.SortBy);
        Assert.True(q.Descending);
    }

    [Fact]
    public async Task CountFinished_CountsThisYearsAndTotal()
    {
        var (service, player, db) = Make();
        using var _ = db;
        var thisYear = Book("11111111111111111111111111111111");
        thisYear.UserData = new JellyfinPlayerUserData { Played = true, LastPlayedDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) };
        var lastYear = Book("22222222222222222222222222222222");
        lastYear.UserData = new JellyfinPlayerUserData { Played = true, LastPlayedDate = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero) };
        player.Items.AddRange([thisYear, lastYear]);

        var (year, total) = await service.CountFinishedAsync(Session, 2026, default);

        Assert.Equal(1, year);
        Assert.Equal(2, total);
    }

    // ---- next in series ----

    [Fact]
    public void Next_PicksTheNextNumberedBookByTheSameAuthor()
    {
        var current = Book("c", "Dragon Chained (The Dragon of 23rd Street, #1)", author: "Ann");
        var library = new List<JellyfinPlayerItem>
        {
            current,
            Book("b3", "Dragon Bound (The Dragon of 23rd Street, #3)", author: "Ann"),
            Book("b2", "Dragon Burned (The Dragon of 23rd Street, #2)", author: "Ann"),
            Book("other", "Dragon Rider (The Dragon of 23rd Street, #2)", author: "Someone Else"),
            Book("unrelated", "Another Book (Other Series, #2)", author: "Ann"),
        };

        Assert.Equal("b2", ListenService.FindNextInSeries(current, library)?.Id);
    }

    [Fact]
    public void Next_SkipsBooksAlreadyFinished()
    {
        var current = Book("c", "Heir (Gods & Monsters Book 1)", author: "Ann");
        var library = new List<JellyfinPlayerItem>
        {
            current,
            Book("b2", "Spare (Gods & Monsters Book 2)", author: "Ann", played: true),
            Book("b3", "Crown (Gods & Monsters Book 3)", author: "Ann"),
        };

        Assert.Equal("b3", ListenService.FindNextInSeries(current, library)?.Id);
    }

    [Fact]
    public void Next_HandlesPartsAndDuplicateCopies()
    {
        var current = Book("p1a", "The Throne of Broken Gods (1 of 2) [Dramatized Adaptation]", author: "Amber V. Nicole");
        var library = new List<JellyfinPlayerItem>
        {
            current,
            Book("p1b", "The Throne of Broken Gods (1 of 2) [Dramatized Adaptation]", author: "Amber V. Nicole"),
            Book("p2a", "The Throne of Broken Gods (2 of 2) [Dramatized Adaptation]", author: "Amber V. Nicole"),
            Book("p2b", "The Throne of Broken Gods (2 of 2) [Dramatized Adaptation]", author: "Amber V. Nicole"),
        };

        Assert.Equal("p2a", ListenService.FindNextInSeries(current, library)?.Id);
    }

    [Fact]
    public void Next_LastBookOrNoSeriesHasNoNext()
    {
        var last = Book("c", "Crown (Gods & Monsters Book 3)");
        var standalone = Book("s", "The Ballad of Falling Dragons");

        Assert.Null(ListenService.FindNextInSeries(last, [last, Book("b1", "Heir (Gods & Monsters Book 1)")]));
        Assert.Null(ListenService.FindNextInSeries(standalone, [standalone, Book("b2", "Another (Gods & Monsters Book 2)")]));
    }

    [Fact]
    public async Task Next_ReadsTheWholeLibraryOncePerFewMinutes()
    {
        var (service, player, db) = Make();
        using var _ = db;
        var current = Book("c1111111111111111111111111111111", "Heir (Gods & Monsters Book 1)");
        player.Items.AddRange([current, Book("c2222222222222222222222222222222", "Spare (Gods & Monsters Book 2)")]);

        var first = await service.GetNextInSeriesAsync(Session, current.Id, default);
        var second = await service.GetNextInSeriesAsync(Session, current.Id, default);

        Assert.Equal("c2222222222222222222222222222222", first?.Id);
        Assert.Equal(first?.Id, second?.Id);
        Assert.Single(player.Queries);
    }
}
