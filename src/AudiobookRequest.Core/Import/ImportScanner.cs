using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AudiobookRequest.Core.Import;

public enum CandidateKind { Folder, File }

/// <summary>One book found in the scan folder: a folder of parts, or a single file. Files are in listening order.</summary>
public record ImportCandidate(string Id, CandidateKind Kind, string RelativePath, string DisplayName, IReadOnlyList<string> Files, long TotalBytes)
{
    /// <summary>The folder or file the book lives in, for remembering that it has been imported.</summary>
    public string SourcePath(string root) => Path.GetFullPath(Path.Combine(root, RelativePath));
}

/// <summary>
/// Finds the books in a folder of loose audiobooks. Every messy shape people end up with is reduced to "one
/// candidate per book": a folder of numbered MP3 parts is one book, a lone .m4b is one book, a folder holding
/// several .m4b files is several books, and "CD1/CD2" sub-folders belong to the one book above them.
/// </summary>
public static partial class ImportScanner
{
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4b", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".wav",
    };

    // Whole-book containers: a folder with two or more of these holds separate books, not parts of one.
    private static readonly HashSet<string> WholeBookExtensions = new(StringComparer.OrdinalIgnoreCase) { ".m4b", ".m4a" };

    /// <summary>The folder finished imports are moved into when the person asks for that. Never scanned.</summary>
    public const string ImportedFolderName = "_imported";

    [GeneratedRegex(@"^(cd|disc|disk|part)[\s._-]*\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscFolder();

    [GeneratedRegex(@"\d+|\D+")]
    private static partial Regex Chunks();

    public static IReadOnlyList<ImportCandidate> Scan(string root)
    {
        var rootFull = Path.GetFullPath(root);
        if (!Directory.Exists(rootFull))
        {
            throw new DirectoryNotFoundException($"The folder '{root}' doesn't exist.");
        }

        var found = new List<ImportCandidate>();
        Walk(rootFull, rootFull, found);
        return found.OrderBy(c => c.RelativePath, NaturalComparer.Instance).ToList();
    }

    private static void Walk(string root, string dir, List<ImportCandidate> found)
    {
        var audio = AudioFilesIn(dir);
        var subdirs = SafeDirectories(dir).Where(d => !IsIgnored(Path.GetFileName(d))).ToList();

        if (audio.Count > 0)
        {
            if (dir == root || audio.Count(f => WholeBookExtensions.Contains(Path.GetExtension(f))) >= 2)
            {
                foreach (var file in audio)
                {
                    found.Add(Make(root, CandidateKind.File, file, [file]));
                }
            }
            else
            {
                found.Add(Make(root, CandidateKind.Folder, dir, audio));
            }
        }
        else if (dir != root && IsDiscSet(subdirs))
        {
            var files = subdirs.OrderBy(Path.GetFileName, NaturalComparer.Instance).SelectMany(AudioFilesIn).ToList();
            found.Add(Make(root, CandidateKind.Folder, dir, files));
            return;
        }

        foreach (var sub in subdirs)
        {
            Walk(root, sub, found);
        }
    }

    // "CD1", "Disc 2", "Part 3" folders, each holding audio directly: together they are one book.
    private static bool IsDiscSet(List<string> subdirs) =>
        subdirs.Count >= 2
        && subdirs.All(d => DiscFolder().IsMatch(Path.GetFileName(d)) && AudioFilesIn(d).Count > 0)
        && subdirs.All(d => !SafeDirectories(d).Any());

    private static bool IsIgnored(string name) =>
        name.Equals(ImportedFolderName, StringComparison.OrdinalIgnoreCase) || name.StartsWith('.') || name.StartsWith('$');

    private static List<string> AudioFilesIn(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f)) && !Path.GetFileName(f).StartsWith('.'))
                .OrderBy(Path.GetFileName, NaturalComparer.Instance)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeDirectories(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static ImportCandidate Make(string root, CandidateKind kind, string path, IReadOnlyList<string> files)
    {
        var relative = Path.GetRelativePath(root, path);
        var name = kind == CandidateKind.File ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
        long bytes = 0;
        foreach (var f in files)
        {
            try { bytes += new FileInfo(f).Length; } catch (IOException) { /* a file that vanished mid-scan counts as nothing */ }
        }
        return new ImportCandidate(IdFor(relative), kind, relative, name, files, bytes);
    }

    /// <summary>A short stable id from the path inside the scan folder, so the same book keeps the same id between scans.</summary>
    public static string IdFor(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/').ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16].ToLowerInvariant();
    }

    /// <summary>Compares "Track 2" before "Track 10" by treating runs of digits as numbers.</summary>
    public sealed class NaturalComparer : IComparer<string?>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var a = Chunks().Matches(x);
            var b = Chunks().Matches(y);
            for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var ca = a[i].Value;
                var cb = b[i].Value;
                int result;
                if (char.IsDigit(ca[0]) && char.IsDigit(cb[0]))
                {
                    var na = ca.TrimStart('0');
                    var nb = cb.TrimStart('0');
                    result = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                    if (result == 0) result = ca.Length.CompareTo(cb.Length);
                }
                else
                {
                    result = string.Compare(ca, cb, StringComparison.OrdinalIgnoreCase);
                }
                if (result != 0) return result;
            }
            return a.Count.CompareTo(b.Count);
        }
    }
}
