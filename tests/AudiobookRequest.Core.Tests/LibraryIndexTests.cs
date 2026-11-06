using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Core.Tests;

public class LibraryIndexTests
{
    private static LibraryIndex Index(params (string Title, string? Artists)[] books) =>
        new(books.Select(b => new LibraryEntry(b.Title, b.Artists)));

    [Fact]
    public void IsOwned_ExactTitleIgnoresCaseAndAuthor()
    {
        var index = Index(("Project Hail Mary", "Ray Porter"));
        Assert.True(index.IsOwned("project hail mary", "Andy Weir"));
    }

    [Fact]
    public void IsOwned_EditionSuffixCountsAsOwnedWhenAuthorMatches()
    {
        var index = Index(("Quicksilver (Unabridged)", "Callie Hart|Anthony Palmini"));
        Assert.True(index.IsOwned("Quicksilver", "Callie Hart"));
    }

    [Fact]
    public void IsOwned_SameTitleDifferentAuthorIsAnotherBook()
    {
        var index = Index(("Inferno (Unabridged)", "Dan Brown"));
        Assert.False(index.IsOwned("Inferno", "Dante Alighieri"));
    }

    [Fact]
    public void IsOwned_UnknownLibraryAuthorFallsBackToTitleAlone()
    {
        var index = Index(("Quicksilver (Unabridged)", null));
        Assert.True(index.IsOwned("Quicksilver", "Callie Hart"));
    }

    [Fact]
    public void FindSimilar_FlagsSubtitleVariantWithoutCallingItOwned()
    {
        var index = Index(("The Final Empire", "Brandon Sanderson"));

        Assert.False(index.IsOwned("Mistborn: The Final Empire", "Brandon Sanderson"));
        Assert.Equal("The Final Empire", index.FindSimilar("Mistborn: The Final Empire", "Brandon Sanderson"));
    }

    [Fact]
    public void FindSimilar_IsNullForAnActuallyOwnedBook()
    {
        var index = Index(("Project Hail Mary", "Andy Weir"));
        Assert.Null(index.FindSimilar("Project Hail Mary", "Andy Weir"));
    }

    [Fact]
    public void FindSimilar_IgnoresDifferentAuthors()
    {
        var index = Index(("The Final Empire", "Someone Else"));
        Assert.Null(index.FindSimilar("Mistborn: The Final Empire", "Brandon Sanderson"));
    }
}
