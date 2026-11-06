using System.Net;
using AudiobookRequest.Core.Clients;

namespace AudiobookRequest.Core.Tests;

public class JellyfinPlayerClientTests
{
    private static readonly JellyfinSession Session = new("tok-123", "user-abc");

    private static (JellyfinPlayerClient Client, FakeHttpMessageHandler Handler) Make(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpMessageHandler(status, body);
        return (new JellyfinPlayerClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task GetItems_AsksForTheUsersOwnAudiobooks_WithTheirToken()
    {
        var (client, handler) = Make(HttpStatusCode.OK, """{"Items":[{"Id":"a1","Name":"Dune","RunTimeTicks":36000000000,"UserData":{"PlaybackPositionTicks":6000000000,"Played":false}}],"TotalRecordCount":1}""");

        var result = await client.GetItemsAsync("http://jf/", Session, new JellyfinItemQuery(ResumableOnly: true, SearchTerm: "du ne", SortBy: "DatePlayed", Descending: true, StartIndex: 5, Limit: 10));

        var url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        Assert.StartsWith("http://jf/Users/user-abc/Items?", url);
        Assert.Contains("IncludeItemTypes=AudioBook", url);
        Assert.Contains("Filters=IsResumable", url);
        Assert.Contains("SearchTerm=du%20ne", url);
        Assert.Contains("SortOrder=Descending", url);
        Assert.Contains("StartIndex=5", url);
        Assert.Contains("Limit=10", url);
        Assert.Contains("Token=\"tok-123\"", handler.LastRequest.Headers.GetValues("Authorization").Single());
        Assert.Equal(1, result.TotalRecordCount);
        Assert.Equal(6000000000, result.Items[0].UserData!.PlaybackPositionTicks);
    }

    [Fact]
    public async Task GetItems_WithoutFilters_OmitsThem()
    {
        var (client, handler) = Make(HttpStatusCode.OK, """{"Items":[],"TotalRecordCount":0}""");

        await client.GetItemsAsync("http://jf", Session, new JellyfinItemQuery());

        var url = handler.LastRequest!.RequestUri!.ToString();
        Assert.DoesNotContain("IsResumable", url);
        Assert.DoesNotContain("SearchTerm", url);
        Assert.Contains("SortOrder=Ascending", url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedToken_ThrowsUnauthorized(HttpStatusCode status)
    {
        var (client, _) = Make(status, "");

        await Assert.ThrowsAsync<JellyfinUnauthorizedException>(() => client.GetItemsAsync("http://jf", Session, new JellyfinItemQuery()));
        await Assert.ThrowsAsync<JellyfinUnauthorizedException>(() => client.GetItemAsync("http://jf", Session, "a1"));
        await Assert.ThrowsAsync<JellyfinUnauthorizedException>(() => client.OpenAsync("http://jf", Session, "/Audio/a1/stream", null));
    }

    [Fact]
    public async Task GetItem_MissingBook_IsNull()
    {
        var (client, _) = Make(HttpStatusCode.NotFound, "");

        Assert.Null(await client.GetItemAsync("http://jf", Session, "nope"));
    }

    [Fact]
    public async Task GetItem_ReadsChaptersAndMediaSources()
    {
        var (client, _) = Make(HttpStatusCode.OK, """
            {"Id":"a1","Name":"Dune","Chapters":[{"StartPositionTicks":0,"Name":"Opening"},{"StartPositionTicks":6000000000,"Name":"Two"}],
             "MediaSources":[{"Id":"ms1","Container":"m4b","MediaStreams":[{"Type":"Audio","Codec":"aac"}]}]}
            """);

        var item = await client.GetItemAsync("http://jf", Session, "a1");

        Assert.Equal(2, item!.Chapters!.Count);
        Assert.Equal("aac", item.MediaSources![0].MediaStreams![0].Codec);
    }

    [Fact]
    public async Task Open_ForwardsRangeHeader()
    {
        var (client, handler) = Make(HttpStatusCode.PartialContent, "x");

        using var response = await client.OpenAsync("http://jf", Session, "/Audio/a1/stream?static=true", "bytes=100-");

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes=100-", handler.LastRequest!.Headers.GetValues("Range").Single());
        Assert.Equal("http://jf/Audio/a1/stream?static=true", handler.LastRequest.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("Playing", "/Sessions/Playing")]
    [InlineData("Progress", "/Sessions/Playing/Progress")]
    [InlineData("Stopped", "/Sessions/Playing/Stopped")]
    public async Task ReportPlayback_PostsToTheRightEndpoint(string kind, string path)
    {
        var (client, handler) = Make(HttpStatusCode.NoContent, "");

        await client.ReportPlaybackAsync("http://jf", Session, new JellyfinPlaybackReport(kind, "a1", null, 123, true, "ps-1"));

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://jf" + path, handler.LastRequest.RequestUri!.ToString());
        var body = handler.LastRequestBody!;
        Assert.Contains("\"ItemId\":\"a1\"", body);
        Assert.Contains("\"MediaSourceId\":\"a1\"", body);
        Assert.Contains("\"PositionTicks\":123", body);
        Assert.Contains("\"IsPaused\":true", body);
        Assert.Contains("\"PlaySessionId\":\"ps-1\"", body);
    }

    [Fact]
    public async Task SetPlayed_UsesPostToMarkAndDeleteToUnmark()
    {
        var (client, handler) = Make(HttpStatusCode.OK, "{}");

        await client.SetPlayedAsync("http://jf", Session, "a1", true);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://jf/Users/user-abc/PlayedItems/a1", handler.LastRequest.RequestUri!.ToString());

        await client.SetPlayedAsync("http://jf", Session, "a1", false);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
    }

    [Fact]
    public async Task Requests_IdentifyAsTheDeviceTheTokenWasIssuedTo()
    {
        var (client, handler) = Make(HttpStatusCode.OK, """{"Items":[],"TotalRecordCount":0}""");
        var session = new JellyfinSession("tok-123", "user-abc", "abr-0123456789abcdef01234567", "Safari on iPhone");

        await client.GetItemsAsync("http://jf", session, new JellyfinItemQuery());

        var header = handler.LastRequest!.Headers.GetValues("Authorization").Single();
        Assert.Contains("DeviceId=\"abr-0123456789abcdef01234567\"", header);
        Assert.Contains("Device=\"Safari on iPhone\"", header);
        Assert.EndsWith("Token=\"tok-123\"", header);
    }

    [Fact]
    public async Task Requests_WithoutADevice_UseTheOriginalDefaults()
    {
        var (client, handler) = Make(HttpStatusCode.OK, """{"Items":[],"TotalRecordCount":0}""");

        await client.GetItemsAsync("http://jf", Session, new JellyfinItemQuery());

        Assert.Contains("DeviceId=\"audiobookrequest-web\"", handler.LastRequest!.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public void DeviceNames_CannotBreakOutOfTheHeader()
    {
        var header = JellyfinHeaders.ForLogin(new JellyfinDevice("id\", Token=\"x", "Evil\", Token=\"y\r\nX-Injected: 1"));

        Assert.DoesNotContain("\r", header);
        Assert.DoesNotContain("\n", header);
        // Exactly the four quoted fields we wrote -- nothing smuggled in.
        Assert.Equal(8, header.Count(c => c == '"'));
    }

    [Fact]
    public async Task GetItems_PlayedOnlyAsksForFinishedBooks()
    {
        var (client, handler) = Make(HttpStatusCode.OK, """{"Items":[],"TotalRecordCount":0}""");

        await client.GetItemsAsync("http://jf", Session, new JellyfinItemQuery(PlayedOnly: true));

        Assert.Contains("Filters=IsPlayed", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Logout_PostsToSessionsLogout()
    {
        var (client, handler) = Make(HttpStatusCode.NoContent, "");

        await client.LogoutAsync("http://jf", Session);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://jf/Sessions/Logout", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task Logout_AnAlreadyExpiredTokenIsFine()
    {
        var (client, _) = Make(HttpStatusCode.Unauthorized, "");

        await client.LogoutAsync("http://jf", Session);
    }

    [Fact]
    public async Task Logout_ServerErrorsSurface()
    {
        var (client, _) = Make(HttpStatusCode.InternalServerError, "");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.LogoutAsync("http://jf", Session));
    }

    [Fact]
    public async Task StopTranscode_DeletesTheDevicesEncoding()
    {
        var (client, handler) = Make(HttpStatusCode.NoContent, "");

        await client.StopTranscodeAsync("http://jf", new JellyfinSession("t", "u", "abr-1", "Chrome"), "ps 1");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("http://jf/Videos/ActiveEncodings?deviceId=abr-1&playSessionId=ps%201", handler.LastRequest.RequestUri!.AbsoluteUri);
    }
}
