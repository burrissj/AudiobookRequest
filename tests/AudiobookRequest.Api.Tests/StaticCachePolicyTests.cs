using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.Tests;

public class StaticCachePolicyTests
{
    [Theory]
    [InlineData("/", "no-cache")]
    [InlineData("/index.html", "no-cache")]
    [InlineData("/sw.js", "no-cache")]
    [InlineData("/manifest.webmanifest", "no-cache")]
    [InlineData("/favicon.svg", "no-cache")]
    [InlineData("/listen", "no-cache")]
    [InlineData(null, "no-cache")]
    public void ThePageAndServiceWorkerAreAlwaysRevalidated(string? path, string expected) => Assert.Equal(expected, StaticCachePolicy.For(path));

    [Theory]
    [InlineData("/assets/index-u0BqEr55.js")]
    [InlineData("/assets/hls-D9b4QHpD.js")]
    [InlineData("/ASSETS/index-2_C5g7Fv.css")]
    public void HashedBuildFilesAreKeptForever(string path)
    {
        var value = StaticCachePolicy.For(path);

        Assert.Contains("immutable", value);
        Assert.Contains("max-age=31536000", value);
    }

    [Fact]
    public void IconsAreKeptForADay() => Assert.Equal("public, max-age=86400", StaticCachePolicy.For("/icons/icon-192.png"));
}
