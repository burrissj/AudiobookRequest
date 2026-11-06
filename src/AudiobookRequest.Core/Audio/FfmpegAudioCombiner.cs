namespace AudiobookRequest.Core.Audio;

public class FfmpegAudioCombiner(IProcessRunner processRunner, string ffmpegPath = "ffmpeg", string ffprobePath = "ffprobe") : IAudioCombiner
{
    public async Task CombineAsync(IReadOnlyList<string> sourceFiles, string outputPath, BookTags tags, string? coverImagePath, bool embedTags = true, CancellationToken ct = default, bool requireAac = false)
    {
        if (sourceFiles.Count == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(sourceFiles));
        }

        var workDir = Path.Combine(Path.GetTempPath(), "abr-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var intermediatePath = Path.Combine(workDir, "combined.m4b");
            await ConcatOrCopyAsync(sourceFiles, intermediatePath, requireAac, ct);

            string? chaptersPath = null;
            if (sourceFiles.Count > 1)
            {
                var durations = new List<TimeSpan>();
                foreach (var file in sourceFiles)
                {
                    durations.Add(await GetDurationAsync(file, ct));
                }

                chaptersPath = Path.Combine(workDir, "chapters.txt");
                await File.WriteAllTextAsync(chaptersPath, FfmpegMetadataBuilder.BuildChapters(durations), ct);
            }

            await RemuxWithMetadataAsync(intermediatePath, outputPath, tags, chaptersPath, coverImagePath, embedTags, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup of scratch dir */ }
        }
    }

    private async Task<TimeSpan> GetDurationAsync(string filePath, CancellationToken ct)
    {
        var result = await processRunner.RunAsync(ffprobePath, [
            "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", filePath
        ], ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"ffprobe failed to read duration of '{filePath}': {result.StandardError}");
        }
        return FfmpegMetadataBuilder.ParseFfprobeDuration(result.StandardOutput);
    }

    private async Task<(string Codec, string SampleRate, string Channels)> GetAudioStreamInfoAsync(string filePath, CancellationToken ct)
    {
        var result = await processRunner.RunAsync(ffprobePath, [
            "-v", "error", "-select_streams", "a:0",
            "-show_entries", "stream=codec_name,sample_rate,channels",
            "-of", "csv=p=0", filePath
        ], ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"ffprobe failed to inspect '{filePath}': {result.StandardError}");
        }
        var parts = result.StandardOutput.Trim().Split(',');
        return (parts.ElementAtOrDefault(0) ?? "", parts.ElementAtOrDefault(1) ?? "", parts.ElementAtOrDefault(2) ?? "");
    }

    // Codecs that belong in an .m4b as they are.
    private static bool IsMp4Audio(string codec) => codec is "aac" or "alac";

    private async Task ConcatOrCopyAsync(IReadOnlyList<string> sourceFiles, string outputPath, bool requireAac, CancellationToken ct)
    {
        if (sourceFiles.Count == 1)
        {
            var mustReencode = requireAac && !IsMp4Audio((await GetAudioStreamInfoAsync(sourceFiles[0], ct)).Codec.ToLowerInvariant());
            if (!mustReencode && await TryRunFfmpegAsync(BuildSingleFileArgs(sourceFiles[0], outputPath, reencode: false), outputPath, ct))
            {
                return;
            }
            if (await TryRunFfmpegAsync(BuildSingleFileArgs(sourceFiles[0], outputPath, reencode: true), outputPath, ct))
            {
                return;
            }
            throw new InvalidOperationException($"ffmpeg failed to normalize '{sourceFiles[0]}' to .m4b.");
        }

        // Prefer a lossless stream copy, but only when every source shares the same codec/rate/channels
        // (ffmpeg's concat demuxer doesn't reliably fail on a mismatch — it can silently produce a broken file).
        var infos = new List<(string Codec, string SampleRate, string Channels)>();
        foreach (var file in sourceFiles)
        {
            infos.Add(await GetAudioStreamInfoAsync(file, ct));
        }
        var canCopy = infos.Distinct().Count() == 1 && !(requireAac && infos.Any(i => !IsMp4Audio(i.Codec.ToLowerInvariant())));

        var concatListPath = outputPath + ".concat.txt";
        await File.WriteAllTextAsync(concatListPath, FfmpegMetadataBuilder.BuildConcatList(sourceFiles), ct);
        try
        {
            if (canCopy && await TryRunFfmpegAsync(BuildConcatArgs(concatListPath, outputPath, reencode: false), outputPath, ct))
            {
                return;
            }
            if (await TryRunFfmpegAsync(BuildConcatArgs(concatListPath, outputPath, reencode: true), outputPath, ct))
            {
                return;
            }
            throw new InvalidOperationException("ffmpeg failed to concatenate source files, even with re-encoding.");
        }
        finally
        {
            try { File.Delete(concatListPath); } catch (IOException) { /* still held by a stopped ffmpeg; the work folder is cleared later */ }
        }
    }

    private static List<string> BuildSingleFileArgs(string source, string output, bool reencode)
    {
        var args = new List<string> { "-y", "-i", source, "-vn", "-c:a", reencode ? "aac" : "copy" };
        if (reencode)
        {
            args.Add("-b:a");
            args.Add("64k");
        }
        args.Add(output);
        return args;
    }

    private static List<string> BuildConcatArgs(string concatListPath, string output, bool reencode)
    {
        var args = new List<string> { "-y", "-f", "concat", "-safe", "0", "-i", concatListPath, "-vn", "-c:a", reencode ? "aac" : "copy" };
        if (reencode)
        {
            args.Add("-b:a");
            args.Add("64k");
        }
        args.Add(output);
        return args;
    }

    private async Task<bool> TryRunFfmpegAsync(IReadOnlyList<string> args, string expectedOutputPath, CancellationToken ct)
    {
        var result = await processRunner.RunAsync(ffmpegPath, args, ct);
        return result.Success && File.Exists(expectedOutputPath);
    }

    private async Task RemuxWithMetadataAsync(string inputPath, string outputPath, BookTags tags, string? chaptersPath, string? coverImagePath, bool embedTags, CancellationToken ct)
    {
        var args = new List<string> { "-y", "-i", inputPath };
        var nextInputIndex = 1;
        var metadataInputIndex = -1;
        var coverInputIndex = -1;

        if (chaptersPath is not null)
        {
            args.AddRange(["-f", "ffmetadata", "-i", chaptersPath]);
            metadataInputIndex = nextInputIndex++;
        }
        if (coverImagePath is not null && embedTags)
        {
            args.AddRange(["-i", coverImagePath]);
            coverInputIndex = nextInputIndex++;
        }

        args.AddRange(["-map", "0:a"]);
        if (coverInputIndex >= 0)
        {
            args.AddRange(["-map", $"{coverInputIndex}:v"]);
        }
        if (metadataInputIndex >= 0)
        {
            args.AddRange(["-map_metadata", metadataInputIndex.ToString()]);
        }

        args.AddRange(["-c:a", "copy"]);
        if (coverInputIndex >= 0)
        {
            args.AddRange(["-c:v", "mjpeg", "-disposition:v:0", "attached_pic"]);
        }

        if (embedTags)
        {
            args.AddRange(["-metadata", $"title={tags.Title}"]);
            args.AddRange(["-metadata", $"artist={tags.Author}"]);
            args.AddRange(["-metadata", $"album_artist={tags.Author}"]);
            args.AddRange(["-metadata", $"album={tags.Series ?? tags.Title}"]);
            args.AddRange(["-metadata", "genre=Audiobook"]);
        }

        args.Add(outputPath);

        var result = await processRunner.RunAsync(ffmpegPath, args, ct);
        if (!result.Success || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"ffmpeg failed to write the final output: {result.StandardError}");
        }
    }
}
