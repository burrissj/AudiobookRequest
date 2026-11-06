using System.Net;
using AudiobookRequest.Core.Clients;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class JellyfinClientTests
{
    private static JellyfinClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<JellyfinClient>.Instance);

    [Fact]
    public async Task AuthenticateAsync_SendsPascalCaseUsernameAndPw()
    {
        // Regression test: JsonContent.Create on an anonymous type serializes camelCase by default,
        // but Jellyfin's real API requires exact "Username"/"Pw" field names and silently rejects
        // (or, worse, appears to succeed with a malformed body against a lenient mock) anything else.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            { "User": { "Id": "u1", "Name": "alice" }, "AccessToken": "tok" }
            """);
        var client = CreateClient(handler);

        await client.AuthenticateAsync("http://jellyfin:8096", "alice", "secret");

        Assert.NotNull(handler.LastRequestBody);
        var body = handler.LastRequestBody!;
        Assert.Contains("\"Username\":\"alice\"", body);
        Assert.Contains("\"Pw\":\"secret\"", body);
        Assert.DoesNotContain("\"username\"", body);
    }

    [Fact]
    public async Task AuthenticateAsync_UsesLowercasePathAndStandardAuthorizationHeader()
    {
        // Regression test: confirmed against a real Jellyfin 12.1.0 server that the legacy
        // "/Users/AuthenticateByName" path + "X-Emby-Authorization" header (correct for older
        // Jellyfin/Emby) gets rejected with a generic 400, while its own web client uses the
        // lowercase path and the standard "Authorization" header.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            { "User": { "Id": "u1", "Name": "alice" }, "AccessToken": "tok" }
            """);
        var client = CreateClient(handler);

        await client.AuthenticateAsync("http://jellyfin:8096", "alice", "secret");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("http://jellyfin:8096/Users/authenticatebyname", handler.LastRequest!.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.Contains("Authorization"));
        Assert.False(handler.LastRequest.Headers.Contains("X-Emby-Authorization"));
        Assert.Contains("MediaBrowser", handler.LastRequest.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task AuthenticateAsync_WithADevice_SendsItsIdAndName()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"AccessToken":"t","User":{"Id":"u","Name":"alice"}}""");
        var client = new JellyfinClient(new HttpClient(handler), NullLogger<JellyfinClient>.Instance);

        await client.AuthenticateAsync("http://jellyfin:8096", "alice", "secret", default, new JellyfinDevice("abr-0123456789abcdef01234567", "Safari on iPhone"));

        var header = handler.LastRequest!.Headers.GetValues("Authorization").Single();
        Assert.Contains("DeviceId=\"abr-0123456789abcdef01234567\"", header);
        Assert.Contains("Device=\"Safari on iPhone\"", header);
    }
}
