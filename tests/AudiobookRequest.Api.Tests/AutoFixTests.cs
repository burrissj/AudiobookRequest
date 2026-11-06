using System.Text.Json.Nodes;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public sealed class AutoFixTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeChaptarrClient chaptarr = new();

    private static ChaptarrImportCandidate Candidate(int matchedBook = 3628, string matchedTitle = "The Throne of Broken Gods", int author = 30, string? rejection = null) => new(
        "/downloads/p2/part2.m4b", "part2", 100, matchedBook, matchedTitle, author,
        rejection is null ? [] : [rejection],
        JsonNode.Parse("""{ "path": "/downloads/p2/part2.m4b", "quality": { "quality": { "id": 12 } } }""")!.AsObject());

    private static ChaptarrBookInfo Target(string title = "The Throne of Broken Gods (Part 2 of 2) [Dramatized Adaptation]", int author = 30) =>
        new(3636, title, author, [new ChaptarrEditionInfo(23267, false, true)]);

    // ---- the safety rules, in isolation ----

    [Fact]
    public void Evaluate_AllowsASiblingEditionByTheSameAuthor() =>
        Assert.True(ImportFixService.Evaluate([Candidate()], Target()).Safe);

    [Fact]
    public void Evaluate_AllowsASubtitleVariantOfTheSameTitle() =>
        Assert.True(ImportFixService.Evaluate(
            [Candidate(3368, "The Final Empire", 25)],
            new ChaptarrBookInfo(3369, "Mistborn: The Final Empire", 25, [])).Safe);

    [Fact]
    public void Evaluate_RefusesADifferentAuthor() =>
        Assert.False(ImportFixService.Evaluate([Candidate(author: 99)], Target()).Safe);

    [Fact]
    public void Evaluate_RefusesAnUnrelatedTitleEvenWhenTheAuthorMatches()
    {
        var decision = ImportFixService.Evaluate([Candidate(matchedTitle: "A Completely Different Book")], Target());

        Assert.False(decision.Safe);
        Assert.Contains("same title", decision.Reason);
    }

    [Fact]
    public void Evaluate_RefusesWhenFilesMatchedDifferentBooks()
    {
        var mixed = new[] { Candidate(3628), Candidate(3700) };

        Assert.False(ImportFixService.Evaluate(mixed, Target()).Safe);
    }

    [Fact]
    public void Evaluate_RefusesWhenChaptarrListsAnyOtherRejection() =>
        Assert.False(ImportFixService.Evaluate([Candidate(rejection: "Not an upgrade for existing file")], Target()).Safe);

    [Fact]
    public void Evaluate_RefusesWhenTheFilesAlreadyMatchTheRequestedBook() =>
        Assert.False(ImportFixService.Evaluate([Candidate(matchedBook: 3636)], Target()).Safe);

    [Fact]
    public void Evaluate_RefusesWithNothingToImport() =>
        Assert.False(ImportFixService.Evaluate([], Target()).Safe);

    // ---- wired into the orchestrator ----

    private ChaptarrOrchestrationService Orchestration() => new(
        t.Db, t.Settings, chaptarr, new FakeNotificationSender(), new FakeExternalNotifier(),
        new ImportFixService(t.Db, t.Settings, chaptarr, Nulls.Logger<ImportFixService>()),
        Nulls.Logger<ChaptarrOrchestrationService>());

    private BookRequest BlockedRequest()
    {
        var request = t.AddRequest(t.Victoria, BookRequestStatus.Downloading, "The Throne of Broken Gods (Part 2 of 2)", r =>
        {
            r.ChaptarrBookId = 3636;
            r.RequestedAt = DateTimeOffset.UtcNow.AddHours(-1);
        });
        chaptarr.Queue.Add(new ChaptarrQueueItem(
            "Part 2", 3636, "completed", 100, 0, "Deluge",
            TrackedDownloadStatus: "warning", TrackedDownloadState: "importBlocked",
            ErrorMessage: "Rejected: matched a different book.", DownloadId: "DL1"));
        chaptarr.ImportCandidates.Add(Candidate());
        chaptarr.BookInfos[3636] = Target();
        return request;
    }

    [Fact]
    public async Task WhenEnabled_ASafeBlockedImportIsFixedOnItsOwn_AndOnlyOnce()
    {
        t.CurrentSettings.AutoFixImports = true;
        t.Db.SaveChanges();
        var request = BlockedRequest();

        await Orchestration().RunOnceAsync(default);
        await Orchestration().RunOnceAsync(default);

        var (files, mode) = Assert.Single(chaptarr.ManualImports);
        Assert.Equal("auto", mode);
        Assert.Equal(3636, files.Single()["bookId"]!.GetValue<int>());
        Assert.NotNull(request.AutoFixAttemptedAt);
        Assert.StartsWith("Auto-fixed", request.DiagnosticMessage);
    }

    [Fact]
    public async Task WhenDisabled_NothingIsImportedAutomatically()
    {
        var request = BlockedRequest();

        await Orchestration().RunOnceAsync(default);

        Assert.Empty(chaptarr.ManualImports);
        Assert.Null(request.AutoFixAttemptedAt);
        Assert.StartsWith("Import blocked", request.DiagnosticMessage); // still reported for a manual fix
    }

    [Fact]
    public async Task WhenEnabled_AnUnsafeCaseIsLeftForAnAdmin_AndNotRetriedEveryTick()
    {
        t.CurrentSettings.AutoFixImports = true;
        t.Db.SaveChanges();
        var request = BlockedRequest();
        chaptarr.ImportCandidates.Clear();
        chaptarr.ImportCandidates.Add(Candidate(author: 99));

        await Orchestration().RunOnceAsync(default);
        chaptarr.ImportCandidates.Clear();
        chaptarr.ImportCandidates.Add(Candidate()); // even if it became safe, the attempt was already spent
        await Orchestration().RunOnceAsync(default);

        Assert.Empty(chaptarr.ManualImports);
        Assert.NotNull(request.AutoFixAttemptedAt);
        Assert.StartsWith("Import blocked", request.DiagnosticMessage);
    }

    [Fact]
    public async Task WhenChaptarrRejectsTheAutoImport_TheReasonIsShownAndItIsNotRetried()
    {
        t.CurrentSettings.AutoFixImports = true;
        t.Db.SaveChanges();
        var request = BlockedRequest();
        chaptarr.CommandStatuses.Enqueue(new ChaptarrCommandStatus(900, "failed", "unsuccessful", "Disk is full"));

        await Orchestration().RunOnceAsync(default);

        Assert.Contains("Disk is full", request.DiagnosticMessage);
        Assert.Single(chaptarr.ManualImports);
    }

    [Fact]
    public async Task SearchingAgain_ResetsTheOneAttemptAllowance()
    {
        var request = BlockedRequest();
        request.Status = BookRequestStatus.Failed; // search again is offered for failed or still-searching requests
        request.AutoFixAttemptedAt = DateTimeOffset.UtcNow;
        t.Db.SaveChanges();
        var push = new FakeNotificationSender();
        var upcoming = new UpcomingRequestService(t.Db, t.Settings, chaptarr, push, Nulls.Logger<UpcomingRequestService>());
        var actions = new RequestActionsService(t.Db, t.Settings, chaptarr, new FakeExternalNotifier(), push, upcoming, Nulls.Logger<RequestActionsService>());

        await actions.SearchAgainAsync(request.Id, default);

        Assert.Null(request.AutoFixAttemptedAt);
    }

    public void Dispose() => t.Dispose();
}
