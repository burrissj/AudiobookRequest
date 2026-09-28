namespace AudiobookRequest.Core.Audio;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Thin wrapper around external process execution (ffmpeg/ffprobe), abstracted for testability.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct = default);
}
