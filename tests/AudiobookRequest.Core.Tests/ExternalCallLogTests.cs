using System.Net;
using System.Net.Sockets;
using AudiobookRequest.Core.Clients;
using Microsoft.Extensions.Logging;

namespace AudiobookRequest.Core.Tests;

public class ExternalCallLogTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw ex;
    }

    private static HttpRequestException DnsFailure() =>
        new("No such host is known. (api.audible.com:443)", new SocketException((int)SocketError.HostNotFound));

    [Fact]
    public void Describe_NamesADnsFailureAndWhatToCheck()
    {
        var reason = ExternalCallLog.Describe(DnsFailure());

        Assert.NotNull(reason);
        Assert.Contains("DNS lookup failed", reason);
        Assert.Contains("this server can't resolve the host", reason);
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, "connection refused")]
    [InlineData(SocketError.TimedOut, "connection timed out")]
    [InlineData(SocketError.NetworkUnreachable, "network unreachable")]
    public void Describe_NamesOtherSocketFailures(SocketError error, string expected) =>
        Assert.Contains(expected, ExternalCallLog.Describe(new HttpRequestException("x", new SocketException((int)error))));

    [Fact]
    public void Describe_ReportsAnHttpStatus() =>
        Assert.Equal("the service answered HTTP 502 (BadGateway)", ExternalCallLog.Describe(new HttpRequestException("x", null, HttpStatusCode.BadGateway)));

    [Fact]
    public void Describe_ReportsATimeout()
    {
        Assert.Equal("timed out", ExternalCallLog.Describe(new TaskCanceledException("t", new TimeoutException())));
        Assert.Equal("timed out", ExternalCallLog.Describe(new TimeoutException()));
    }

    [Fact]
    public void Describe_LeavesRealBugsAlone()
    {
        Assert.Null(ExternalCallLog.Describe(new InvalidOperationException("bug")));
        Assert.Null(ExternalCallLog.Describe(new NullReferenceException()));
    }

    [Fact]
    public void LogFailure_OnANetworkProblem_IsOneLineWithNoStackTrace()
    {
        var log = new CapturingLogger<ExternalCallLogTests>();

        log.LogFailure(DnsFailure(), "Audible browse failed (category {Category})", "123");

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains("Audible browse failed (category 123): DNS lookup failed", entry.Message);
    }

    [Fact]
    public void LogFailure_OnAnythingElse_KeepsTheFullException()
    {
        var log = new CapturingLogger<ExternalCallLogTests>();
        var bug = new InvalidOperationException("bug");

        log.LogFailure(bug, "Audible browse failed (category {Category})", "123");

        var entry = Assert.Single(log.Entries);
        Assert.Same(bug, entry.Exception);
        Assert.Equal("Audible browse failed (category 123)", entry.Message);
    }

    [Fact]
    public async Task Browse_WhenTheHostCannotBeResolved_ReturnsNothingAndLogsOneShortLine()
    {
        var log = new CapturingLogger<AudiobookMetadataClient>();
        var client = new AudiobookMetadataClient(new HttpClient(new ThrowingHandler(DnsFailure())), log);

        var page = await client.BrowseAsync("18580616011", null, "Relevance", 1, 10);

        Assert.Empty(page.Items);
        var entry = Assert.Single(log.Entries);
        Assert.Null(entry.Exception);
        Assert.Contains("DNS lookup failed", entry.Message);
    }

    [Fact]
    public async Task SeriesAndMetadataLookups_AlsoLogOneShortLine()
    {
        var log = new CapturingLogger<AudiobookMetadataClient>();
        var client = new AudiobookMetadataClient(new HttpClient(new ThrowingHandler(DnsFailure())), log);

        Assert.Empty(await client.GetSeriesBooksAsync("Moonfall", "Author"));
        Assert.Null(await client.TryResolveDetailAsync("Title", "Author"));

        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, e => Assert.Null(e.Exception));
    }
}
