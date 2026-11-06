using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Tests;

/// <summary>A request says "(Gods &amp; Monsters, #3)"; Jellyfin's copy says ": Gods and Monsters, Book 3". Same book.</summary>
public class SeriesTitleMatchingTests
{
    private const string Request = "The Dawn of the Cursed Queen (Gods & Monsters, #3)";
    private const string Jellyfin = "The Dawn of the Cursed Queen: Gods and Monsters, Book 3";

    [Fact]
    public void TheSameNumberedBookWrittenTwoWaysIsTheSameBook()
    {
        Assert.True(TitleMatcher.IsSimilarTitle(Request, Jellyfin));
        Assert.True(TitleMatcher.IsSimilarTitle(Jellyfin, Request));
        Assert.True(TitleMatcher.IsSameSeriesEntry(Request, Jellyfin));
    }

    [Theory]
    [InlineData("Dragon Chained (The Dragon of 23rd Street, #1)", "Dragon Chained: The Dragon of 23rd Street #1")]
    [InlineData("When the Moon Hatched (Moonfall, #1)", "When the Moon Hatched: Moonfall Series #1")]
    [InlineData("The Wrath of the Fallen (Gods & Monsters, #4)", "The Wrath of the Fallen: Gods and Monsters, Book 4")]
    public void OtherRealTitlesOfTheSameShape(string a, string b) => Assert.True(TitleMatcher.IsSimilarTitle(a, b));

    [Fact]
    public void ADifferentNumberInTheSameSeriesIsADifferentBook() =>
        Assert.False(TitleMatcher.IsSimilarTitle(Request, "The Dawn of the Cursed Queen: Gods and Monsters, Book 4"));

    [Fact]
    public void ADifferentBookWithTheSameNumberInTheSameSeriesIsADifferentBook() =>
        Assert.False(TitleMatcher.IsSimilarTitle(Request, "The Book of Azrael: Gods and Monsters, Book 3"));

    [Fact]
    public void ADifferentSeriesIsADifferentBook() =>
        Assert.False(TitleMatcher.IsSimilarTitle(Request, "The Dawn of the Cursed Queen: Another Saga, Book 3"));

    [Fact]
    public void TitlesWithNoSeriesInformationAreNotAffected()
    {
        Assert.False(TitleMatcher.IsSameSeriesEntry("Dune", "Dune: Messiah"));
        Assert.False(TitleMatcher.IsSimilarTitle("Dune", "Dune: Messiah"));
        Assert.True(TitleMatcher.IsSimilarTitle("Mistborn: The Final Empire", "The Final Empire"));
    }

    [Fact]
    public void TheNameBeforeTheSeriesTag()
    {
        Assert.Equal("dawn of the cursed queen", TitleMatcher.NormalizedHead(Request));
        Assert.Equal("dawn of the cursed queen", TitleMatcher.NormalizedHead(Jellyfin));
        Assert.Equal("plain title", TitleMatcher.NormalizedHead("Plain Title"));
    }

    [Fact]
    public void TheLibraryCountsItAsOwned_NotJustSimilar()
    {
        var index = new LibraryIndex([new LibraryEntry(Jellyfin, "Amber V. Nicole|Some Narrator")]);

        Assert.True(index.IsOwned(Request, "Amber V. Nicole"));
        Assert.Null(index.FindSimilar(Request, "Amber V. Nicole"));
    }

    [Fact]
    public void TheLibraryStillRefusesTheWrongAuthor()
    {
        var index = new LibraryEntry(Jellyfin, "Someone Else");

        Assert.False(new LibraryIndex([index]).IsOwned(Request, "Amber V. Nicole"));
    }

    [Fact]
    public void TheLibraryDoesNotOwnTheNextBook()
    {
        var index = new LibraryIndex([new LibraryEntry(Jellyfin, "Amber V. Nicole")]);

        Assert.False(index.IsOwned("The Wrath of the Fallen (Gods & Monsters, #4)", "Amber V. Nicole"));
    }
}
