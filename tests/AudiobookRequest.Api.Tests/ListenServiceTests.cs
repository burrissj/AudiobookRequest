using System.Security.Claims;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Tests;

public class ListenServiceTests
{
    private const string BookId = "0123456789abcdef0123456789abcdef";
    private static readonly JellyfinSession Session = new("tok", "uid");

    private static JellyfinPlayerItem Book(string id = BookId, string container = "m4b", string codec = "aac", long position = 0) => new()
    {
        Id = id,
        Name = "Dune",
        ArtistItems = [new JellyfinNameId { Name = "Frank Herbert" }, new JellyfinNameId { Name = "Scott Brick" }],
        RunTimeTicks = 36_000_000_000, // 1 hour
        ImageTags = new Dictionary<string, string> { ["Primary"] = "tag" },
        UserData = new JellyfinPlayerUserData { PlaybackPositionTicks = position },
        Chapters = [new JellyfinChapter { StartPositionTicks = 0, Name = "Opening" }, new JellyfinChapter { StartPositionTicks = 18_000_000_000, Name = null }],
        MediaSources = [new JellyfinMediaSource { Id = "ms1", Container = container, MediaStreams = [new JellyfinMediaStream { Type = "Audio", Codec = codec }] }],
    };

    private static (ListenService Service, FakeJellyfinPlayerClient Player, TestDb Db) Make()
    {
        var db = new TestDb();
        var player = new FakeJellyfinPlayerClient();
        return (new ListenService(db.Settings, player, new MemoryCache(new MemoryCacheOptions())), player, db);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("0123456789abcdef0123456789abcde", false)]
    [InlineData("0123456789abcdef0123456789abcdefg", false)]
    [InlineData("abc/../def", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ItemIds_AreStrictlyValidated(string? id, bool valid) => Assert.Equal(valid, ListenService.IsValidItemId(id));

    [Theory]
    [InlineData("m4b", "aac", true)]
    [InlineData("mp4", "AAC", true)]
    [InlineData("mp3", "mp3", true)]
    [InlineData("m4b", "eac3", false)]
    [InlineData("m4b", "ac3", false)]
    [InlineData("flac", "dts", false)]
    [InlineData("wma", "wmav2", false)]
    [InlineData("m4b,mp4", "aac", true)]
    public void Evaluate_ChecksCodecAndContainer(string container, string codec, bool expected)
    {
        var source = new JellyfinMediaSource { Container = container, MediaStreams = [new JellyfinMediaStream { Type = "Audio", Codec = codec }] };

        var (canPlay, reason) = ListenService.Evaluate(source);

        Assert.Equal(expected, canPlay);
        Assert.Equal(expected, reason is null);
    }

    [Fact]
    public void Evaluate_NamesTheCodecInTheReason()
    {
        var (_, reason) = ListenService.Evaluate(new JellyfinMediaSource { Container = "m4b", MediaStreams = [new JellyfinMediaStream { Type = "Audio", Codec = "eac3" }] });

        Assert.Contains("EAC3", reason);
    }

    [Fact]
    public void Evaluate_NoSource_CannotPlay() => Assert.False(ListenService.Evaluate(null).CanPlay);

    [Fact]
    public void Detail_ConvertsTicksAndNamesUnnamedChapters()
    {
        var detail = ListenService.ToDetail(Book(position: 9_000_000_000));

        Assert.Equal("Frank Herbert, Scott Brick", detail.Author);
        Assert.Equal(3600, detail.DurationSeconds);
        Assert.Equal(900, detail.PositionSeconds);
        Assert.True(detail.HasCover);
        Assert.Equal("ms1", detail.MediaSourceId);
        Assert.Equal(["Opening", "Chapter 2"], detail.Chapters.Select(c => c.Name));
        Assert.Equal(1800, detail.Chapters[1].StartSeconds);
        Assert.True(detail.CanPlay);
    }


    [Fact]
    public void Detail_NumericChapterNamesBecomeChapterN()
    {
        var item = Book();
        item.Chapters = [new JellyfinChapter { Name = "1" }, new JellyfinChapter { Name = " 02 " }, new JellyfinChapter { Name = "Prologue" }, new JellyfinChapter { Name = "" }];

        var names = ListenService.ToDetail(item).Chapters.Select(c => c.Name);

        Assert.Equal(["Chapter 1", "Chapter 2", "Prologue", "Chapter 4"], names);
    }

    [Fact]
    public async Task Stream_LooksTheBookUpOnceForManyRangeRequests()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book());

        for (var i = 0; i < 5; i++)
        {
            var (response, _) = await service.OpenStreamAsync(Session, BookId, $"bytes={i * 100}-", default);
            response.Dispose();
        }

        Assert.Equal(5, player.Opened.Count);
        // 5 stream opens, but the book's details (GetItemAsync) were fetched once.
        Assert.Equal(6, player.BaseUrls.Count);
    }

    [Fact]
    public async Task Stream_CacheIsPerPerson()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book());

        (await service.OpenStreamAsync(Session, BookId, null, default)).Response.Dispose();
        (await service.OpenStreamAsync(new JellyfinSession("other-token", "other-user"), BookId, null, default)).Response.Dispose();

        // Two people: two detail lookups + two stream opens.
        Assert.Equal(4, player.BaseUrls.Count);
    }

    [Theory]
    [InlineData("m4b", "audio/mp4")]
    [InlineData("mp4", "audio/mp4")]
    [InlineData("mp3", "audio/mpeg")]
    [InlineData("flac", "audio/flac")]
    [InlineData("opus", "audio/ogg")]
    public void ContentType_IsWhatBrowsersWant(string container, string expected) => Assert.Equal(expected, ListenService.ContentTypeFor(container, "application/octet-stream"));

    [Fact]
    public void ContentType_UnknownContainerKeepsJellyfinsLabel() => Assert.Equal("audio/x-weird", ListenService.ContentTypeFor("weird", "audio/x-weird"));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1.5, 15_000_000)]
    [InlineData(-5, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Ticks_AreClampedAndFinite(double seconds, long ticks) => Assert.Equal(ticks, ListenService.ToTicks(seconds));

    [Fact]
    public async Task Library_Resume_AsksForResumableNewestFirst()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book());

        var result = await service.GetLibraryAsync(Session, ListenFilter.Resume, null, 0, 20, default);

        var q = player.Queries.Single();
        Assert.True(q.ResumableOnly);
        Assert.Equal("DatePlayed", q.SortBy);
        Assert.True(q.Descending);
        Assert.Single(result.Items);
        Assert.Equal("http://jellyfin", player.BaseUrls.Single());
        Assert.Equal(Session, player.LastSession);
    }

    [Fact]
    public async Task Library_ClampsPagingAndPassesSearch()
    {
        var (service, player, db) = Make();
        using var _ = db;

        await service.GetLibraryAsync(Session, ListenFilter.All, "  dune ", -5, 5000, default);

        var q = player.Queries.Single();
        Assert.Equal("dune", q.SearchTerm);
        Assert.Equal(0, q.StartIndex);
        Assert.Equal(100, q.Limit);
        Assert.Equal("SortName", q.SortBy);
    }

    [Fact]
    public async Task Stream_RefusesBooksThatNeedConverting_WithoutTouchingTheStream()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book(codec: "eac3"));

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.OpenStreamAsync(Session, BookId, null, default));

        Assert.Equal(415, ex.StatusCode);
        Assert.Empty(player.Opened);
    }

    [Fact]
    public async Task Stream_ForwardsRangeAndUsesTheMediaSource()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book());

        var (response, container) = await service.OpenStreamAsync(Session, BookId, "bytes=500-", default);
        response.Dispose();

        var (path, range) = player.Opened.Single();
        Assert.Equal($"/Audio/{BookId}/stream?static=true&MediaSourceId=ms1", path);
        Assert.Equal("bytes=500-", range);
        Assert.Equal("m4b", container);
    }

    [Fact]
    public async Task Stream_UnknownBook_Is404()
    {
        var (service, _, db) = Make();
        using var _ = db;

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.OpenStreamAsync(Session, BookId, null, default));

        Assert.Equal(404, ex.StatusCode);
    }

    [Theory]
    [InlineData("start", "Playing")]
    [InlineData("stop", "Stopped")]
    [InlineData("progress", "Progress")]
    [InlineData(null, "Progress")]
    [InlineData("gibberish", "Progress")]
    public async Task Progress_MapsEventsAndSecondsToTicks(string? evt, string kind)
    {
        var (service, player, db) = Make();
        using var _ = db;

        await service.ReportProgressAsync(Session, BookId, "ms1", new ProgressRequest(12.5, true, evt, "ps"), default);

        var report = player.Reports.Single();
        Assert.Equal(kind, report.Kind);
        Assert.Equal(125_000_000, report.PositionTicks);
        Assert.True(report.IsPaused);
        Assert.Equal("ms1", report.MediaSourceId);
    }

    [Fact]
    public async Task JellyfinNotConfigured_Is503()
    {
        var (service, _, db) = Make();
        using var _ = db;
        db.CurrentSettings.JellyfinUrl = null;
        await db.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.GetLibraryAsync(Session, ListenFilter.All, null, 0, 10, default));

        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task RejectedToken_Propagates()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Unauthorized = true;

        await Assert.ThrowsAsync<JellyfinUnauthorizedException>(() => service.GetLibraryAsync(Session, ListenFilter.All, null, 0, 10, default));
    }

    [Fact]
    public async Task Played_IsRelayed()
    {
        var (service, player, db) = Make();
        using var _ = db;

        await service.SetPlayedAsync(Session, BookId, true, default);
        await service.SetPlayedAsync(Session, BookId, false, default);

        Assert.Equal([(BookId, true), (BookId, false)], player.PlayedChanges);
    }
}

public class SessionClaimsTests
{
    [Fact]
    public void LoginClaims_RoundTripTheJellyfinSession()
    {
        var user = new Core.Entities.AppUser { Id = 7, JellyfinUserId = "j", Username = "victoria", Role = Core.Entities.UserRole.User };
        var auth = new JellyfinAuthResult { AccessToken = "secret", User = new JellyfinUser { Id = "jf-1", Name = "victoria" } };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(SessionClaims.Build(user, auth, new JellyfinDevice("abr-0123456789abcdef01234567", "Safari on iPhone")), "test"));

        Assert.Equal("7", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(new JellyfinSession("secret", "jf-1", "abr-0123456789abcdef01234567", "Safari on iPhone"), SessionClaims.JellyfinSessionOf(principal));
    }

    [Fact]
    public void Version090Sessions_FallBackToTheDefaultDevice()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(SessionClaims.JellyfinToken, "t"), new Claim(SessionClaims.JellyfinUserId, "u")], "test"));

        Assert.Equal(new JellyfinSession("t", "u"), SessionClaims.JellyfinSessionOf(principal));
    }

    [Fact]
    public void OlderSessions_HaveNoJellyfinSession()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "test"));

        Assert.Null(SessionClaims.JellyfinSessionOf(principal));
    }
}
