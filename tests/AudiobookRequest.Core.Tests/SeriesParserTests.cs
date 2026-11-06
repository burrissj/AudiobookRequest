using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Tests;

public class SeriesParserTests
{
    [Theory]
    [InlineData("Dragon Chained (The Dragon of 23rd Street, #1)", "dragon of 23rd street", 1)]
    [InlineData("Getting Schooled (Getting Some #1) (Unabridged)", "getting some", 1)]
    [InlineData("Quicksilver (Fae & Alchemy #1) (Unabridged)", "fae and alchemy", 1)]
    [InlineData("When the Moon Hatched (Moonfall, #2)", "moonfall", 2)]
    [InlineData("When the Moon Hatched: Moonfall Series #1", "moonfall", 1)]
    [InlineData("The Dawn of the Cursed Queen: Gods and Monsters, Book 3", "gods and monsters", 3)]
    [InlineData("David Wolf 06 - Smoked Out", "david wolf", 6)]
    [InlineData("The Throne of Broken Gods (2 of 2) [Dramatized Adaptation]", "throne of broken gods", 2)]
    [InlineData("The Throne of Broken Gods (1 of 2) [Dramatized Adaptation]", "throne of broken gods", 1)]
    [InlineData("Heir (Gods & Monsters Book 2)", "gods and monsters", 2)]
    public void Parse_FindsTheSeriesAndNumber(string title, string key, int number)
    {
        var p = SeriesParser.Parse(title);

        Assert.NotNull(p);
        Assert.Equal(key, p.SeriesKey);
        Assert.Equal(number, p.Number);
    }

    [Theory]
    [InlineData("When the Moon Hatched (Moonfall, #2)", "Moonfall")]
    [InlineData("Quicksilver (Fae & Alchemy #1) (Unabridged)", "Fae & Alchemy")]
    [InlineData("When the Moon Hatched: Moonfall Series #1", "Moonfall")]
    [InlineData("The Dawn of the Cursed Queen: Gods and Monsters, Book 3", "Gods and Monsters")]
    public void Parse_KeepsTheSeriesAsTheTitleSpellsIt(string title, string name) => Assert.Equal(name, SeriesParser.Parse(title)!.Name);

    [Theory]
    [InlineData("Harry Potter and the Chamber of Secrets (Full-Cast Edition)")]
    [InlineData("The Ballad of Falling Dragons")]
    [InlineData("Spider-Man Book")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_ReturnsNullRatherThanGuessing(string? title) => Assert.Null(SeriesParser.Parse(title));

    [Fact]
    public void Parse_HyphenInsideAWordIsNotASeparator()
    {
        // "Spider-Man Book 2" must not become the series "man".
        var p = SeriesParser.Parse("Spider-Man Book 2");

        Assert.True(p is null || p.SeriesKey != "man");
    }

    [Theory]
    [InlineData("Amber Nicole", "Amber V. Nicole")]
    [InlineData("amber v nicole", "Amber V. Nicole")]
    [InlineData("J.R. Ward", "J. R. Ward")]
    [InlineData("J.R.R. Tolkien", "J. R. R. Tolkien")]
    [InlineData("Brandon Sanderson", "Brandon R. Sanderson")]
    [InlineData("Martin Luther King", "Martin Luther King Jr.")]
    public void AuthorGroupKey_IsTheSameForTheSameAuthorWrittenTwoWays(string a, string b) =>
        Assert.Equal(TitleMatcher.AuthorGroupKey(a), TitleMatcher.AuthorGroupKey(b));

    [Theory]
    [InlineData("Amber Nicole", "Anna Nicole")]
    [InlineData("John Smith", "Jane Smith")]
    [InlineData("Amber Nicole", "Amber Nicholls")]
    public void AuthorGroupKey_KeepsDifferentAuthorsApart(string a, string b) =>
        Assert.NotEqual(TitleMatcher.AuthorGroupKey(a), TitleMatcher.AuthorGroupKey(b));

    [Fact]
    public void AuthorGroupKey_IsEmptyWithoutLettersOrDigits() => Assert.Equal("", TitleMatcher.AuthorGroupKey("?!"));

    [Theory]
    [InlineData("The Moonfall Series", "moonfall")]
    [InlineData("MOONFALL", "moonfall")]
    [InlineData("Fae & Alchemy", "fae and alchemy")]
    [InlineData("Fae and Alchemy", "fae and alchemy")]
    [InlineData("The Series", "series")]
    public void Normalise_MakesSpellingsCompareEqual(string input, string expected) => Assert.Equal(expected, SeriesParser.Normalise(input));
}
