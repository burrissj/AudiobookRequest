using System.Globalization;
using System.Text;

namespace AudiobookRequest.Core.Audio;

public record BookTags(string Title, string Author, string? Series);

public static class FfmpegMetadataBuilder
{
    /// <summary>
    /// Builds an FFMETADATA1 file: one [CHAPTER] block per source file, with start/end times derived
    /// cumulatively from each file's own duration (accurate as long as the concat step is a lossless
    /// stream copy, which is what we attempt first).
    /// </summary>
    public static string BuildChapters(IReadOnlyList<TimeSpan> sourceDurations)
    {
        var sb = new StringBuilder();
        sb.AppendLine(";FFMETADATA1");

        var cursorMs = 0L;
        for (var i = 0; i < sourceDurations.Count; i++)
        {
            var endMs = cursorMs + (long)sourceDurations[i].TotalMilliseconds;
            sb.AppendLine("[CHAPTER]");
            sb.AppendLine("TIMEBASE=1/1000");
            sb.AppendLine(FormattableString.Invariant($"START={cursorMs}"));
            sb.AppendLine(FormattableString.Invariant($"END={endMs}"));
            sb.AppendLine(FormattableString.Invariant($"title=Chapter {i + 1}"));
            cursorMs = endMs;
        }

        return sb.ToString();
    }

    /// <summary>Builds an ffconcat demuxer list file listing each source file, escaping single quotes per ffmpeg's syntax.</summary>
    public static string BuildConcatList(IReadOnlyList<string> sourceFiles)
    {
        var sb = new StringBuilder();
        foreach (var file in sourceFiles)
        {
            var escaped = file.Replace("'", "'\\''");
            sb.AppendLine($"file '{escaped}'");
        }
        return sb.ToString();
    }

    public static TimeSpan ParseFfprobeDuration(string ffprobeOutput)
    {
        var trimmed = ffprobeOutput.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            throw new FormatException($"Could not parse ffprobe duration output: '{ffprobeOutput}'");
        }
        return TimeSpan.FromSeconds(seconds);
    }
}
