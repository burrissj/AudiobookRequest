using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Tests;

public class ListenGroupingTests
{
    private static readonly JellyfinSession Session = new("tok", "uid", "abr-0123456789abcdef01234567", "Safari on iPhone");

    private static JellyfinPlayerItem Book(string id, string name, string? author = "Ann", bool played = false, bool cover = false) => new()
    {
        Id = id,
        Name = name,
        ArtistItems = author is null ? null : [new JellyfinNameId { Name = author }],
        UserData = new JellyfinPlayerUserData { Played = played },
        ImageTags = cover ? new Dictionary<string, string> { ["Primary"] = "t" } : null,
    };

    private static (ListenService Service, FakeJellyfinPlayerClient Player, TestDb Db) Make()
    {
        var db = new TestDb();
        var player = new FakeJellyfinPlayerClient();
        return (new ListenService(db.Settings, player, new MemoryCache(new MemoryCacheOptions())), player, db);
    }

    [Fact]
    public void ByAuthor_OnePilePerAuthor_AZ_UnknownLast()
    {
        var library = new[]
        {
            Book("1", "Zeta", "Bob"),
            Book("2", "Alpha", "Ann"),
            Book("3", "Nameless", author: null),
            Book("4", "Beta", "Ann"),
        };

        var piles = ListenService.GroupBooks(library, ListenGrouping.Author);

        Assert.Equal(["Ann", "Bob", "Unknown author"], piles.Select(p => p.Name));
        Assert.Equal(["Alpha", "Beta"], piles[0].Books.Select(b => b.Name));
        Assert.Equal("-", piles[2].Key);
    }

    [Fact]
    public void ByAuthor_DifferentSpellingsOfOneAuthorAreOnePile()
    {
        var library = new[]
        {
            Book("1", "One", "J.R. Ward"),
            Book("2", "Two", "J. R. Ward"),
            Book("3", "Three", "J.R. Ward"),
        };

        var piles = ListenService.GroupBooks(library, ListenGrouping.Author);

        var pile = Assert.Single(piles);
        Assert.Equal(3, pile.Books.Count);
        Assert.Equal("J.R. Ward", pile.Name); // the spelling most books use
    }

    [Fact]
    public void ByAuthor_BooksComeSeriesBySeriesInOrder_StandalonesAfter()
    {
        var library = new[]
        {
            Book("s", "A Stand Alone"),
            Book("b2", "Spare (Gods & Monsters Book 2)"),
            Book("a2", "Second (Dragon Street, #2)"),
            Book("b1", "Heir (Gods & Monsters Book 1)"),
            Book("a1", "First (Dragon Street, #1)"),
        };

        var pile = Assert.Single(ListenService.GroupBooks(library, ListenGrouping.Author));

        Assert.Equal(["a1", "a2", "b1", "b2", "s"], pile.Books.Select(b => b.Id));
    }

    [Fact]
    public void BySeries_OnePilePerSeriesInBookOrder_AndTheLooseBooksLast()
    {
        var library = new[]
        {
            Book("x", "Unattached"),
            Book("g3", "Crown (Gods & Monsters Book 3)"),
            Book("m1", "When the Moon Hatched: Moonfall Series #1"),
            Book("g1", "Heir (Gods and Monsters, #1)"),
            Book("g2", "Spare (The Gods & Monsters Series Book 2)"),
        };

        var piles = ListenService.GroupBooks(library, ListenGrouping.Series);

        Assert.Equal(["gods and monsters", "moonfall", "-"], piles.Select(p => p.Key));
        Assert.Equal(["g1", "g2", "g3"], piles[0].Books.Select(b => b.Id));
        Assert.Equal("Not in a series", piles[2].Name);
        Assert.Equal(["x"], piles[2].Books.Select(b => b.Id));
        Assert.Equal("Ann", piles[0].By);
    }

    [Fact]
    public void BySeries_NoLoosePileWhenEveryBookIsInASeries()
    {
        var piles = ListenService.GroupBooks([Book("1", "Heir (Gods & Monsters Book 1)")], ListenGrouping.Series);

        Assert.DoesNotContain(piles, p => p.Key == "-");
    }

    [Fact]
    public void Item_CarriesItsSeriesAndNumber()
    {
        var item = ListenService.ToItem(Book("1", "Spare (Gods & Monsters Book 2)"));
        var plain = ListenService.ToItem(Book("2", "Dune"));

        Assert.Equal("Gods & Monsters", item.Series);
        Assert.Equal(2m, item.SeriesNumber);
        Assert.Null(plain.Series);
        Assert.Null(plain.SeriesNumber);
    }

    [Fact]
    public async Task Groups_CountBooksAndFinishedAndPickCoversForTheStack()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.AddRange(
        [
            Book("a1", "First (Dragon Street, #1)", played: true, cover: true),
            Book("a2", "Second (Dragon Street, #2)", cover: false),
            Book("a3", "Third (Dragon Street, #3)", cover: true),
        ]);

        var result = await service.GetGroupsAsync(Session, ListenGrouping.Series, BookMetadataIndex.Empty, default);

        var g = Assert.Single(result.Groups);
        Assert.Equal(3, result.Pending); // none of the three has been asked about on Audible
        Assert.Equal("Dragon Street", g.Name);
        Assert.Equal(3, g.Count);
        Assert.Equal(1, g.Finished);
        Assert.Equal(["a1", "a3"], g.CoverIds);
    }

    [Fact]
    public async Task GroupBooks_AreReadFreshSoProgressIsCurrent()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.AddRange([Book("a1", "First (Dragon Street, #1)"), Book("a2", "Second (Dragon Street, #2)")]);
        await service.GetGroupsAsync(Session, ListenGrouping.Series, BookMetadataIndex.Empty, default); // fills the cached library
        player.Queries.Clear();

        var books = await service.GetGroupBooksAsync(Session, ListenGrouping.Series, "dragon street", BookMetadataIndex.Empty, default);

        Assert.Equal(["a1", "a2"], books!.Select(b => b.Id));
        var asked = Assert.Single(player.Queries);
        Assert.Equal(["a1", "a2"], asked.Ids);
    }

    [Fact]
    public void ByAuthor_TheSameAuthorWithAndWithoutAMiddleInitialIsOnePile()
    {
        var library = new[]
        {
            Book("1", "One", "Amber Nicole"),
            Book("2", "Two", "Amber V. Nicole"),
            Book("3", "Three", "Amber V Nicole"),
            Book("4", "Four", "Amber Nicholls"),
            Book("5", "Five", "Anna Nicole"),
        };

        var piles = ListenService.GroupBooks(library, ListenGrouping.Author);

        Assert.Equal(["Amber Nicholls", "Amber V. Nicole", "Anna Nicole"], piles.Select(p => p.Name));
        Assert.Equal(3, piles[1].Books.Count);
    }

    [Fact]
    public void BySeries_AudiblesSeriesBeatsTheTitle_AndKeepsBooksTogetherWhateverTheirTitlesSay()
    {
        var library = new[]
        {
            Book("1", "The Final Empire"),
            Book("2", "Well of Ascension"),
            Book("3", "Mistborn: The Hero of Ages (Mistborn, #3)"), // not known to Audible yet: the title says it
        };
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo> { ["1"] = new("Mistborn", 1), ["2"] = new("The Mistborn Series", 2) },
            new HashSet<string> { "1", "2" });

        var pile = Assert.Single(ListenService.GroupBooks(library, ListenGrouping.Series, known));

        Assert.Equal(["1", "2", "3"], pile.Books.Select(b => b.Id));
        Assert.Equal("Mistborn", pile.Name);
    }

    [Fact]
    public void BySeries_ATitlesShorterSeriesNameJoinsTheAudibleOneItStartsThe_ButOnlyWhenThatIsUnambiguous()
    {
        var library = new[]
        {
            Book("1", "The Final Empire"),
            Book("2", "The Hero of Ages (Mistborn, #3)"),  // Audible has no listing for this one
            Book("3", "Words of Radiance (Stormlight, #2)"), // "Stormlight" starts two Audible series: not guessed
            Book("4", "Oathbringer"),
            Book("5", "Rhythm of War"),
        };
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo>
            {
                ["1"] = new("The Mistborn Saga", 1),
                ["4"] = new("Stormlight Archive", 3),
                ["5"] = new("Stormlight Chronicles", 4),
            },
            new HashSet<string> { "1", "4", "5" });

        var piles = ListenService.GroupBooks(library, ListenGrouping.Series, known);

        Assert.Equal(["mistborn saga", "stormlight", "stormlight archive", "stormlight chronicles"], piles.Select(p => p.Key));
        Assert.Equal(["1", "2"], piles.Single(p => p.Key == "mistborn saga").Books.Select(b => b.Id));
    }

    [Fact]
    public void BySeries_AudiblesNumberOrdersTheBooks_UnnumberedOnesLast()
    {
        var library = new[] { Book("a", "Prequel"), Book("b", "Second"), Book("c", "First") };
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo> { ["a"] = new("Saga", null), ["b"] = new("Saga", 2), ["c"] = new("Saga", 1) },
            new HashSet<string>());

        var pile = Assert.Single(ListenService.GroupBooks(library, ListenGrouping.Series, known));

        Assert.Equal(["c", "b", "a"], pile.Books.Select(b => b.Id));
    }

    [Fact]
    public async Task GroupBooks_CarryAudiblesSeriesAndNumberToTheTiles()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.AddRange([Book("a1", "The Final Empire"), Book("a2", "Well of Ascension")]);
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo> { ["a1"] = new("Mistborn", 1), ["a2"] = new("Mistborn", 2) },
            new HashSet<string> { "a1", "a2" });

        var books = await service.GetGroupBooksAsync(Session, ListenGrouping.Series, "mistborn", known, default);

        Assert.Equal([("Mistborn", 1m), ("Mistborn", 2m)], books!.Select(b => (b.Series!, b.SeriesNumber!.Value)));
    }

    [Fact]
    public void Corrections_AnAuthorFixMovesTheBookToThatAuthor_EvenWhenJellyfinHasNone()
    {
        var library = new[] { Book("1", "One", author: null), Book("2", "Two", "Amber Nicole"), Book("3", "Three", "Typo Nme") };
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo>(),
            new HashSet<string>(),
            new Dictionary<string, string> { ["1"] = "Amber V. Nicole", ["3"] = "Real Name" });

        var piles = ListenService.GroupBooks(library, ListenGrouping.Author, known);

        Assert.Equal(["Amber V. Nicole", "Real Name"], piles.Select(p => p.Name));
        Assert.Equal(["2", "1"], piles[0].Books.Select(b => b.Id).OrderByDescending(id => id)); // both Ambers, one pile
        Assert.DoesNotContain(piles, p => p.Name is "Unknown author" or "Typo Nme");
    }

    [Fact]
    public void Corrections_NotInASeriesBeatsWhatTheTitleSays()
    {
        var library = new[] { Book("1", "Heir (Gods & Monsters Book 1)"), Book("2", "Spare (Gods & Monsters Book 2)") };
        var known = new BookMetadataIndex(new Dictionary<string, SeriesInfo>(), new HashSet<string>(), null, new HashSet<string> { "2" });

        var piles = ListenService.GroupBooks(library, ListenGrouping.Series, known);

        Assert.Equal(["1"], piles.Single(p => p.Key == "gods and monsters").Books.Select(b => b.Id));
        Assert.Equal(["2"], piles.Single(p => p.Key == "-").Books.Select(b => b.Id));
    }

    [Fact]
    public void Corrections_ASeriesAddedByHandGathersTheBooks()
    {
        var library = new[] { Book("1", "Untitled Thing"), Book("2", "Another Thing") };
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo> { ["1"] = new("My Series", 1), ["2"] = new("My Series", 2) },
            new HashSet<string>());

        var pile = Assert.Single(ListenService.GroupBooks(library, ListenGrouping.Series, known));

        Assert.Equal(["1", "2"], pile.Books.Select(b => b.Id));
        Assert.Equal("My Series", pile.Name);
    }

    [Fact]
    public void WithMetadata_ShowsTheCorrectedAuthorAndSeries()
    {
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo> { ["1"] = new("Saga", 2) },
            new HashSet<string>(),
            new Dictionary<string, string> { ["1"] = "Right Author" },
            new HashSet<string> { "2" });
        var corrected = ListenService.ToItem(Book("1", "Heir (Gods & Monsters Book 1)", "Wrong"));
        var detached = ListenService.ToItem(Book("2", "Spare (Gods & Monsters Book 2)"));
        var untouched = ListenService.ToItem(Book("3", "Plain"));

        var one = ListenService.WithMetadata(corrected, known);
        var two = ListenService.WithMetadata(detached, known);

        Assert.Equal(("Right Author", "Saga", (decimal?)2), (one.Author, one.Series, one.SeriesNumber));
        Assert.Equal((null, null), (two.Series, two.SeriesNumber)); // the title's series is hidden when an admin said "no series"
        Assert.Equal(untouched, ListenService.WithMetadata(untouched, known));
    }

    [Fact]
    public void WithMetadata_TheBooksPageAgreesWithTheLibrary()
    {
        var known = new BookMetadataIndex(
            new Dictionary<string, SeriesInfo> { ["1"] = new("Saga", 2) },
            new HashSet<string>(),
            new Dictionary<string, string> { ["1"] = "Right Author" },
            new HashSet<string> { "2" });

        var one = ListenService.WithMetadata(ListenService.ToDetail(Book("1", "Plain", "Wrong")), known);
        var two = ListenService.WithMetadata(ListenService.ToDetail(Book("2", "Spare (Gods & Monsters Book 2)")), known);
        var three = ListenService.WithMetadata(ListenService.ToDetail(Book("3", "Heir (Gods & Monsters Book 1)")), known);

        Assert.Equal(("Right Author", "Saga", (decimal?)2), (one.Author, one.Series, one.SeriesNumber));
        Assert.Equal((null, null), (two.Series, two.SeriesNumber));
        Assert.Equal(("Gods & Monsters", (decimal?)1), (three.Series, three.SeriesNumber)); // from the title, as before
    }

    [Fact]
    public async Task Pending_CountsBooksNotYetAskedAbout()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.AddRange([Book("a1", "One"), Book("a2", "Two"), Book("a3", "Three")]);
        var known = new BookMetadataIndex(new Dictionary<string, SeriesInfo>(), new HashSet<string> { "a1" });

        var result = await service.GetGroupsAsync(Session, ListenGrouping.Series, known, default);

        Assert.Equal(2, result.Pending);
    }

    [Fact]
    public async Task GroupBooks_UnknownPileIsNull()
    {
        var (service, player, db) = Make();
        using var _ = db;
        player.Items.Add(Book("a1", "First (Dragon Street, #1)"));

        Assert.Null(await service.GetGroupBooksAsync(Session, ListenGrouping.Series, "nothing", BookMetadataIndex.Empty, default));
    }
}
