using AudiobookRequest.Core.Audio;

namespace AudiobookRequest.Core.Tests;

/// <summary>
/// Records every ffmpeg/ffprobe invocation and returns canned results. Writes an empty file at what
/// each call's last argument implies is the output path, mimicking what a real ffmpeg run would leave
/// on disk, so callers that check File.Exists(outputPath) after a "successful" run behave correctly.
/// </summary>
public class FakeProcessRunner : IProcessRunner
{
    public List<(string FileName, List<string> Arguments)> Calls { get; } = [];

    /// <summary>Maps an executable name to a queue of canned results, consumed in call order.</summary>
    public Dictionary<string, Queue<ProcessResult>> Results { get; } = [];

    public ProcessResult DefaultResult { get; set; } = new(0, "", "");

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        Calls.Add((fileName, [.. arguments]));

        var result = Results.TryGetValue(fileName, out var queue) && queue.Count > 0
            ? queue.Dequeue()
            : DefaultResult;

        if (result.Success && arguments.Count > 0)
        {
            var lastArg = arguments[^1];
            if (lastArg.EndsWith(".m4b", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllBytes(lastArg, []);
            }
        }

        return Task.FromResult(result);
    }
}
