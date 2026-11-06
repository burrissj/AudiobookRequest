using System.Text;
using System.Text.RegularExpressions;

namespace AudiobookRequest.Api.Services;

public record LogEntry(string Level, string Text);

public record LogView(string File, IReadOnlyList<string> Files, IReadOnlyList<LogEntry> Entries, bool Truncated);

/// <summary>
/// Reads the tail of the Serilog rolling log files so problems can be diagnosed from the admin page
/// instead of hunting for files on the server. Read-only; only files matching Serilog's own naming are served.
/// </summary>
public partial class LogViewerService(AppPaths paths)
{
    private const long MaxBytesRead = 2 * 1024 * 1024;

    // Serilog's default file format: "2026-10-02 12:00:00.123 +00:00 [INF] message"; exception stack
    // lines that follow don't start with a timestamp and belong to the previous entry.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}")]
    private static partial Regex EntryStartRegex();

    [GeneratedRegex(@"\[(VRB|DBG|INF|WRN|ERR|FTL)\]")]
    private static partial Regex LevelRegex();

    [GeneratedRegex(@"^log-\d+\.txt$")]
    private static partial Regex FileNameRegex();

    public LogView Read(string? file, string level, string? search, int maxEntries)
    {
        var files = ListFiles();
        var chosen = file is not null && files.Contains(file) ? file : files.FirstOrDefault();
        if (chosen is null)
        {
            return new LogView("", files, [], false);
        }

        var (text, truncated) = ReadTail(Path.Combine(paths.LogDirectory, chosen));
        var entries = Parse(text);
        if (truncated && entries.Count > 0)
        {
            entries.RemoveAt(0); // the first entry was cut mid-way by the tail window
        }

        IEnumerable<LogEntry> filtered = entries;
        filtered = level switch
        {
            "error" => filtered.Where(e => e.Level is "ERR" or "FTL"),
            "warning" => filtered.Where(e => e.Level is "WRN" or "ERR" or "FTL"),
            _ => filtered,
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(e => e.Text.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var result = filtered.TakeLast(Math.Clamp(maxEntries, 10, 1000)).Reverse().ToList();
        return new LogView(chosen, files, result, truncated);
    }

    private List<string> ListFiles()
    {
        if (!Directory.Exists(paths.LogDirectory))
        {
            return [];
        }
        return Directory.EnumerateFiles(paths.LogDirectory, "log-*.txt")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => FileNameRegex().IsMatch(n))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static (string Text, bool Truncated) ReadTail(string path)
    {
        // Serilog keeps the current file open for writing; share read access so we don't fight it.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var truncated = stream.Length > MaxBytesRead;
        if (truncated)
        {
            stream.Seek(-MaxBytesRead, SeekOrigin.End);
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return (reader.ReadToEnd(), truncated);
    }

    private static List<LogEntry> Parse(string text)
    {
        var entries = new List<LogEntry>();
        StringBuilder? current = null;
        var level = "INF";

        void Flush()
        {
            if (current is not null)
            {
                entries.Add(new LogEntry(level, current.ToString().TrimEnd()));
            }
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (EntryStartRegex().IsMatch(trimmed))
            {
                Flush();
                current = new StringBuilder(trimmed);
                var match = LevelRegex().Match(trimmed);
                level = match.Success ? match.Groups[1].Value : "INF";
            }
            else if (current is not null)
            {
                current.Append('\n').Append(trimmed);
            }
        }
        Flush();
        return entries;
    }
}
