using AudiobookRequest.Core.Recommendations;

namespace AudiobookRequest.Core.Tests;

public class CategoryCatalogTests
{
    [Fact]
    public void Catalog_HasUniqueIdsAndEveryCategoryCanBeQueried()
    {
        Assert.Equal(CategoryCatalog.All.Count, CategoryCatalog.All.Select(c => c.Id).Distinct().Count());
        Assert.All(CategoryCatalog.All, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.False(string.IsNullOrWhiteSpace(c.Description));
            Assert.True(c.AudibleCategoryId is not null || c.Keywords is not null, $"{c.Id} has nothing to query by");
            Assert.Contains(c.Sort, new[] { "BestSellers", "-ReleaseDate", "Relevance" });
        });
        Assert.DoesNotContain(CategoryCatalog.All, c => c.Id == BrowseCategory.CustomId);
    }

    [Fact]
    public void Catalog_MixesAllThreeLayouts()
    {
        Assert.Contains(CategoryCatalog.All, c => c.Layout == CategoryLayout.Grid);
        Assert.Contains(CategoryCatalog.All, c => c.Layout == CategoryLayout.Tilted);
        Assert.Contains(CategoryCatalog.All, c => c.Layout == CategoryLayout.Shelf);
    }

    [Theory]
    [InlineData("scary", "spooky")]
    [InlineData("COZY", "cozy-mystery")]
    [InlineData("vampire", "paranormal-romance")]
    [InlineData("graphic audio", "graphic-audio")]
    public void Search_FindsCategoriesBySynonymTitleOrDescription(string query, string expectedId) =>
        Assert.Contains(CategoryCatalog.Search(query), c => c.Id == expectedId);

    [Fact]
    public void Search_RequiresEveryWordToMatch()
    {
        var results = CategoryCatalog.Search("romance fantasy");

        Assert.Contains(results, c => c.Id == "romantasy");
        Assert.DoesNotContain(results, c => c.Id == "cozy-mystery");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzzqqqq")]
    public void Search_NothingTypedOrNothingMatching_ReturnsEmpty(string query) =>
        Assert.Empty(CategoryCatalog.Search(query));

    [Fact]
    public void Find_IsCaseInsensitive() => Assert.Equal("spooky", CategoryCatalog.Find("SPOOKY")?.Id);

    [Fact]
    public void Custom_BuildsASearchCategoryFromFreeText()
    {
        var custom = BrowseCategory.Custom("  dragons  ");

        Assert.Equal(BrowseCategory.CustomId, custom.Id);
        Assert.Equal("dragons", custom.Title);
        Assert.Equal("dragons", custom.Keywords);
        Assert.Equal("Relevance", custom.Sort);
    }

    [Fact]
    public void PickForHome_SameSeedGivesTheSamePage_DifferentSeedShuffles()
    {
        var a = CategoryCatalog.PickForHome(42, 6).Select(c => c.Id).ToList();
        var again = CategoryCatalog.PickForHome(42, 6).Select(c => c.Id).ToList();
        var others = Enumerable.Range(100, 20).Select(s => CategoryCatalog.PickForHome(s, 6).Select(c => c.Id).ToList());

        Assert.Equal(a, again);
        Assert.Contains(others, o => !o.SequenceEqual(a));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(2026)]
    public void PickForHome_AlwaysHasVarietyAndNoDuplicates(int seed)
    {
        var picked = CategoryCatalog.PickForHome(seed, 6);

        Assert.Equal(6, picked.Count);
        Assert.Equal(6, picked.Select(c => c.Id).Distinct().Count());
        Assert.Contains(picked, c => c.Layout == CategoryLayout.Tilted);
        Assert.Contains(picked, c => c.Layout == CategoryLayout.Shelf);
        Assert.True(picked.Count(c => c.Layout == CategoryLayout.Tilted) <= 2);
        Assert.NotEqual(CategoryLayout.Tilted, picked[0].Layout);
    }

    [Fact]
    public void PickForHome_AskingForMoreThanExistReturnsEverything() =>
        Assert.Equal(CategoryCatalog.All.Count, CategoryCatalog.PickForHome(1, 999).Count);
}
