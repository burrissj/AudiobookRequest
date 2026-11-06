using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public class OrchestrationDiagnosticsTests
{
    private static (ChaptarrOrchestrationService Service, FakeChaptarrClient Chaptarr, FakeExternalNotifier External, FakeNotificationSender Push) Build(TestDb t)
    {
        var chaptarr = new FakeChaptarrClient();
        var external = new FakeExternalNotifier();
        var push = new FakeNotificationSender();
        var service = new ChaptarrOrchestrationService(t.Db, t.Settings, chaptarr, push, external,
            new ImportFixService(t.Db, t.Settings, chaptarr, Nulls.Logger<ImportFixService>()), Nulls.Logger<ChaptarrOrchestrationService>());
        return (service, chaptarr, external, push);
    }

    [Fact]
    public async Task BlockedImport_ShowsChaptarrsReasonOnTheRequest()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Downloading, configure: r => r.ChaptarrBookId = 3368);
        chaptarr.Queue.Add(new ChaptarrQueueItem(
            "Mistborn", 3368, "completed", 100, 0, "Deluge",
            TrackedDownloadStatus: "warning", TrackedDownloadState: "importBlocked",
            ErrorMessage: "Rejected: matched a different book."));

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Downloading, request.Status);
        Assert.Equal("Import blocked: Rejected: matched a different book.", request.DiagnosticMessage);
    }

    [Fact]
    public async Task ABlankRootFolderSetting_IsNamedInsteadOfCrashing()
    {
        using var t = new TestDb();
        t.CurrentSettings.ChaptarrRootFolderPath = null;
        t.Db.SaveChanges();
        var (service, _, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Requested);

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Requested, request.Status);
        Assert.Equal("Couldn't add this to Chaptarr yet: The Chaptarr root folder path isn't set. Fill it in under Settings.", request.DiagnosticMessage);
    }

    [Fact]
    public async Task AProgrammingError_ShowsWhereItHappenedNotJustObjectReferenceNotSet()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        chaptarr.AddBookError = new NullReferenceException();
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Requested);

        await service.RunOnceAsync(default);

        Assert.StartsWith("Couldn't add this to Chaptarr yet: Object reference not set", request.DiagnosticMessage);
        Assert.Contains("internal error in", request.DiagnosticMessage);
    }

    [Fact]
    public async Task AnIdChaptarrNoLongerKnows_IsReplacedByTheSameBookFoundByTitleAndAuthor()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Requested, "A Court of Splintered Harmony [Dramatized Adaptation]",
            r => r.Author = "Sarah J. Maas");
        chaptarr.UnknownForeignIds.Add(request.ChaptarrForeignBookId);
        chaptarr.Lookups["A Court of Splintered Harmony [Dramatized Adaptation] Sarah J. Maas"] =
        [
            new() { ForeignBookId = "other", Title = "A Court of Splintered Harmony", AuthorName = "Someone Else" },
            new() { ForeignBookId = "new-id", Title = "A Court of Splintered Harmony (Dramatized Adaptation)", AuthorName = "Sarah J. Maas" },
        ];

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Searching, request.Status);
        Assert.Equal("new-id", request.ChaptarrForeignBookId);
        Assert.Equal(["new-id"], chaptarr.AddedForeignIds);
        Assert.DoesNotContain("Couldn't add", request.DiagnosticMessage);
    }

    [Fact]
    public async Task AnUnknownIdWithNoMatchingBook_KeepsTheRequestAndSaysSo()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Requested);
        var originalId = request.ChaptarrForeignBookId;
        chaptarr.UnknownForeignIds.Add(originalId);

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Requested, request.Status);
        Assert.Equal(originalId, request.ChaptarrForeignBookId);
        Assert.Contains("found no match", request.DiagnosticMessage);
    }

    [Fact]
    public async Task AWishlistedRequestWhoseListingIsGone_FailsAfterTheGraceSoItCanBeRetried()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Requested, configure: r =>
        {
            r.Wishlisted = true;
            r.RequestedAt = DateTimeOffset.UtcNow.AddHours(-1);
        });
        chaptarr.UnknownForeignIds.Add(request.ChaptarrForeignBookId);

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Failed, request.Status);
        Assert.Contains("Retry with different search", request.FailureReason);
    }

    [Fact]
    public async Task HealthyDownload_HasNoDiagnostic()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Downloading, configure: r =>
        {
            r.ChaptarrBookId = 7;
            r.DiagnosticMessage = "old problem";
        });
        chaptarr.Queue.Add(new ChaptarrQueueItem("Book", 7, "downloading", 100, 40, "Deluge", TrackedDownloadStatus: "ok", TrackedDownloadState: "downloading"));

        await service.RunOnceAsync(default);

        Assert.Null(request.DiagnosticMessage);
        Assert.Equal(0.6, request.DownloadProgressPercent!.Value, 3);
    }

    [Fact]
    public async Task ImportedBook_MovesToProcessingAndClearsDiagnostic()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Downloading, configure: r =>
        {
            r.ChaptarrBookId = 7;
            r.DiagnosticMessage = "Import blocked: x";
        });
        chaptarr.History.Add(new ChaptarrHistoryItem(7, "bookFileImported", DateTimeOffset.UtcNow));

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Processing, request.Status);
        Assert.Null(request.DiagnosticMessage);
    }

    [Fact]
    public async Task FailedDownloadInHistory_IsReportedWhileSnatched()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Snatched, configure: r =>
        {
            r.ChaptarrBookId = 9;
            r.RequestedAt = DateTimeOffset.UtcNow.AddHours(-1);
        });
        chaptarr.History.Add(new ChaptarrHistoryItem(9, "grabbed", DateTimeOffset.UtcNow.AddMinutes(-5)));
        chaptarr.History.Add(new ChaptarrHistoryItem(9, "downloadFailed", DateTimeOffset.UtcNow.AddMinutes(-1), "Torrent was removed"));

        await service.RunOnceAsync(default);

        Assert.Equal("Download failed: Torrent was removed", request.DiagnosticMessage);
    }

    [Fact]
    public async Task NothingFoundAfter24Hours_FailsAndNotifies()
    {
        using var t = new TestDb();
        var (service, _, external, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Searching, configure: r =>
        {
            r.ChaptarrBookId = 5;
            r.RequestedAt = DateTimeOffset.UtcNow.AddHours(-25);
        });

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Failed, request.Status);
        var sent = Assert.Single(external.Sent);
        Assert.Equal(NotificationKind.RequestFailed, sent.Kind);
    }

    [Fact]
    public async Task WishlistedRequest_KeepsSearchingInsteadOfFailing()
    {
        using var t = new TestDb();
        var (service, chaptarr, external, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Searching, configure: r =>
        {
            r.ChaptarrBookId = 5;
            r.Wishlisted = true;
            r.RequestedAt = DateTimeOffset.UtcNow.AddHours(-30);
            r.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-13);
        });

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Searching, request.Status);
        Assert.Equal([5], chaptarr.SearchedBookIds);
        Assert.StartsWith("Wishlisted:", request.DiagnosticMessage);
        Assert.Empty(external.Sent);
        Assert.True(DateTimeOffset.UtcNow - request.LastSearchAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task WishlistedRequest_DoesNotSearchAgainBeforeTheRetryInterval()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        t.AddRequest(t.Victoria, BookRequestStatus.Searching, configure: r =>
        {
            r.ChaptarrBookId = 5;
            r.Wishlisted = true;
            r.LastSearchAt = DateTimeOffset.UtcNow.AddHours(-2);
        });

        await service.RunOnceAsync(default);

        Assert.Empty(chaptarr.SearchedBookIds);
    }

    [Fact]
    public async Task RequestedBook_IsAddedToChaptarrAndSearched()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Requested);

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Searching, request.Status);
        Assert.Equal(100, request.ChaptarrBookId);
        Assert.Equal([100], chaptarr.SearchedBookIds);
        Assert.NotNull(request.LastSearchAt);
    }

    [Fact]
    public async Task PendingApprovalRequests_AreNotTouched()
    {
        using var t = new TestDb();
        var (service, chaptarr, _, _) = Build(t);
        var request = t.AddRequest(t.Victoria, BookRequestStatus.PendingApproval);

        await service.RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.PendingApproval, request.Status);
        Assert.Null(request.ChaptarrBookId);
        Assert.Empty(chaptarr.SearchedBookIds);
    }
}
