using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

namespace AudiobookRequest.Api.Tests;

/// <summary>Guards the installable-app files: the web server must serve the manifest as one, and the manifest must point at real icons.</summary>
public class PwaAssetTests
{
    private static readonly string PublicDir = FindClientPublicDir();

    private static string FindClientPublicDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "client", "public")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("client/public not found"), "client", "public");
    }

    [Theory]
    [InlineData("manifest.webmanifest", "application/manifest+json")]
    [InlineData("sw.js", "text/javascript")]
    [InlineData("icons/icon-192.png", "image/png")]
    public void StaticFileServer_KnowsTheContentTypes(string file, string expected)
    {
        var provider = new FileExtensionContentTypeProvider();

        Assert.True(provider.TryGetContentType(file, out var contentType));
        Assert.StartsWith(expected, contentType);
    }

    [Fact]
    public void Manifest_IsInstallableAndPointsAtRealPngIconsOfTheDeclaredSize()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(PublicDir, "manifest.webmanifest")));
        var root = manifest.RootElement;

        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("name").GetString()));

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512");

        foreach (var icon in icons)
        {
            var path = Path.Combine(PublicDir, icon.GetProperty("src").GetString()!.TrimStart('/'));
            Assert.True(File.Exists(path), $"{path} is missing");
            var size = icon.GetProperty("sizes").GetString()!.Split('x').Select(int.Parse).ToArray();
            using var png = File.OpenRead(path);
            var header = new byte[24];
            Assert.Equal(24, png.Read(header, 0, 24));
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, header[..4]); // PNG signature
            Assert.Equal(size[0], (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19]); // IHDR width
            Assert.Equal(size[1], (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23]); // IHDR height
        }
    }

    [Fact]
    public void ServiceWorker_NeverCachesApiTraffic()
    {
        var script = File.ReadAllText(Path.Combine(PublicDir, "sw.js"));

        Assert.Contains("/api/", script);
        Assert.Contains("/hubs/", script);
        Assert.Contains("request.method !== 'GET'", script);
    }

    [Fact]
    public void IndexHtml_LinksTheManifestAndAppleTouchIcon()
    {
        var html = File.ReadAllText(Path.Combine(PublicDir, "..", "index.html"));

        Assert.Contains("rel=\"manifest\"", html);
        Assert.Contains("apple-touch-icon", html);
        Assert.Contains("viewport-fit=cover", html);
    }
}
