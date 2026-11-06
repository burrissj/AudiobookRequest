using AudiobookRequest.Core.Recommendations;

namespace AudiobookRequest.Core.Tests;

public class AudiobookSeriesTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("3", true)]
    [InlineData("1-3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("3.5", false)]
    public void IsSingleBook_OnlyMatchesPlainIntegers(string? sequence, bool expected)
    {
        Assert.Equal(expected, AudiobookSeries.IsSingleBook(sequence));
    }

    [Fact]
    public void ParseSingleBookNumber_ParsesPlainIntegerSequences()
    {
        Assert.Equal(3, AudiobookSeries.ParseSingleBookNumber("3"));
        Assert.Null(AudiobookSeries.ParseSingleBookNumber("1-3"));
        Assert.Null(AudiobookSeries.ParseSingleBookNumber(null));
    }

    [Theory]
    [InlineData("1-3", 3)]
    [InlineData("2-2", 1)]
    [InlineData("1", null)]
    [InlineData(null, null)]
    [InlineData("3-1", null)]
    public void ParseBundleCount_ParsesRangesOnly(string? sequence, int? expected)
    {
        Assert.Equal(expected, AudiobookSeries.ParseBundleCount(sequence));
    }
}
