using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Tests;

public class PersonalBookServiceTests
{
    private const string BookA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BookB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string BookC = "cccccccccccccccccccccccccccccccc";

    private static (PersonalBookService Service, TestDb Db) Make()
    {
        var db = new TestDb();
        return (new PersonalBookService(db.Db), db);
    }

    // ---- tag tidying ----

    [Theory]
    [InlineData("Favorites", "Favorites", "favorites")]
    [InlineData("  Sci   Fi  ", "Sci Fi", "sci fi")]
    [InlineData("#Re-listen", "Re-listen", "re-listen")]
    [InlineData("##  Great narrator", "Great narrator", "great narrator")]
    [InlineData("Tab\there\nnow", "Tab here now", "tab here now")]
    public void NormalizeTag_TidiesWhatWasTyped(string raw, string display, string key)
    {
        var tag = PersonalBookService.NormalizeTag(raw);

        Assert.NotNull(tag);
        Assert.Equal(display, tag.Value.Display);
        Assert.Equal(key, tag.Value.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public void NormalizeTag_NothingUsableIsNull(string? raw) => Assert.Null(PersonalBookService.NormalizeTag(raw));

    [Fact]
    public void NormalizeTag_LongTagsAreCut()
    {
        var tag = PersonalBookService.NormalizeTag(new string('x', 100));

        Assert.Equal(PersonalBookService.MaxTagLength, tag!.Value.Display.Length);
    }

    // ---- ratings ----

    [Fact]
    public async Task Rating_SetChangeAndClear()
    {
        var (service, db) = Make();
        using var _ = db;

        Assert.Equal(4, await service.SetRatingAsync(db.Victoria.Id, BookA, 4, default));
        Assert.Equal(5, await service.SetRatingAsync(db.Victoria.Id, BookA, 5, default));
        Assert.Equal(5, (await service.GetAsync(db.Victoria.Id, BookA, default)).Rating);
        Assert.Single(db.Db.BookRatings);

        Assert.Null(await service.SetRatingAsync(db.Victoria.Id, BookA, 0, default));
        Assert.Null((await service.GetAsync(db.Victoria.Id, BookA, default)).Rating);
        Assert.Empty(db.Db.BookRatings);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public async Task Rating_OutOfRangeIsRefused(int stars)
    {
        var (service, db) = Make();
        using var _ = db;

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.SetRatingAsync(db.Victoria.Id, BookA, stars, default));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Rating_ClearingWhenThereIsNoneIsFine()
    {
        var (service, db) = Make();
        using var _ = db;

        Assert.Null(await service.SetRatingAsync(db.Victoria.Id, BookA, 0, default));
    }

    [Fact]
    public async Task Ratings_AreEachPersonsOwn()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.SetRatingAsync(db.Victoria.Id, BookA, 5, default);
        await service.SetRatingAsync(db.Admin.Id, BookA, 2, default);

        Assert.Equal(5, (await service.GetAsync(db.Victoria.Id, BookA, default)).Rating);
        Assert.Equal(2, (await service.GetAsync(db.Admin.Id, BookA, default)).Rating);
        var mine = await service.RatingsAsync(db.Victoria.Id, [BookA, BookB], default);
        Assert.Equal(new Dictionary<string, int> { [BookA] = 5 }, mine);
    }

    [Fact]
    public async Task RatedBooks_ComeBestFirst_AndPage()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.SetRatingAsync(db.Victoria.Id, BookA, 3, default);
        await service.SetRatingAsync(db.Victoria.Id, BookB, 5, default);
        await service.SetRatingAsync(db.Victoria.Id, BookC, 4, default);
        await service.SetRatingAsync(db.Admin.Id, "dddddddddddddddddddddddddddddddd", 5, default);

        var (ids, total) = await service.ItemIdsRatedAsync(db.Victoria.Id, 0, 2, default);
        var (rest, _) = await service.ItemIdsRatedAsync(db.Victoria.Id, 2, 2, default);

        Assert.Equal(3, total);
        Assert.Equal([BookB, BookC], ids);
        Assert.Equal([BookA], rest);
    }

    // ---- tags ----

    [Fact]
    public async Task Tags_AddAndRemove()
    {
        var (service, db) = Make();
        using var _ = db;

        Assert.Equal("Favorites", await service.AddTagAsync(db.Victoria.Id, BookA, "  Favorites ", default));
        await service.AddTagAsync(db.Victoria.Id, BookA, "Re-listen", default);
        Assert.Equal(["Favorites", "Re-listen"], (await service.GetAsync(db.Victoria.Id, BookA, default)).Tags);

        await service.RemoveTagAsync(db.Victoria.Id, BookA, "favorites", default); // any capitalisation
        Assert.Equal(["Re-listen"], (await service.GetAsync(db.Victoria.Id, BookA, default)).Tags);
    }

    [Fact]
    public async Task Tags_AddingTheSameTagAgainChangesNothing()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.AddTagAsync(db.Victoria.Id, BookA, "Sci-Fi", default);

        var again = await service.AddTagAsync(db.Victoria.Id, BookA, "sci-fi", default);

        Assert.Equal("Sci-Fi", again);
        Assert.Single(db.Db.BookTags);
    }

    [Fact]
    public async Task Tags_KeepTheSpellingTheyFirstUsed()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.AddTagAsync(db.Victoria.Id, BookA, "Sci-Fi", default);

        var onAnotherBook = await service.AddTagAsync(db.Victoria.Id, BookB, "sci-fi", default);

        Assert.Equal("Sci-Fi", onAnotherBook);
        var tags = await service.ListTagsAsync(db.Victoria.Id, default);
        Assert.Equal([new TagCountDto("Sci-Fi", 2)], tags);
    }

    [Fact]
    public async Task Tags_AreEachPersonsOwn()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.AddTagAsync(db.Victoria.Id, BookA, "Mine", default);
        await service.AddTagAsync(db.Admin.Id, BookA, "Theirs", default);

        Assert.Equal(["Mine"], (await service.GetAsync(db.Victoria.Id, BookA, default)).Tags);
        Assert.Equal(["Mine"], (await service.ListTagsAsync(db.Victoria.Id, default)).Select(t => t.Tag));
        // another person removing "Mine" doesn't touch mine
        await service.RemoveTagAsync(db.Admin.Id, BookA, "Mine", default);
        Assert.Equal(["Mine"], (await service.GetAsync(db.Victoria.Id, BookA, default)).Tags);
    }

    [Fact]
    public async Task Tags_EmptyIsRefused()
    {
        var (service, db) = Make();
        using var _ = db;

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.AddTagAsync(db.Victoria.Id, BookA, "  # ", default));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Tags_ABookHasALimit()
    {
        var (service, db) = Make();
        using var _ = db;
        for (var i = 0; i < PersonalBookService.MaxTagsPerBook; i++)
        {
            await service.AddTagAsync(db.Victoria.Id, BookA, $"tag {i}", default);
        }

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.AddTagAsync(db.Victoria.Id, BookA, "one too many", default));

        Assert.Equal(400, ex.StatusCode);
        // an existing tag can still be re-added without hitting the limit
        Assert.Equal("tag 3", await service.AddTagAsync(db.Victoria.Id, BookA, "TAG 3", default));
    }

    [Fact]
    public async Task Tags_ListedMostUsedFirst()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.AddTagAsync(db.Victoria.Id, BookA, "rare", default);
        foreach (var book in new[] { BookA, BookB, BookC })
        {
            await service.AddTagAsync(db.Victoria.Id, book, "common", default);
        }
        await service.AddTagAsync(db.Victoria.Id, BookB, "alpha", default);

        var tags = await service.ListTagsAsync(db.Victoria.Id, default);

        Assert.Equal([("common", 3), ("alpha", 1), ("rare", 1)], tags.Select(t => (t.Tag, t.Count)));
    }

    [Fact]
    public async Task BooksWithATag_NewestTaggedFirst_AnyCapitalisation()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.AddTagAsync(db.Victoria.Id, BookA, "Favorites", default);
        await service.AddTagAsync(db.Victoria.Id, BookB, "Favorites", default);
        await service.AddTagAsync(db.Victoria.Id, BookC, "Other", default);

        var (ids, total) = await service.ItemIdsWithTagAsync(db.Victoria.Id, "FAVORITES", 0, 10, default);

        Assert.Equal(2, total);
        Assert.Equal([BookB, BookA], ids);
        Assert.Empty((await service.ItemIdsWithTagAsync(db.Victoria.Id, "nothing", 0, 10, default)).Ids);
        Assert.Empty((await service.ItemIdsWithTagAsync(db.Victoria.Id, "", 0, 10, default)).Ids);
        Assert.Empty((await service.ItemIdsWithTagAsync(db.Admin.Id, "Favorites", 0, 10, default)).Ids);
    }

    // ---- bookmarks ----

    private static AddBookmarkRequest Mark(double at, string? note = null, string? label = null, string? clientId = null) => new(at, note, label, clientId);

    [Fact]
    public async Task Bookmarks_AreListedInBookOrder()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(900, "late", "Chapter 9"), default);
        await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(60, "early"), default);
        await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(300), default);

        var list = (await service.GetAsync(db.Victoria.Id, BookA, default)).Bookmarks;

        Assert.Equal([60.0, 300.0, 900.0], list.Select(b => b.PositionSeconds));
        Assert.Equal("Chapter 9", list[2].Label);
        Assert.Null(list[1].Note);
    }

    [Fact]
    public async Task Bookmarks_ASecondSendOfTheSameBookmarkIsNotSavedTwice()
    {
        var (service, db) = Make();
        using var _ = db;

        var first = await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(120, "x", null, "client-1"), default);
        var again = await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(120, "x", null, "client-1"), default);

        Assert.Equal(first.Id, again.Id);
        Assert.Single(db.Db.BookBookmarks);
    }

    [Fact]
    public async Task Bookmarks_DifferentPeopleMayReuseAClientId()
    {
        var (service, db) = Make();
        using var _ = db;

        await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(1, null, null, "same"), default);
        await service.AddBookmarkAsync(db.Admin.Id, BookA, Mark(1, null, null, "same"), default);

        Assert.Equal(2, db.Db.BookBookmarks.Count());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e9)]
    public async Task Bookmarks_ImpossiblePlacesAreRefused(double at)
    {
        var (service, db) = Make();
        using var _ = db;

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(at), default));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Bookmarks_NotesAreTidiedAndCut()
    {
        var (service, db) = Make();
        using var _ = db;

        var a = await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(1, "  great\tline\n  ", null), default);
        var b = await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(2, new string('n', 1000)), default);
        var c = await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(3, "   "), default);

        Assert.Equal("greatline", a.Note);
        Assert.Equal(PersonalBookService.MaxNoteLength, b.Note!.Length);
        Assert.Null(c.Note);
    }

    [Fact]
    public async Task Bookmarks_ABookHasALimit()
    {
        var (service, db) = Make();
        using var _ = db;
        for (var i = 0; i < PersonalBookService.MaxBookmarksPerBook; i++)
        {
            db.Db.BookBookmarks.Add(new Core.Entities.BookBookmark { AppUserId = db.Victoria.Id, JellyfinItemId = BookA, PositionSeconds = i });
        }
        await db.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(999), default));

        Assert.Equal(400, ex.StatusCode);
        // another book is unaffected
        await service.AddBookmarkAsync(db.Victoria.Id, BookB, Mark(1), default);
    }

    [Fact]
    public async Task Bookmarks_OnlyTheOwnerCanEditOrDelete()
    {
        var (service, db) = Make();
        using var _ = db;
        var mark = await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(10, "mine"), default);

        var ex = await Assert.ThrowsAsync<ListenException>(() => service.UpdateBookmarkAsync(db.Admin.Id, mark.Id, "hijacked", default));
        Assert.Equal(404, ex.StatusCode);
        await service.DeleteBookmarkAsync(db.Admin.Id, mark.Id, default);

        var still = (await service.GetAsync(db.Victoria.Id, BookA, default)).Bookmarks.Single();
        Assert.Equal("mine", still.Note);
    }

    [Fact]
    public async Task Bookmarks_EditNoteAndDelete()
    {
        var (service, db) = Make();
        using var _ = db;
        var mark = await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(10), default);

        var edited = await service.UpdateBookmarkAsync(db.Victoria.Id, mark.Id, "now with a note", default);
        Assert.Equal("now with a note", edited.Note);

        await service.DeleteBookmarkAsync(db.Victoria.Id, mark.Id, default);
        await service.DeleteBookmarkAsync(db.Victoria.Id, mark.Id, default); // deleting twice is fine
        Assert.Empty((await service.GetAsync(db.Victoria.Id, BookA, default)).Bookmarks);
    }

    [Fact]
    public async Task GetAsync_BringsEverythingForOneBookAndNothingForAnother()
    {
        var (service, db) = Make();
        using var _ = db;
        await service.SetRatingAsync(db.Victoria.Id, BookA, 4, default);
        await service.AddTagAsync(db.Victoria.Id, BookA, "Keeper", default);
        await service.AddBookmarkAsync(db.Victoria.Id, BookA, Mark(5), default);

        var a = await service.GetAsync(db.Victoria.Id, BookA, default);
        var b = await service.GetAsync(db.Victoria.Id, BookB, default);

        Assert.Equal(4, a.Rating);
        Assert.Single(a.Tags);
        Assert.Single(a.Bookmarks);
        Assert.Null(b.Rating);
        Assert.Empty(b.Tags);
        Assert.Empty(b.Bookmarks);
    }
}

public class ListenByIdsTests
{
    private static JellyfinPlayerItem Book(string id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async Task GetItemsByIds_KeepsTheRequestedOrderAndSkipsMissingBooks()
    {
        using var db = new TestDb();
        var player = new FakeJellyfinPlayerClient();
        player.Items.AddRange([Book("11111111111111111111111111111111", "First"), Book("22222222222222222222222222222222", "Second"), Book("33333333333333333333333333333333", "Third")]);
        var service = new ListenService(db.Settings, player, new MemoryCache(new MemoryCacheOptions()));

        var items = await service.GetItemsByIdsAsync(
            new JellyfinSession("t", "u"),
            ["33333333333333333333333333333333", "deadbeefdeadbeefdeadbeefdeadbeef", "11111111111111111111111111111111"],
            default);

        Assert.Equal(["Third", "First"], items.Select(i => i.Title));
        Assert.Equal(["33333333333333333333333333333333", "deadbeefdeadbeefdeadbeefdeadbeef", "11111111111111111111111111111111"], player.Queries.Single().Ids);
    }

    [Fact]
    public async Task GetItemsByIds_NoIdsMeansNoCall()
    {
        using var db = new TestDb();
        var player = new FakeJellyfinPlayerClient();
        var service = new ListenService(db.Settings, player, new MemoryCache(new MemoryCacheOptions()));

        Assert.Empty(await service.GetItemsByIdsAsync(new JellyfinSession("t", "u"), [], default));
        Assert.Empty(player.Queries);
    }
}
