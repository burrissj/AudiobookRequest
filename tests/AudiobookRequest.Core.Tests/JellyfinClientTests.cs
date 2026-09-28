using System.Net;
using AudiobookRequest.Core.Clients;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class JellyfinClientTests
{
    [Fact]
    public async Task AuthenticateAsync_SendsPascalCaseUsernameAndPw()
    {
        // Regression test: JsonContent.Create on an anonymous type serializes camelCase by default,
        // but Jellyfin's real API requires exact "Username"/"Pw" field names and silently rejects
        // (or, worse, appears to succeed with a malformed body against a lenient mock) anything else.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """
            { "User": { "Id": "u1", "Name": "alice" }, "AccessToken": "tok" }
            """);
        var client = new JellyfinClient(new HttpClient(handler));

        await client.AuthenticateAsync("http://jellyfin:8096", "alice", "secret");

        Assert.NotNull(handler.LastRequestBody);
        var body = handler.LastRequestBody!;
        Assert.Contains("\"Username\":\"alice\"", body);
        Assert.Contains("\"Pw\":\"secret\"", body);
        Assert.DoesNotContain("\"username\"", body);
    }
}
