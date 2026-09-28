using AudiobookRequest.Core.Library;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class NamingPatternServiceTests
{
    [Fact]
    public void BuildRelativePath_SubstitutesAllTokens()
    {
        var path = NamingPatternService.BuildRelativePath("{Author}/{Series}/{Title}", "Andy Weir", "Project Hail Mary", "Standalone");

        Assert.Equal(Path.Combine("Andy Weir", "Standalone", "Project Hail Mary"), path);
    }

    [Fact]
    public void BuildRelativePath_CollapsesMissingSeriesSegment()
    {
        var path = NamingPatternService.BuildRelativePath("{Author}/{Series}/{Title}", "Andy Weir", "Project Hail Mary", null);

        Assert.Equal(Path.Combine("Andy Weir", "Project Hail Mary"), path);
    }

    [Theory]
    [InlineData("Sci-Fi: Vol. 1?", "Sci-Fi_ Vol. 1_")]
    [InlineData("Weird*Name", "Weird_Name")]
    public void BuildRelativePath_SanitizesInvalidCharacters(string title, string expectedSegment)
    {
        var path = NamingPatternService.BuildRelativePath("{Title}", "Author", title, null);

        Assert.Equal(expectedSegment, path);
    }
}
