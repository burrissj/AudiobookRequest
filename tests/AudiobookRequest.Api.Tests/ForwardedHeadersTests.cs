using System.Net;
using AudiobookRequest.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AudiobookRequest.Api.Tests;

/// <summary>Runs the real forwarded-headers middleware with the app's settings, to prove an HTTPS reverse proxy is believed -- and a stranger is not.</summary>
public class ForwardedHeadersTests
{
    private static async Task<HttpContext> Send(string fromAddress, string? forwardedProto, bool trustAny = false)
    {
        var options = ForwardedHeadersSetup.Create(trustAny);
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("192.168.1.113", 5230);
        context.Connection.RemoteIpAddress = IPAddress.Parse(fromAddress);
        if (forwardedProto is not null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            context.Request.Headers["X-Forwarded-Host"] = "books.example.com";
            context.Request.Headers["X-Forwarded-For"] = "192.168.1.77";
        }
        await middleware.Invoke(context);
        return context;
    }

    [Theory]
    [InlineData("192.168.1.35")]   // the Nginx Proxy Manager host in this setup
    [InlineData("192.168.50.2")]
    [InlineData("10.20.30.40")]
    [InlineData("172.16.5.9")]
    [InlineData("172.31.255.1")]
    [InlineData("127.0.0.1")]
    [InlineData("fd12:3456:789a::1")]
    public async Task AProxyOnAPrivateNetwork_IsBelieved_SoRequestsCountAsHttps(string proxy)
    {
        var context = await Send(proxy, "https");

        Assert.Equal("https", context.Request.Scheme);
        Assert.True(context.Request.IsHttps);
        Assert.Equal("books.example.com", context.Request.Host.Value);
        Assert.Equal(IPAddress.Parse("192.168.1.77"), context.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.113.9")]
    [InlineData("172.32.0.1")]     // just outside 172.16.0.0/12
    [InlineData("2606:4700:4700::1111")]
    public async Task ASourceOnThePublicInternet_IsNeverBelieved(string stranger)
    {
        var context = await Send(stranger, "https");

        Assert.Equal("http", context.Request.Scheme);
        Assert.False(context.Request.IsHttps);
        Assert.Equal("192.168.1.113:5230", context.Request.Host.Value);
        Assert.Equal(IPAddress.Parse(stranger), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task ADirectLanRequestWithNoProxyHeaders_StaysPlainHttp()
    {
        var context = await Send("192.168.1.44", forwardedProto: null);

        Assert.False(context.Request.IsHttps);
        Assert.Equal("192.168.1.113:5230", context.Request.Host.Value);
    }

    [Fact]
    public async Task TrustAnyProxy_BelievesEvenAPublicAddress()
    {
        var context = await Send("203.0.113.9", "https", trustAny: true);

        Assert.True(context.Request.IsHttps);
    }
}
