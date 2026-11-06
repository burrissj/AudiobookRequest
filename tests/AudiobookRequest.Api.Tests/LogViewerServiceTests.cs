using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.Tests;

public sealed class LogViewerServiceTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "abr-logs-" + Guid.NewGuid().ToString("N"));

    public LogViewerServiceTests() => Directory.CreateDirectory(dir);

    private LogViewerService Service() => new(new AppPaths("ffmpeg", "ffprobe", dir));

    private void WriteLog(string name, params string[] lines) => File.WriteAllLines(Path.Combine(dir, name), lines);

    [Fact]
    public void Read_GroupsStackTraceLinesWithTheirEntryAndFiltersByLevel()
    {
        WriteLog("log-20261002.txt",
            "2026-10-02 12:00:00.000 +00:00 [INF] Started",
            "2026-10-02 12:00:01.000 +00:00 [ERR] Failed to process request 7",
            "System.IO.FileNotFoundException: nope",
            "   at Thing.Do()",
            "2026-10-02 12:00:02.000 +00:00 [WRN] Slow poll");

        var errors = Service().Read(null, "error", null, 100);
        var warnings = Service().Read(null, "warning", null, 100);

        var error = Assert.Single(errors.Entries);
        Assert.Contains("FileNotFoundException", error.Text);
        Assert.Contains("at Thing.Do()", error.Text);
        Assert.Equal(2, warnings.Entries.Count);
        Assert.Equal("WRN", warnings.Entries[0].Level); // newest first
    }

    [Fact]
    public void Read_SearchIsCaseInsensitive()
    {
        WriteLog("log-20261002.txt",
            "2026-10-02 12:00:00.000 +00:00 [INF] Library refresh done",
            "2026-10-02 12:00:01.000 +00:00 [INF] Something else");

        var view = Service().Read(null, "all", "LIBRARY", 100);

        Assert.Single(view.Entries);
    }

    [Fact]
    public void Read_DefaultsToTheNewestFileAndOnlyServesLogFiles()
    {
        WriteLog("log-20261001.txt", "2026-10-01 12:00:00.000 +00:00 [INF] old");
        WriteLog("log-20261002.txt", "2026-10-02 12:00:00.000 +00:00 [INF] new");
        File.WriteAllText(Path.Combine(dir, "secrets.txt"), "2026-10-02 12:00:00.000 +00:00 [INF] not a log");

        var newest = Service().Read(null, "all", null, 100);
        var traversal = Service().Read("../secrets.txt", "all", null, 100);

        Assert.Equal("log-20261002.txt", newest.File);
        Assert.Equal(["log-20261002.txt", "log-20261001.txt"], newest.Files);
        Assert.Equal("log-20261002.txt", traversal.File);
    }

    [Fact]
    public void Read_NoLogsYet_ReturnsEmpty()
    {
        var view = Service().Read(null, "all", null, 100);

        Assert.Empty(view.Entries);
        Assert.Empty(view.Files);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup */ }
    }
}
