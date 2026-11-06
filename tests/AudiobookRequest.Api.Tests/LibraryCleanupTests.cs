using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Tests;

public sealed class LibraryCleanupTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeJellyfinClient jellyfin = new();
    private readonly FakeChaptarrClient chaptarr = new();
    private readonly FakeNotificationSender push = new();

    private LibraryCleanupService Service() => new(
        t.Db, t.Settings, jellyfin,
        new RequestCancellationService(t.Db, t.Settings, chaptarr, push, Nulls.Logger<RequestCancellationService>()));

    private static JellyfinPlayedItem Owned(string name, string author = "An Author") =>
        new() { Id = Guid.NewGuid().ToString("N"), Name = name, ArtistItems = [new JellyfinNameId { Name = author }] };

    private BookRequest Ready(string title, string author = "An Author", int? chaptarrBookId = null) =>
        t.AddRequest(t.Victoria, BookRequestStatus.Ready, title, r =>
        {
            r.Author = author;
            r.ChaptarrBookId = chaptarrBookId;
            r.ReadyAt = DateTimeOffset.UtcNow;
        });

    public void Dispose() => t.Dispose();

    [Fact]
    public async Task FlagsFinishedRequestsWhoseBookIsNoLongerInJellyfin()
    {
        jellyfin.Library.Add(Owned("Still Here"));
        Ready("Still Here");
        var gone = Ready("Deleted Book");

        var scan = await Service().ScanAsync(default);

        Assert.Null(scan.Blocked);
        Assert.Equal(2, scan.FinishedChecked);
        Assert.Equal(1, scan.JellyfinBooks);
        Assert.Equal([gone.Id], scan.Missing.Select(m => m.Id));
        Assert.Equal("victoria", scan.Missing[0].RequestedBy);
    }

    [Fact]
    public async Task ABookSpelledDifferentlyInJellyfinIsStillThere()
    {
        // The book that showed "Jellyfin hasn't picked this up yet" although it was there.
        jellyfin.Library.Add(Owned("The Dawn of the Cursed Queen: Gods and Monsters, Book 3", "Amber V. Nicole"));
        Ready("The Dawn of the Cursed Queen (Gods & Monsters, #3)", "Amber V. Nicole");

        var scan = await Service().ScanAsync(default);

        Assert.Empty(scan.Missing);
    }

    [Fact]
    public async Task OnlyFinishedRequestsAreEverFlagged()
    {
        jellyfin.Library.Add(Owned("Something Else"));
        t.AddRequest(t.Victoria, BookRequestStatus.Downloading, "Not Yet");
        t.AddRequest(t.Victoria, BookRequestStatus.Requested, "Just Asked");
        t.AddRequest(t.Victoria, BookRequestStatus.Failed, "Failed One");
        t.AddRequest(t.Victoria, BookRequestStatus.Processing, "Being Filed");

        var scan = await Service().ScanAsync(default);

        Assert.Equal(0, scan.FinishedChecked);
        Assert.Empty(scan.Missing);
    }

    [Fact]
    public async Task AnEmptyJellyfinIsNotTakenAsEverythingDeleted()
    {
        Ready("One");
        Ready("Two");

        var scan = await Service().ScanAsync(default);

        Assert.NotNull(scan.Blocked);
        Assert.Contains("no audiobooks at all", scan.Blocked);
        Assert.Empty(scan.Missing);
    }

    [Fact]
    public async Task JellyfinNotSetUpIsReportedNotGuessedAt()
    {
        t.CurrentSettings.JellyfinApiKey = null;
        await t.Db.SaveChangesAsync();
        Ready("One");

        var scan = await Service().ScanAsync(default);

        Assert.NotNull(scan.Blocked);
        Assert.Empty(scan.Missing);
    }

    [Fact]
    public async Task JellyfinBeingDownSurfacesAsAnErrorAndChangesNothing()
    {
        Ready("One");
        jellyfin.LibraryError = new HttpRequestException("connection refused");

        await Assert.ThrowsAsync<HttpRequestException>(() => Service().ScanAsync(default));
        await Assert.ThrowsAsync<HttpRequestException>(() => Service().RemoveAsync([1], default));

        Assert.Single(t.Db.BookRequests);
    }

    [Fact]
    public async Task RemovesTheRecordButKeepsTheFilesAndTheOriginalsRecord()
    {
        jellyfin.Library.Add(Owned("Still Here"));
        Ready("Still Here");
        var gone = Ready("Deleted Book", chaptarrBookId: 77);
        var file = Path.Combine(Path.GetTempPath(), "abr-cleanup-" + Guid.NewGuid().ToString("N") + ".m4b");
        await File.WriteAllTextAsync(file, "audio");
        t.Db.ProcessedBooks.Add(new ProcessedBook
        {
            BookRequestId = gone.Id,
            FinalPath = file,
            OriginalSourcePathsJson = "[]",
            CopiedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
            OriginalCleanupAt = DateTimeOffset.UtcNow.AddDays(7),
        });
        await t.Db.SaveChangesAsync();
        t.Db.ChangeTracker.Clear();

        try
        {
            var result = await Service().RemoveAsync([gone.Id], default);

            Assert.Equal(1, result.Removed);
            Assert.Equal(0, result.StillInJellyfin);
            Assert.Equal(["Still Here"], await t.Db.BookRequests.Select(r => r.Title).ToListAsync());
            Assert.Equal([77], chaptarr.DeletedBookIds);          // Chaptarr is told, so a fresh request starts clean
            Assert.True(File.Exists(file));                       // the audiobook file is never touched
            var processed = await t.Db.ProcessedBooks.SingleAsync(); // its bookkeeping stays, detached from the request
            Assert.Null(processed.BookRequestId);
            Assert.Contains(gone.Id, push.Removed);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task NeverRemovesABookThatIsStillInJellyfin_EvenIfAskedTo()
    {
        jellyfin.Library.Add(Owned("Still Here"));
        jellyfin.Library.Add(Owned("Another"));
        var present = Ready("Still Here");
        var gone = Ready("Deleted Book");

        var result = await Service().RemoveAsync([present.Id, gone.Id, 9999], default);

        Assert.Equal(1, result.Removed);
        Assert.Equal(2, result.StillInJellyfin); // the present one, and the id that isn't a missing finished request
        Assert.Equal(["Still Here"], await t.Db.BookRequests.Select(r => r.Title).ToListAsync());
    }

    [Fact]
    public async Task RemovingWhileJellyfinLooksEmptyIsRefused()
    {
        var one = Ready("One");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RemoveAsync([one.Id], default));

        Assert.Single(t.Db.BookRequests);
    }

    [Fact]
    public void TheLinkServiceNowFindsTheBookThatShowedAsNotPickedUp()
    {
        var item = Owned("The Dawn of the Cursed Queen: Gods and Monsters, Book 3", "Amber V. Nicole");

        Assert.True(JellyfinLinkService.IsSameBook(item, "The Dawn of the Cursed Queen (Gods & Monsters, #3)", "Amber V. Nicole"));
        Assert.False(JellyfinLinkService.IsSameBook(item, "The Dawn of the Cursed Queen (Gods & Monsters, #4)", "Amber V. Nicole"));
        Assert.False(JellyfinLinkService.IsSameBook(item, "The Dawn of the Cursed Queen (Gods & Monsters, #3)", "Someone Else"));
    }
}
