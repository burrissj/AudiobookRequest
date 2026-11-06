using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Tests;

public class LibrarySeriesTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeMetadataClient audible = new();

    private LibrarySeriesService Service() => new(t.Db, audible);

    private static AudiobookListing Listing(string title, string author, string? series = null, string? sequence = null, params string[] moreAuthors) =>
        new("B0" + title.GetHashCode(), title, author, "cover", 600, null, null, series, sequence, [], [author, .. moreAuthors], null, series is null ? null : "S1");

    private LibraryBookCache Cached(string id, string title, string? author)
    {
        var row = new LibraryBookCache { JellyfinItemId = id, Title = title, Author = author, Artists = author };
        t.Db.LibraryBooks.Add(row);
        t.Db.SaveChanges();
        return row;
    }

    // ---- reading Audible's answer ----

    [Theory]
    [InlineData("3", "3")]
    [InlineData("1-3", "1")]
    [InlineData("2.5", "2.5")]
    [InlineData("Book 4", "4")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseSequence_ReadsTheFirstNumber(string? text, string? expected) =>
        Assert.Equal(expected, LibrarySeriesService.ParseSequence(text)?.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Pick_NeedsTheSameBook_NotJustASimilarTitle()
    {
        var listings = new[]
        {
            Listing("The Final Empire Companion", "Someone Else", "Fan Series", "1"),
            Listing("The Final Empire", "Brandon Sanderson", "Mistborn", "1"),
        };

        var pick = LibrarySeriesService.Pick("The Final Empire (Unabridged)", "Brandon Sanderson", listings);

        Assert.Equal("Mistborn", pick?.SeriesTitle);
    }

    [Fact]
    public void Pick_RejectsTheSameTitleByAnotherAuthor()
    {
        var listings = new[] { Listing("Dark Wings", "Totally Different", "Other Saga", "2") };

        Assert.Null(LibrarySeriesService.Pick("Dark Wings", "Ann Author", listings));
    }

    [Fact]
    public void Pick_WithNoAuthorOnFile_TakesTheTitleAlone()
    {
        var listings = new[] { Listing("Dark Wings", "Ann Author", "Wing Saga", "2") };

        Assert.Equal("Wing Saga", LibrarySeriesService.Pick("Dark Wings", null, listings)?.SeriesTitle);
    }

    [Fact]
    public void Pick_AnyCoAuthorWillDo_AndAMiddleInitialDoesNotMatter()
    {
        var listings = new[] { Listing("Throne", "Someone", "Gods", "2", "Amber V. Nicole") };

        Assert.NotNull(LibrarySeriesService.Pick("Throne", "Amber Nicole", listings));
    }

    // ---- the look-up ----

    [Fact]
    public async Task Lookup_FindsTheSeriesAndNumber()
    {
        audible.TitleAuthorPages["Brandon Sanderson"] = new([Listing("The Final Empire", "Brandon Sanderson", "Mistborn", "1")], 1);

        var found = await Service().LookupAsync("The Final Empire", "Brandon Sanderson", default);

        Assert.Equal(new SeriesLookup(true, "Mistborn", "S1", "1"), found);
    }

    [Fact]
    public async Task Lookup_RetriesWithoutTheBracketedExtras_ThenWithoutTheAuthor()
    {
        audible.TitleAuthorPages["Ann Author"] = new([], 0); // nothing when searching with the author
        audible.TitlePage = new([Listing("Dark Wings", "Ann Author", "Wing Saga", "2")], 1); // but found by title alone

        var found = await Service().LookupAsync("Dark Wings (Unabridged) [Dramatized]", "Ann Author", default);

        Assert.Equal("Wing Saga", found?.Series);
        Assert.Equal(
            [("Dark Wings (Unabridged) [Dramatized]", "Ann Author"), ("Dark Wings", "Ann Author"), ("Dark Wings", null)],
            audible.SearchCalls);
    }

    [Fact]
    public async Task Lookup_RecognisedBookWithNoSeriesIsAnAnswerToo()
    {
        audible.TitleAuthorPages["Andy Weir"] = new([Listing("Artemis", "Andy Weir")], 1);

        var found = await Service().LookupAsync("Artemis", "Andy Weir", default);

        Assert.Equal(new SeriesLookup(true, null, null, null), found);
    }

    [Fact]
    public async Task Lookup_NothingAtAll_IsNullSoTheCallerCanTellItMayBeOffline()
    {
        Assert.Null(await Service().LookupAsync("Nothing Anywhere", "Nobody", default));
    }

    // ---- the background batch ----

    [Fact]
    public async Task Enrich_RecordsTheSeriesOnTheCachedBooks()
    {
        var row = Cached("j1", "The Final Empire", "Brandon Sanderson");
        audible.TitleAuthorPages["Brandon Sanderson"] = new([Listing("The Final Empire", "Brandon Sanderson", "Mistborn", "1")], 1);

        var settled = await Service().EnrichBatchAsync(10, default);

        Assert.Equal(1, settled);
        await t.Db.Entry(row).ReloadAsync();
        Assert.Equal("Mistborn", row.SeriesName);
        Assert.Equal("1", row.SeriesSequence);
        Assert.NotNull(row.SeriesCheckedAt);
    }

    [Fact]
    public async Task Enrich_LeavesAlreadyCheckedBooksAloneUntilTheyAreDueAgain()
    {
        var fresh = Cached("j1", "A", "X");
        fresh.SeriesCheckedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var stale = Cached("j2", "B", "X");
        stale.SeriesCheckedAt = DateTimeOffset.UtcNow.AddDays(-5); // not in a series, last asked 5 days ago
        var staleSeries = Cached("j3", "C", "X");
        staleSeries.SeriesName = "Saga";
        staleSeries.SeriesCheckedAt = DateTimeOffset.UtcNow.AddDays(-5); // has a series: no need to ask for two months
        t.Db.SaveChanges();

        await Service().EnrichBatchAsync(10, default);

        Assert.DoesNotContain(audible.SearchCalls, c => c.Title == "A" || c.Title == "C");
        Assert.Contains(audible.SearchCalls, c => c.Title == "B");
    }

    [Fact]
    public async Task Enrich_WhenAudibleAnswersNothingForEveryBook_RecordsNothingSoItIsTriedAgain()
    {
        var rows = new[] { Cached("j1", "One", "A"), Cached("j2", "Two", "B"), Cached("j3", "Three", "C") };

        var settled = await Service().EnrichBatchAsync(10, default);

        Assert.Equal(0, settled);
        foreach (var r in rows)
        {
            await t.Db.Entry(r).ReloadAsync();
            Assert.Null(r.SeriesCheckedAt);
        }
    }

    [Fact]
    public async Task Enrich_DoesAtMostOneBatch_NewBooksFirst()
    {
        var old = Cached("j0", "Old", "A");
        old.SeriesCheckedAt = DateTimeOffset.UtcNow.AddDays(-10);
        Cached("j1", "New One", "A");
        Cached("j2", "New Two", "A");
        t.Db.SaveChanges();
        audible.TitlePage = new([Listing("Anything", "Z")], 1);

        var settled = await Service().EnrichBatchAsync(2, default);

        Assert.Equal(2, settled);
        Assert.DoesNotContain(audible.SearchCalls, c => c.Title == "Old");
    }

    [Fact]
    public async Task Index_HasTheSeriesAndEveryBookThatWasAsked()
    {
        var a = Cached("j1", "A", "X");
        a.SeriesName = "Saga";
        a.SeriesSequence = "2";
        a.SeriesCheckedAt = DateTimeOffset.UtcNow;
        var b = Cached("j2", "B", "X");
        b.SeriesCheckedAt = DateTimeOffset.UtcNow;
        Cached("j3", "C", "X");
        t.Db.SaveChanges();

        var index = await Service().GetIndexAsync(default);

        Assert.Equal(new SeriesInfo("Saga", 2), index.Series["j1"]);
        Assert.Single(index.Series);
        Assert.Equal(["j1", "j2"], index.Checked.Order());
    }

    // ---- an admin's corrections ----

    [Fact]
    public async Task Override_IsSavedTidiedAndShownInTheIndex()
    {
        var saved = await Service().SetOverrideAsync("j1", new BookOverrideDto("  Amber   V. Nicole ", " Gods  & Monsters", 2, false), default);

        Assert.Equal(new BookOverrideDto("Amber V. Nicole", "Gods & Monsters", 2, false), saved);
        var index = await Service().GetIndexAsync(default);
        Assert.Equal("Amber V. Nicole", index.AuthorOf("j1"));
        Assert.Equal(new SeriesInfo("Gods & Monsters", 2), index.Series["j1"]);
    }

    [Fact]
    public async Task Override_BeatsWhatAudibleSaid_AndNotInSeriesTakesTheBookOutOfItsSeries()
    {
        var a = Cached("j1", "A", "X");
        a.SeriesName = "Wrong Saga";
        a.SeriesSequence = "1";
        var b = Cached("j2", "B", "X");
        b.SeriesName = "Wrong Saga";
        b.SeriesSequence = "2";
        t.Db.SaveChanges();
        await Service().SetOverrideAsync("j1", new BookOverrideDto(null, "Right Saga", 4, false), default);
        await Service().SetOverrideAsync("j2", new BookOverrideDto(null, null, null, true), default);

        var index = await Service().GetIndexAsync(default);

        Assert.Equal(new SeriesInfo("Right Saga", 4), index.Series["j1"]);
        Assert.DoesNotContain("j2", index.Series.Keys);
        Assert.True(index.IsNotInSeries("j2"));
        Assert.False(index.IsNotInSeries("j1"));
    }

    [Fact]
    public async Task Override_SurvivesAudibleBeingAskedAgain()
    {
        var row = Cached("j1", "The Final Empire", "Brandon Sanderson");
        audible.TitleAuthorPages["Brandon Sanderson"] = new([Listing("The Final Empire", "Brandon Sanderson", "Mistborn", "1")], 1);
        await Service().SetOverrideAsync("j1", new BookOverrideDto(null, "My Own Name", 7, false), default);

        await Service().EnrichBatchAsync(10, default);

        await t.Db.Entry(row).ReloadAsync();
        Assert.Equal("Mistborn", row.SeriesName); // Audible's answer is still kept...
        Assert.Equal(new SeriesInfo("My Own Name", 7), (await Service().GetIndexAsync(default)).Series["j1"]); // ...but doesn't win
    }

    [Fact]
    public async Task Override_WithNothingLeftIsRemoved()
    {
        await Service().SetOverrideAsync("j1", new BookOverrideDto("Ann", null, null, false), default);

        var saved = await Service().SetOverrideAsync("j1", new BookOverrideDto("  ", null, null, false), default);

        Assert.Null(saved);
        Assert.Empty(await t.Db.BookOverrides.ToListAsync());
        Assert.Null((await Service().GetEditAsync("j1", default)).Override);
    }

    [Fact]
    public async Task Override_UpdatesTheExistingRowInsteadOfAddingAnother()
    {
        await Service().SetOverrideAsync("j1", new BookOverrideDto("Ann", null, null, false), default);
        await Service().SetOverrideAsync("j1", new BookOverrideDto("Bea", "Saga", 1, false), default);

        var only = Assert.Single(await t.Db.BookOverrides.ToListAsync());
        Assert.Equal(("Bea", "Saga"), (only.Author, only.Series));
    }

    [Theory]
    [InlineData("Saga", 1, true)]     // in a series and not in one
    [InlineData(null, 3, false)]      // a number with no series to number it in
    [InlineData("Saga", -1, false)]   // not a real number
    [InlineData("Saga", 20000, false)]
    public async Task Override_RefusesContradictoryOrNonsenseInput(string? series, int number, bool notInSeries)
    {
        var ex = await Assert.ThrowsAsync<ListenException>(() =>
            Service().SetOverrideAsync("j1", new BookOverrideDto(null, series, number, notInSeries), default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(await t.Db.BookOverrides.ToListAsync());
    }

    [Fact]
    public async Task Override_RefusesAbsurdlyLongNames()
    {
        var ex = await Assert.ThrowsAsync<ListenException>(() =>
            Service().SetOverrideAsync("j1", new BookOverrideDto(new string('a', 201), null, null, false), default));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Edit_ShowsTheCorrectionAndWhatAudibleSaid()
    {
        var row = Cached("j1", "A", "X");
        row.SeriesName = "Saga";
        row.SeriesSequence = "3";
        row.SeriesCheckedAt = DateTimeOffset.UtcNow;
        t.Db.SaveChanges();
        await Service().SetOverrideAsync("j1", new BookOverrideDto("New Author", null, null, false), default);

        var edit = await Service().GetEditAsync("j1", default);

        Assert.Equal(new BookOverrideDto("New Author", null, null, false), edit.Override);
        Assert.Equal(new AudibleSeriesDto(true, "Saga", 3), edit.Audible);
        Assert.Equal(new AudibleSeriesDto(false, null, null), (await Service().GetEditAsync("unknown", default)).Audible);
    }

    public void Dispose() => t.Dispose();
}
