using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Tests;

public class TitleMatcherTests
{
    [Theory]
    [InlineData("Project Hail Mary (Unabridged)", "project hail mary")]
    [InlineData("The Final Empire [Graphic Audio]", "final empire")]
    [InlineData("Harry Potter & the Sorcerer's Stone", "harry potter and the sorcerer s stone")]
    [InlineData("A Novel Idea", "novel idea")]
    [InlineData("Project Hail Mary: A Novel", "project hail mary")]
    public void NormalizeTitle_StripsEditionNoiseAndArticles(string raw, string expected) =>
        Assert.Equal(expected, TitleMatcher.NormalizeTitle(raw));

    [Fact]
    public void NormalizeTitle_DoesNotMangleWordsContainingEditionWords() =>
        Assert.Equal("novella", TitleMatcher.NormalizeTitle("A Novella"));

    [Theory]
    [InlineData("Mistborn: The Final Empire", "The Final Empire", true)]
    [InlineData("The Final Empire", "Mistborn: The Final Empire", true)]
    [InlineData("Mistborn: The Final Empire", "Mistborn - The Final Empire", true)]
    [InlineData("Project Hail Mary (Unabridged)", "Project Hail Mary", true)]
    [InlineData("Dune", "Dune: Messiah", false)]
    [InlineData("Mistborn: The Final Empire", "Mistborn: The Well of Ascension", false)]
    [InlineData("Dune", "Emma", false)]
    public void IsSimilarTitle_MatchesSamBookNotJustSharedSeriesPrefix(string a, string b, bool expected) =>
        Assert.Equal(expected, TitleMatcher.IsSimilarTitle(a, b));

    [Theory]
    [InlineData("J.R.R. Tolkien", "J. R. R. Tolkien", true)]
    [InlineData("Brandon Sanderson", "brandon sanderson", true)]
    [InlineData("John Smith", "J Smith", true)]
    [InlineData("Brandon Sanderson", "Michael Kramer", false)]
    [InlineData("John Smith", "Jane Smith", true)]
    [InlineData("John Smith", "Adam Smith", false)]
    public void AuthorsMatch_ToleratesInitialsAndPunctuation(string a, string b, bool expected) =>
        Assert.Equal(expected, TitleMatcher.AuthorsMatch(a, b));
}
