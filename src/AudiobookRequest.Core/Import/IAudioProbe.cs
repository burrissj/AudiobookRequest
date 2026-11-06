using System.Globalization;
using System.Text.Json;
using AudiobookRequest.Core.Audio;

namespace AudiobookRequest.Core.Import;

/// <summary>Reads the length, codec and embedded tags of an audio file.</summary>
public interface IAudioProbe
{
    /// <exception cref="InvalidOperationException">The file can't be read as audio.</exception>
    Task<AudioFileInfo> ProbeAsync(string path, CancellationToken ct = default);
}

public class FfprobeAudioProbe(IProcessRunner processRunner, string ffprobePath = "ffprobe") : IAudioProbe
{
    public async Task<AudioFileInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        var result = await processRunner.RunAsync(ffprobePath, [
            "-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-select_streams", "a:0", path
        ], ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"'{System.IO.Path.GetFileName(path)}' can't be read as audio: {result.StandardError.Trim()}");
        }
        return Parse(path, result.StandardOutput);
    }

    /// <summary>Pulls the fields we use out of ffprobe's JSON. Tag names vary in case between formats ("TITLE", "Title").</summary>
    public static AudioFileInfo Parse(string path, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        double duration = 0;
        string? codec = null;
        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = seconds;
            }
            ReadTags(format, tags);
        }
        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 0)
        {
            var stream = streams[0];
            codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() : null;
            ReadTags(stream, tags);
        }

        string? Tag(string name) => tags.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        return new AudioFileInfo(path, duration, codec, Tag("title"), Tag("artist"), Tag("album_artist") ?? Tag("albumartist"), Tag("album"));
    }

    private static void ReadTags(JsonElement element, Dictionary<string, string> into)
    {
        if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (var tag in tags.EnumerateObject())
        {
            // Format-level tags win over stream-level ones that came first.
            into.TryAdd(tag.Name, tag.Value.GetString() ?? "");
        }
    }
}
