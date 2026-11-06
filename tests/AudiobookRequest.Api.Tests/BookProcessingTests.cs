using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Tests;

public sealed class BookProcessingTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeChaptarrClient chaptarr = new();
    private readonly FakeJellyfinClient jellyfin = new();
    private readonly FakeCoverArtDownloader cover = new();
    private readonly FakeAudioCombiner combiner = new();
    private readonly FakeExternalNotifier external = new();
    private readonly FakeNotificationSender push = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "abr-proc-" + Guid.NewGuid().ToString("N"));

    public BookProcessingTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "chaptarr"));
        t.CurrentSettings.StagingPath = Path.Combine(root, "staging");
        t.CurrentSettings.LibraryPath = Path.Combine(root, "library");
        t.CurrentSettings.MinFreeDiskGb = 0;
        t.Db.SaveChanges();
    }

    private BookProcessingService Service() =>
        new(t.Db, t.Settings, chaptarr, jellyfin, cover, combiner, push, external, Nulls.Logger<BookProcessingService>());

    private BookRequest ProcessingRequest(params string[] fileNames)
    {
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Processing, "The Book", r => r.ChaptarrBookId = 42);
        foreach (var name in fileNames)
        {
            var path = Path.Combine(root, "chaptarr", name);
            File.WriteAllText(path, "audio");
            chaptarr.Files.Add(new ChaptarrBookFile(chaptarr.Files.Count + 1, 42, path));
        }
        return request;
    }

    [Fact]
    public async Task FullPipeline_WritesIntoLibraryNotifiesAndRefreshesJellyfin()
    {
        var request = ProcessingRequest("part1.mp3", "part2.mp3");

        await Service().ProcessReadyRequestsAsync(default);

        Assert.Equal(BookRequestStatus.Ready, request.Status);
        Assert.True(File.Exists(Path.Combine(root, "library", "An Author", "The Book", "The Book.m4b")));
        Assert.Equal(1, jellyfin.LibraryRefreshes);
        Assert.Equal(NotificationKind.RequestReady, Assert.Single(external.Sent).Kind);
        Assert.Contains("victoria", external.Sent[0].Message);
        var processed = await t.Db.ProcessedBooks.SingleAsync();
        Assert.False(processed.OriginalDeleted);
        Assert.True(combiner.Calls[0].EmbedTags);
    }

    [Fact]
    public async Task MovingOff_ConversionOn_NeverMarksOriginalsForCleanup()
    {
        t.CurrentSettings.EnableMoving = false;
        t.Db.SaveChanges();
        var request = ProcessingRequest("part1.mp3");

        await Service().ProcessReadyRequestsAsync(default);

        Assert.Equal(BookRequestStatus.Ready, request.Status);
        var processed = await t.Db.ProcessedBooks.SingleAsync();
        Assert.True(processed.OriginalDeleted);
        Assert.True(File.Exists(chaptarr.Files[0].Path));
    }

    [Fact]
    public async Task ConversionOff_SkipsTagsAndCoverArt()
    {
        t.CurrentSettings.EnableConversion = false;
        t.Db.SaveChanges();
        ProcessingRequest("part1.mp3");

        await Service().ProcessReadyRequestsAsync(default);

        Assert.False(combiner.Calls[0].EmbedTags);
        Assert.Null(combiner.Calls[0].Cover);
        Assert.Equal(0, cover.Downloads);
    }

    [Fact]
    public async Task RenamingOff_KeepsChaptarrsFilenameInsideThePatternFolder()
    {
        t.CurrentSettings.EnableRenaming = false;
        t.Db.SaveChanges();
        ProcessingRequest("Original Name.mp3");

        await Service().ProcessReadyRequestsAsync(default);

        Assert.True(File.Exists(Path.Combine(root, "library", "An Author", "The Book", "Original Name.m4b")));
    }

    [Fact]
    public async Task DetectionOnly_MarksReadyWithoutCopyingAnything()
    {
        t.CurrentSettings.EnableMoving = false;
        t.CurrentSettings.EnableConversion = false;
        t.Db.SaveChanges();
        var request = ProcessingRequest("final.m4b");

        await Service().ProcessReadyRequestsAsync(default);

        Assert.Equal(BookRequestStatus.Ready, request.Status);
        Assert.Empty(combiner.Calls);
        Assert.Empty(await t.Db.ProcessedBooks.ToListAsync());
        Assert.Equal(1, jellyfin.LibraryRefreshes);
        Assert.False(Directory.Exists(Path.Combine(root, "library")));
    }

    [Fact]
    public async Task DetectionOnly_WaitsUntilTheFileIsActuallyVisible()
    {
        t.CurrentSettings.EnableMoving = false;
        t.CurrentSettings.EnableConversion = false;
        t.Db.SaveChanges();
        var request = ProcessingRequest("final.m4b");
        File.Delete(chaptarr.Files[0].Path);

        await Service().ProcessReadyRequestsAsync(default);

        Assert.Equal(BookRequestStatus.Processing, request.Status);
        Assert.Equal(0, jellyfin.LibraryRefreshes);
    }

    [Fact]
    public async Task LowDiskSpace_PausesProcessingAndSaysWhy()
    {
        t.CurrentSettings.MinFreeDiskGb = int.MaxValue; // far more than any disk has
        t.Db.SaveChanges();
        var request = ProcessingRequest("part1.mp3");

        await Service().ProcessReadyRequestsAsync(default);

        Assert.Equal(BookRequestStatus.Processing, request.Status);
        Assert.StartsWith("Paused:", request.DiagnosticMessage);
        Assert.Empty(combiner.Calls);

        t.CurrentSettings.MinFreeDiskGb = 0;
        t.Db.SaveChanges();
        await Service().ProcessReadyRequestsAsync(default);

        Assert.Equal(BookRequestStatus.Ready, request.Status);
        Assert.Null(request.DiagnosticMessage);
    }

    [Fact]
    public async Task ProcessingError_IsShownOnTheRequestAndRetried()
    {
        var request = ProcessingRequest("part1.mp3");
        File.Delete(chaptarr.Files[0].Path);

        await Service().ProcessReadyRequestsAsync(default);

        Assert.Equal(BookRequestStatus.Processing, request.Status);
        Assert.StartsWith("Processing error (will retry):", request.DiagnosticMessage);
    }

    public void Dispose()
    {
        t.Dispose();
        try { Directory.Delete(root, recursive: true); } catch { /* temp cleanup */ }
    }
}
