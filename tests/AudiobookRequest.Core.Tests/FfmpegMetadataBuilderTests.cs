using AudiobookRequest.Core.Audio;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class FfmpegMetadataBuilderTests
{
    [Fact]
    public void BuildChapters_ProducesCumulativeStartAndEndTimes()
    {
        var durations = new[] { TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(90) };

        var ffmetadata = FfmpegMetadataBuilder.BuildChapters(durations);

        Assert.Contains(";FFMETADATA1", ffmetadata);
        Assert.Contains("START=0", ffmetadata);
        Assert.Contains("END=120000", ffmetadata);
        Assert.Contains("title=Chapter 1", ffmetadata);
        Assert.Contains("START=120000", ffmetadata);
        Assert.Contains("END=210000", ffmetadata);
        Assert.Contains("title=Chapter 2", ffmetadata);
    }

    [Fact]
    public void BuildConcatList_EscapesSingleQuotesInPaths()
    {
        var list = FfmpegMetadataBuilder.BuildConcatList(["C:/books/Mark's Book/part1.mp3"]);

        Assert.Equal("file 'C:/books/Mark'\\''s Book/part1.mp3'" + Environment.NewLine, list);
    }

    [Fact]
    public void ParseFfprobeDuration_ParsesFractionalSeconds()
    {
        var duration = FfmpegMetadataBuilder.ParseFfprobeDuration("125.484000\n");

        Assert.Equal(TimeSpan.FromSeconds(125.484), duration);
    }
}
