using AudiobookRequest.Core.Audio;
using Xunit;

namespace AudiobookRequest.Core.Tests;

public class FfmpegAudioCombinerTests
{
    private static readonly BookTags Tags = new("Project Hail Mary", "Andy Weir", null);

    [Fact]
    public async Task CombineAsync_SingleFile_SkipsChaptersAndCover()
    {
        var runner = new FakeProcessRunner();
        var combiner = new FfmpegAudioCombiner(runner);
        var output = Path.GetTempFileName() + ".m4b";

        try
        {
            await combiner.CombineAsync(["source.mp3"], output, Tags, coverImagePath: null);

            Assert.Equal(2, runner.Calls.Count(c => c.FileName == "ffmpeg"));
            var normalizeCall = runner.Calls[0];
            Assert.Contains("-c:a", normalizeCall.Arguments);
            Assert.Contains("copy", normalizeCall.Arguments);

            var remuxCall = runner.Calls.Last(c => c.FileName == "ffmpeg");
            Assert.DoesNotContain("-map_metadata", remuxCall.Arguments);
            Assert.DoesNotContain("attached_pic", remuxCall.Arguments);
            Assert.Contains("title=Project Hail Mary", remuxCall.Arguments);
            Assert.Contains("artist=Andy Weir", remuxCall.Arguments);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task CombineAsync_MultiFileWithMismatchedCodecs_ReencodesInsteadOfCopying()
    {
        var runner = new FakeProcessRunner();
        runner.Results["ffprobe"] = new Queue<ProcessResult>([
            new ProcessResult(0, "mp3,44100,2", ""), // stream info file 1
            new ProcessResult(0, "aac,48000,2", ""),  // stream info file 2 (different -> can't copy)
            new ProcessResult(0, "120.0", ""),        // duration file 1
            new ProcessResult(0, "90.0", ""),         // duration file 2
        ]);
        var combiner = new FfmpegAudioCombiner(runner);
        var output = Path.GetTempFileName() + ".m4b";

        try
        {
            await combiner.CombineAsync(["a.mp3", "b.m4a"], output, Tags, coverImagePath: null);

            var concatCall = runner.Calls.First(c => c.FileName == "ffmpeg");
            Assert.Contains("aac", concatCall.Arguments);
            Assert.DoesNotContain("copy", concatCall.Arguments);

            var remuxCall = runner.Calls.Last(c => c.FileName == "ffmpeg");
            Assert.Contains("-map_metadata", remuxCall.Arguments);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task CombineAsync_WithCoverImage_AttachesItAsPicture()
    {
        var runner = new FakeProcessRunner();
        var combiner = new FfmpegAudioCombiner(runner);
        var output = Path.GetTempFileName() + ".m4b";

        try
        {
            await combiner.CombineAsync(["source.mp3"], output, Tags, coverImagePath: "cover.jpg");

            var remuxCall = runner.Calls.Last(c => c.FileName == "ffmpeg");
            Assert.Contains("cover.jpg", remuxCall.Arguments);
            Assert.Contains("attached_pic", remuxCall.Arguments);
            Assert.Contains("mjpeg", remuxCall.Arguments);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
