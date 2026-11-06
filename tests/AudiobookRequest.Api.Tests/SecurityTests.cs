using System.Net;
using AudiobookRequest.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace AudiobookRequest.Api.Tests;

file sealed class ManualClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan by) => now += by;
}

public class LoginThrottleTests
{
    private static void Fail(LoginThrottle t, string address, string user, int times)
    {
        for (var i = 0; i < times; i++)
        {
            t.RecordFailure(address, user);
        }
    }

    [Fact]
    public void ANormalPersonMistypingNowAndThenIsNeverStopped()
    {
        var t = new LoginThrottle(new ManualClock());

        Fail(t, "10.0.0.5", "sam", LoginThrottle.MaxPerAddressAndUser - 1);

        Assert.Null(t.BlockedFor("10.0.0.5", "sam"));
    }

    [Fact]
    public void RepeatedFailuresFromOnePlaceOnOneAccountAreRefusedForAWhile()
    {
        var clock = new ManualClock();
        var t = new LoginThrottle(clock);
        Fail(t, "203.0.113.9", "sam", LoginThrottle.MaxPerAddressAndUser);

        var wait = t.BlockedFor("203.0.113.9", "sam");

        Assert.NotNull(wait);
        Assert.InRange(wait!.Value, TimeSpan.FromMinutes(14), LoginThrottle.Window);
    }

    [Fact]
    public void ItLetsYouInAgainOnceTheWaitIsOver()
    {
        var clock = new ManualClock();
        var t = new LoginThrottle(clock);
        Fail(t, "203.0.113.9", "sam", LoginThrottle.MaxPerAddressAndUser);

        clock.Advance(LoginThrottle.Window + TimeSpan.FromSeconds(1));

        Assert.Null(t.BlockedFor("203.0.113.9", "sam"));
    }

    [Fact]
    public void ItLetsYouBackInGraduallyAsTheOldestFailuresAgeOut()
    {
        var clock = new ManualClock();
        var t = new LoginThrottle(clock);
        t.RecordFailure("1.1.1.1", "sam");
        clock.Advance(TimeSpan.FromMinutes(10));
        Fail(t, "1.1.1.1", "sam", LoginThrottle.MaxPerAddressAndUser - 1);   // five in the window now
        Assert.NotNull(t.BlockedFor("1.1.1.1", "sam"));

        clock.Advance(TimeSpan.FromMinutes(5.5));   // the first one has aged out, leaving four

        Assert.Null(t.BlockedFor("1.1.1.1", "sam"));
    }

    [Fact]
    public void AStrangerFailingAgainstYourNameDoesNotLockYouOutFromYourOwnPhone()
    {
        var t = new LoginThrottle(new ManualClock());
        Fail(t, "203.0.113.9", "sam", LoginThrottle.MaxPerAddressAndUser);   // the stranger fills only their own allowance

        Assert.NotNull(t.BlockedFor("203.0.113.9", "sam"));
        Assert.Null(t.BlockedFor("198.51.100.7", "sam"));   // sam, from elsewhere, is fine
    }

    [Fact]
    public void GuessingAtOneAccountFromManyAddressesIsStoppedToo()
    {
        var t = new LoginThrottle(new ManualClock());
        for (var i = 0; i < LoginThrottle.MaxPerUser; i++)
        {
            t.RecordFailure($"198.51.100.{i}", "sam");
        }

        Assert.NotNull(t.BlockedFor("198.51.100.200", "sam"));
        Assert.Null(t.BlockedFor("198.51.100.200", "someone-else"));
    }

    [Fact]
    public void WorkingThroughManyUsernamesFromOneAddressIsStoppedToo()
    {
        var t = new LoginThrottle(new ManualClock());
        for (var i = 0; i < LoginThrottle.MaxPerAddress; i++)
        {
            t.RecordFailure("203.0.113.9", $"user{i}");
        }

        Assert.NotNull(t.BlockedFor("203.0.113.9", "a-fresh-name"));
        Assert.Null(t.BlockedFor("198.51.100.1", "a-fresh-name"));
    }

    [Fact]
    public void ACorrectPasswordClearsEarlierTyposAgainstThatAccount()
    {
        var t = new LoginThrottle(new ManualClock());
        Fail(t, "10.0.0.5", "sam", LoginThrottle.MaxPerAddressAndUser - 1);

        t.RecordSuccess("10.0.0.5", "sam");
        Fail(t, "10.0.0.5", "sam", LoginThrottle.MaxPerAddressAndUser - 1);

        Assert.Null(t.BlockedFor("10.0.0.5", "sam"));
    }

    [Fact]
    public void UsernamesAreComparedWithoutRegardToCaseOrSpaces()
    {
        var t = new LoginThrottle(new ManualClock());
        Fail(t, "203.0.113.9", "Sam", LoginThrottle.MaxPerAddressAndUser);

        Assert.NotNull(t.BlockedFor("203.0.113.9", " sam "));
    }

    [Fact]
    public void ItDoesNotGrowWithoutLimitWhenSomeoneTriesEndlessUsernames()
    {
        var clock = new ManualClock();
        var t = new LoginThrottle(clock);

        // Far more distinct usernames than it will remember: it must shed the old ones rather than keep them all.
        for (var i = 0; i < 30_000; i++)
        {
            t.RecordFailure("203.0.113.9", $"user-{i}");
        }

        // still working: the flooding address is blocked, a newcomer is not
        Assert.NotNull(t.BlockedFor("203.0.113.9", "anyone"));
        Assert.Null(t.BlockedFor("198.51.100.1", "anyone"));
        // and everything ages out as normal afterwards
        clock.Advance(LoginThrottle.Window + TimeSpan.FromMinutes(1));
        Assert.Null(t.BlockedFor("203.0.113.9", "anyone"));
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(40, "40 seconds")]
    [InlineData(90, "2 minutes")]
    [InlineData(60 * 15, "15 minutes")]
    public void ItSaysHowLongToWaitInPlainWords(int seconds, string expected) =>
        Assert.Equal(expected, LoginThrottle.Describe(TimeSpan.FromSeconds(seconds)));
}

public class SetupAccessTests
{
    [Theory]
    [InlineData("192.168.1.50")]
    [InlineData("10.0.0.9")]
    [InlineData("172.20.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("100.100.5.5")]          // Tailscale
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:192.168.1.50")]  // a home address, wrapped the way a dual-stack server reports it
    public void HomeAndVpnAddressesCountAsHome(string address) => Assert.True(SetupAccess.IsHomeNetwork(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.113.9")]
    [InlineData("172.32.0.1")]           // just outside 172.16/12
    [InlineData("100.128.0.1")]          // just outside Tailscale's range
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void PublicAddressesDoNot(string address) => Assert.False(SetupAccess.IsHomeNetwork(IPAddress.Parse(address)));

    [Fact]
    public void AnUnknownAddressIsNotTrusted() => Assert.False(SetupAccess.IsHomeNetwork(null));

    [Fact]
    public void BeforeSetupItIsOpenAtHomeAndShutToTheInternet()
    {
        Assert.Null(SetupAccess.Denial(setupCompleted: false, isAdmin: false, IPAddress.Parse("192.168.1.20")));
        Assert.NotNull(SetupAccess.Denial(setupCompleted: false, isAdmin: false, IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public void AfterSetupOnlyAnAdminMayUseItWhereverTheyAre()
    {
        Assert.Null(SetupAccess.Denial(setupCompleted: true, isAdmin: true, IPAddress.Parse("203.0.113.9")));
        Assert.Contains("Sign in as an admin", SetupAccess.Denial(setupCompleted: true, isAdmin: false, IPAddress.Parse("192.168.1.20")));
    }
}

public class SecurityHeadersTests
{
    private static SecurityOptions Default() => SecurityOptions.From(new ConfigurationBuilder().Build());

    private static async Task<(HttpContext Context, bool ReachedApp)> Send(string method, string path, Action<HttpContext>? shape = null, SecurityOptions? options = null)
    {
        var reached = false;
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseSecurityHeaders(options ?? Default());
        app.Run(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("books.example.net");
        shape?.Invoke(context);
        await app.Build()(context);
        return (context, reached);
    }

    [Fact]
    public async Task EveryResponseCarriesTheProtectiveHeaders()
    {
        var (c, _) = await Send("GET", "/");
        var h = c.Response.Headers;

        Assert.Equal("nosniff", h["X-Content-Type-Options"]);
        Assert.Equal("DENY", h["X-Frame-Options"]);
        Assert.Equal("same-origin", h["Referrer-Policy"]);
        Assert.Contains("camera=()", h["Permissions-Policy"].ToString());
        Assert.Contains("frame-ancestors 'none'", h["Content-Security-Policy"].ToString());
        Assert.Contains("object-src 'none'", h["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task ThePolicyAllowsWhatTheAppActuallyNeeds()
    {
        var (c, _) = await Send("GET", "/");
        var csp = c.Response.Headers["Content-Security-Policy"].ToString();

        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe", csp);   // no inline or eval'd scripts
        Assert.Contains("img-src 'self' data: blob: https:", csp);  // book covers come from the catalog's hosts
        Assert.Contains("media-src 'self' blob:", csp);            // audio, and books saved on the device
        Assert.Contains("connect-src 'self' ws: wss:", csp);       // the live-update connection
    }

    [Fact]
    public async Task HstsIsSentOverHttpsOnly()
    {
        var (secure, _) = await Send("GET", "/");
        var (plain, _) = await Send("GET", "/", c => c.Request.Scheme = "http");

        Assert.Equal("max-age=15552000", secure.Response.Headers["Strict-Transport-Security"]);
        Assert.False(plain.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task HstsAndThePolicyCanBeSwitchedOff()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:Hsts"] = "false",
            ["Security:ContentSecurityPolicy"] = "",
        }).Build();

        var (c, _) = await Send("GET", "/", options: SecurityOptions.From(config));

        Assert.False(c.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.False(c.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.True(c.Response.Headers.ContainsKey("X-Content-Type-Options"));   // the rest stay
    }

    [Fact]
    public async Task APolicyCanBeReplacedFromConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:ContentSecurityPolicy"] = "default-src 'none'" }).Build();

        var (c, _) = await Send("GET", "/", options: SecurityOptions.From(config));

        Assert.Equal("default-src 'none'", c.Response.Headers["Content-Security-Policy"]);
    }

    [Fact]
    public async Task SigningInAndOutIsNeverCached()
    {
        var (auth, _) = await Send("GET", "/api/auth/me");
        var (other, _) = await Send("GET", "/api/books/requests");

        Assert.Equal("no-store", auth.Response.Headers.CacheControl);
        Assert.False(other.Response.Headers.ContainsKey("Cache-Control"));
    }

    // ---- other websites posting to the app ----

    [Fact]
    public async Task APostFromTheAppsOwnPageIsAllowed()
    {
        var (c, reached) = await Send("POST", "/api/books/requests", c => c.Request.Headers.Origin = "https://books.example.net");

        Assert.True(reached);
        Assert.NotEqual(403, c.Response.StatusCode);
    }

    [Fact]
    public async Task APostFromAnotherWebsiteIsRefusedBeforeItReachesTheApp()
    {
        var (c, reached) = await Send("POST", "/api/books/requests", c => c.Request.Headers.Origin = "https://evil.example.org");

        Assert.False(reached);
        Assert.Equal(403, c.Response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task ThatGoesForEveryMethodThatChangesThings(string method)
    {
        var (c, reached) = await Send(method, "/api/admin/x", c => c.Request.Headers.Origin = "https://evil.example.org");

        Assert.False(reached);
        Assert.Equal(403, c.Response.StatusCode);
    }

    [Fact]
    public async Task ReadingFromAnotherSiteIsNotWhatThisCheckIsFor()
    {
        var (_, reached) = await Send("GET", "/api/books/requests", c => c.Request.Headers.Origin = "https://evil.example.org");

        Assert.True(reached);
    }

    [Fact]
    public async Task AnOriginOfNullFromASandboxedPageIsRefused()
    {
        var (c, reached) = await Send("POST", "/api/auth/login", c => c.Request.Headers.Origin = "null");

        Assert.False(reached);
        Assert.Equal(403, c.Response.StatusCode);
    }

    [Fact]
    public async Task ARequestWithNoOriginIsLeftAlone()
    {
        var (_, reached) = await Send("POST", "/api/auth/login");

        Assert.True(reached);
    }

    [Fact]
    public async Task ARequestThatTheBrowserSaysIsCrossSiteIsRefusedEvenWithNoOrigin()
    {
        var (c, reached) = await Send("POST", "/api/auth/login", c => c.Request.Headers["Sec-Fetch-Site"] = "cross-site");

        Assert.False(reached);
        Assert.Equal(403, c.Response.StatusCode);
    }

    [Theory]
    [InlineData("http://192.168.1.113:5230", "192.168.1.113", 5230, true)]    // straight to the app on the home network
    [InlineData("http://192.168.1.113:5230", "192.168.1.113", 5231, false)]   // another port named outright on both sides
    [InlineData("https://books.example.net", "books.example.net", null, true)] // behind the proxy: no port on either side
    [InlineData("https://BOOKS.example.net", "books.example.net", null, true)] // case never matters
    [InlineData("https://books.example.net", "books.example.net", 5230, true)] // a proxy hiding the real port is not a mismatch
    [InlineData("https://books.example.net.evil.org", "books.example.net", null, false)]
    public async Task TheOriginMustBeTheSiteThePageWasLoadedFrom(string origin, string host, int? port, bool allowed)
    {
        var (_, reached) = await Send("POST", "/api/x", c =>
        {
            c.Request.Headers.Origin = origin;
            c.Request.Host = port is null ? new HostString(host) : new HostString(host, port.Value);
        });

        Assert.Equal(allowed, reached);
    }

    [Fact]
    public async Task TheCrossSiteCheckCanBeSwitchedOff()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:CheckOrigin"] = "false" }).Build();

        var (_, reached) = await Send("POST", "/api/x", c => c.Request.Headers.Origin = "https://evil.example.org", SecurityOptions.From(config));

        Assert.True(reached);
    }
}
