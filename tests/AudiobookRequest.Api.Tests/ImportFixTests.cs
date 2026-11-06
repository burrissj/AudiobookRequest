using System.Text.Json.Nodes;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public sealed class ImportFixTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeChaptarrClient chaptarr = new();

    private ImportFixService Service() => new(t.Db, t.Settings, chaptarr, Nulls.Logger<ImportFixService>());

    private ChaptarrOrchestrationService Orchestration() => new(
        t.Db, t.Settings, chaptarr, new FakeNotificationSender(), new FakeExternalNotifier(), Service(),
        Nulls.Logger<ChaptarrOrchestrationService>());

    private static ChaptarrImportCandidate Candidate(string path = "/downloads/Part 2/part2.m4b", int matchedBook = 3628) => new(
        path, "part2", 638_731_591, matchedBook, "The Throne of Broken Gods", 30, [],
        JsonNode.Parse("""
            {
              "path": "/downloads/Part 2/part2.m4b",
              "quality": { "quality": { "id": 12, "name": "M4B" }, "revision": { "version": 1 } },
              "indexerFlags": 4,
              "additionalFile": false,
              "replaceExistingFiles": false
            }
            """)!.AsObject());

    private BookRequest BlockedRequest()
    {
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Downloading, "The Throne of Broken Gods (Part 2 of 2)", r => r.ChaptarrBookId = 3636);
        chaptarr.Queue.Add(new ChaptarrQueueItem(
            "Part 2", 3636, "completed", 100, 0, "Deluge",
            TrackedDownloadStatus: "warning", TrackedDownloadState: "importBlocked", DownloadId: "ABC123"));
        chaptarr.ImportCandidates.Add(Candidate());
        chaptarr.BookInfos[3636] = new ChaptarrBookInfo(3636, "The Throne of Broken Gods (Part 2 of 2) [Dramatized Adaptation]", 30,
        [
            new ChaptarrEditionInfo(23270, IsEbook: true, Monitored: true),
            new ChaptarrEditionInfo(23268, IsEbook: false, Monitored: false),
            new ChaptarrEditionInfo(23267, IsEbook: false, Monitored: true),
        ]);
        return request;
    }

    [Fact]
    public async Task Preview_ListsTheFilesAndWhatChaptarrMatchedThemTo()
    {
        var request = BlockedRequest();

        var preview = await Service().PreviewAsync(request.Id, default);

        Assert.NotNull(preview);
        Assert.True(preview.CanFix);
        var file = Assert.Single(preview.Files);
        Assert.Equal("part2.m4b", file.FileName);
        Assert.Equal(3628, file.MatchedBookId);
        Assert.Equal("The Throne of Broken Gods", file.MatchedTitle);
        Assert.Empty(chaptarr.ManualImports); // previewing never imports
        Assert.True(preview.AutoFixable);     // same author, same title apart from the "(Part 2 of 2)" suffix
        Assert.Contains("same book", preview.AutoFixNote);
    }

    [Fact]
    public async Task Fix_ImportsTheFilesAsTheRequestedBook_WithItsAudiobookEditionAndChaptarrsOwnQuality()
    {
        var request = BlockedRequest();

        var result = await Service().FixAsync(request.Id, default);

        Assert.Equal("completed", result!.Outcome);
        var (files, mode) = Assert.Single(chaptarr.ManualImports);
        Assert.Equal("auto", mode);
        var file = Assert.Single(files);
        Assert.Equal("/downloads/Part 2/part2.m4b", file["path"]!.GetValue<string>());
        Assert.Equal(3636, file["bookId"]!.GetValue<int>());            // the requested book, not the 3628 Chaptarr guessed
        Assert.Equal(30, file["authorId"]!.GetValue<int>());
        Assert.Equal(23267, file["editionId"]!.GetValue<int>());        // monitored audiobook edition, never the ebook
        Assert.Equal("ABC123", file["downloadId"]!.GetValue<string>());
        Assert.Equal(12, file["quality"]!["quality"]!["id"]!.GetValue<int>());
        Assert.Equal(4, file["indexerFlags"]!.GetValue<int>());
        Assert.True(file["disableReleaseSwitching"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Fix_SendsEveryFileOfAMultiFileDownload()
    {
        var request = BlockedRequest();
        chaptarr.ImportCandidates.Add(Candidate("/downloads/Part 2/part2b.m4b"));

        await Service().FixAsync(request.Id, default);

        Assert.Equal(2, chaptarr.ManualImports.Single().Files.Count);
    }

    [Fact]
    public async Task Fix_ReportsChaptarrsReasonWhenTheImportCommandFails()
    {
        var request = BlockedRequest();
        chaptarr.CommandStatuses.Enqueue(new ChaptarrCommandStatus(900, "failed", "unsuccessful", "Destination folder is not writable"));

        var result = await Service().FixAsync(request.Id, default);

        Assert.Equal("failed", result!.Outcome);
        Assert.Contains("Destination folder is not writable", result.Message);
    }

    [Fact]
    public async Task Fix_ReportsFailureWhenChaptarrRefusesTheCall()
    {
        var request = BlockedRequest();
        chaptarr.ManualImportError = new HttpRequestException("Response status code does not indicate success: 400 (Bad Request).");

        var result = await Service().FixAsync(request.Id, default);

        Assert.Equal("failed", result!.Outcome);
        Assert.Contains("400", result.Message);
    }

    [Fact]
    public async Task AfterAFix_TheNextOrchestratorPassMovesTheRequestToProcessing()
    {
        var request = BlockedRequest();
        request.RequestedAt = DateTimeOffset.UtcNow.AddHours(-1);
        t.Db.SaveChanges();

        await Service().FixAsync(request.Id, default);
        // Chaptarr's history now shows the import (the endpoint runs this pass right after the fix).
        chaptarr.Queue.Clear();
        chaptarr.History.Add(new ChaptarrHistoryItem(3636, "bookFileImported", DateTimeOffset.UtcNow));
        await Orchestration().RunOnceAsync(default);

        Assert.Equal(BookRequestStatus.Processing, request.Status);
    }

    [Fact]
    public async Task NotBlocked_CannotBeFixed()
    {
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Downloading, configure: r => r.ChaptarrBookId = 55);
        chaptarr.Queue.Add(new ChaptarrQueueItem("Healthy", 55, "downloading", 100, 50, "Deluge", TrackedDownloadState: "downloading", DownloadId: "OK"));

        var preview = await Service().PreviewAsync(request.Id, default);
        var result = await Service().FixAsync(request.Id, default);

        Assert.False(preview!.CanFix);
        Assert.Equal("failed", result!.Outcome);
        Assert.Empty(chaptarr.ManualImports);
    }

    [Theory]
    [InlineData(BookRequestStatus.Ready)]
    [InlineData(BookRequestStatus.Processing)]
    [InlineData(BookRequestStatus.PendingApproval)]
    public async Task AlreadyImportedOrNotStarted_CannotBeFixed(BookRequestStatus status)
    {
        var request = BlockedRequest();
        request.Status = status;
        t.Db.SaveChanges();

        var preview = await Service().PreviewAsync(request.Id, default);

        Assert.False(preview!.CanFix);
    }

    [Fact]
    public async Task RequestNotYetInChaptarr_CannotBeFixed()
    {
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Requested);

        var preview = await Service().PreviewAsync(request.Id, default);

        Assert.False(preview!.CanFix);
        Assert.Contains("hasn't been added", preview.Reason);
    }

    [Fact]
    public async Task UnknownRequest_IsNull()
    {
        Assert.Null(await Service().PreviewAsync(12345, default));
        Assert.Null(await Service().FixAsync(12345, default));
    }

    [Fact]
    public async Task Fix_WhenTheRequestedBookIsGoneFromChaptarr_ExplainsInsteadOfImporting()
    {
        var request = BlockedRequest();
        chaptarr.BookInfos.Clear();

        var result = await Service().FixAsync(request.Id, default);

        Assert.Equal("failed", result!.Outcome);
        Assert.Empty(chaptarr.ManualImports);
    }

    public void Dispose() => t.Dispose();
}
