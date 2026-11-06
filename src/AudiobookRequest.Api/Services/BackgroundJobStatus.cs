using System.Collections.Concurrent;

namespace AudiobookRequest.Api.Services;

public record JobRun(string Name, DateTimeOffset? LastRunAt, bool? LastSucceeded, string? LastError, DateTimeOffset? LastSuccessAt);

/// <summary>In-memory record of when each background loop last ran, for the admin health page. Resets on restart.</summary>
public class BackgroundJobStatus
{
    private readonly ConcurrentDictionary<string, JobRun> runs = new();

    public void Success(string name)
    {
        var now = DateTimeOffset.UtcNow;
        runs[name] = new JobRun(name, now, true, null, now);
    }

    public void Failure(string name, Exception ex)
    {
        var previous = runs.TryGetValue(name, out var p) ? p.LastSuccessAt : null;
        runs[name] = new JobRun(name, DateTimeOffset.UtcNow, false, ex.Message, previous);
    }

    public IReadOnlyList<JobRun> All() => runs.Values.OrderBy(r => r.Name).ToList();
}

/// <summary>Resolved locations the admin tools need: the ffmpeg binaries and the folder Serilog writes to.</summary>
public record AppPaths(string Ffmpeg, string Ffprobe, string LogDirectory, string BackupDirectory = "");
