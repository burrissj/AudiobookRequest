using AudiobookRequest.Api.Services;
using Microsoft.AspNetCore.Http;

namespace AudiobookRequest.Api.Tests;

public class ConnectionCheckTests
{
    private static HttpRequest Request(string scheme, string host, string? forwardedProto = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        if (forwardedProto is not null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        }
        return context.Request;
    }

    [Fact]
    public void Https_IsOkAndNamesTheHost()
    {
        var check = ConnectionCheck.Describe(Request("https", "books.example.com"));

        Assert.Equal("ok", check.Status);
        Assert.Contains("books.example.com", check.Detail);
    }

    [Fact]
    public void PlainHttp_IsNotAProblem_JustNoted()
    {
        var check = ConnectionCheck.Describe(Request("http", "192.168.1.113:5230"));

        Assert.Equal("info", check.Status);
        Assert.Contains("plain HTTP", check.Detail);
    }

    [Fact]
    public void AProxyClaimingHttpsThatWasNotBelieved_IsFlaggedWithTheFix()
    {
        var check = ConnectionCheck.Describe(Request("http", "192.168.1.113:5230", forwardedProto: "https"));

        Assert.Equal("warn", check.Status);
        Assert.Contains("TrustAnyProxy", check.Detail);
    }
}
