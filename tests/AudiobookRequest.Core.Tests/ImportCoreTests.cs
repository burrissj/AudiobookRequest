using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Import;

namespace AudiobookRequest.Core.Tests;

/// <summary>A scratch folder tree that cleans itself up.</summary>
internal sealed class Tree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "abr-scan-" + Guid.NewGuid().ToString("N"));

    public Tree() => Directory.CreateDirectory(Root);

    public string Add(string relative, int bytes = 10)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* scratch */ }
    }
}

public class ImportScannerTests
{
    private static string[] Names(IEnumerable<ImportCandidate> c) => c.Select(x => x.RelativePath.Replace('\\', '/')).ToArray();

    [Fact]
    public void AFolderOfNumberedPartsIsOneBook_InListeningOrder()
    {
        using var t = new Tree();
        foreach (var n in new[] { "Track 10.mp3", "Track 2.mp3", "Track 1.mp3" })
        {
            t.Add($"Dune/{n}", 100);
        }

        var books = ImportScanner.Scan(t.Root);

        var book = Assert.Single(books);
        Assert.Equal(CandidateKind.Folder, book.Kind);
        Assert.Equal("Dune", book.DisplayName);
        Assert.Equal(["Track 1.mp3", "Track 2.mp3", "Track 10.mp3"], book.Files.Select(Path.GetFileName));
        Assert.Equal(300, book.TotalBytes);
    }

    [Fact]
    public void ALoneM4bIsOneBook_AndLooseFilesInTheRootAreEachTheirOwn()
    {
        using var t = new Tree();
        t.Add("Single Book/book.m4b");
        t.Add("loose one.mp3");
        t.Add("loose two.m4b");

        var books = ImportScanner.Scan(t.Root);

        Assert.Equal(3, books.Count);
        Assert.Equal(["loose one.mp3", "loose two.m4b", "Single Book/book.m4b".Split('/')[0]], Names(books).Select(n => n.Split('/')[0]).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Contains(books, b => b.Kind == CandidateKind.File && b.DisplayName == "loose one");
        Assert.Contains(books, b => b.Kind == CandidateKind.Folder && b.DisplayName == "Single Book");
    }

    [Fact]
    public void AFolderHoldingSeveralM4bsIsSeveralBooks()
    {
        using var t = new Tree();
        t.Add("Series/Book 1.m4b");
        t.Add("Series/Book 2.m4b");
        t.Add("Series/Book 3.m4b");

        var books = ImportScanner.Scan(t.Root);

        Assert.Equal(3, books.Count);
        Assert.All(books, b => Assert.Equal(CandidateKind.File, b.Kind));
        Assert.Equal(["Book 1", "Book 2", "Book 3"], books.Select(b => b.DisplayName));
    }

    [Fact]
    public void DiscFoldersBelongToTheOneBookAbove()
    {
        using var t = new Tree();
        t.Add("Big Book/CD1/01.mp3");
        t.Add("Big Book/CD1/02.mp3");
        t.Add("Big Book/CD2/01.mp3");
        t.Add("Big Book/CD10/01.mp3");

        var books = ImportScanner.Scan(t.Root);

        var book = Assert.Single(books);
        Assert.Equal("Big Book", book.DisplayName);
        Assert.Equal(4, book.Files.Count);
        // disc 1, disc 2, then disc 10 -- not disc 1, disc 10, disc 2
        Assert.Equal(["CD1", "CD1", "CD2", "CD10"], book.Files.Select(f => Path.GetFileName(Path.GetDirectoryName(f)!)));
    }

    [Fact]
    public void AuthorFoldersAreWalkedThroughToTheirBooks()
    {
        using var t = new Tree();
        t.Add("Frank Herbert/Dune/part1.mp3");
        t.Add("Frank Herbert/Dune Messiah/part1.mp3");
        t.Add("Andy Weir/Project Hail Mary/book.m4b");

        var books = ImportScanner.Scan(t.Root);

        Assert.Equal(3, books.Count);
        Assert.Contains(books, b => b.RelativePath.Replace('\\', '/') == "Frank Herbert/Dune");
    }

    [Fact]
    public void IgnoresNonAudioHiddenAndAlreadyImportedFolders()
    {
        using var t = new Tree();
        t.Add("Book/cover.jpg");
        t.Add("Book/notes.txt");
        t.Add("_imported/Done Book/part1.mp3");
        t.Add(".hidden/part1.mp3");
        t.Add("$RECYCLE.BIN/part1.mp3");
        t.Add("Real/part1.mp3");
        t.Add("Real/.DS_Store.mp3");

        var books = ImportScanner.Scan(t.Root);

        var book = Assert.Single(books);
        Assert.Equal("Real", book.DisplayName);
        Assert.Single(book.Files);
    }

    [Fact]
    public void IdsAreStableAndDistinct()
    {
        using var t = new Tree();
        t.Add("A/1.mp3");
        t.Add("B/1.mp3");

        var first = ImportScanner.Scan(t.Root);
        var second = ImportScanner.Scan(t.Root);

        Assert.Equal(first.Select(b => b.Id), second.Select(b => b.Id));
        Assert.Equal(2, first.Select(b => b.Id).Distinct().Count());
        Assert.Equal(16, first[0].Id.Length);
        Assert.Equal(ImportScanner.IdFor("A"), first[0].Id);
        Assert.Equal(ImportScanner.IdFor("a/"), ImportScanner.IdFor("A"));
    }

    [Fact]
    public void AMissingFolderIsReported()
    {
        Assert.Throws<DirectoryNotFoundException>(() => ImportScanner.Scan(Path.Combine(Path.GetTempPath(), "abr-not-there-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void AnEmptyFolderHasNoBooks()
    {
        using var t = new Tree();
        Assert.Empty(ImportScanner.Scan(t.Root));
    }

    [Theory]
    [InlineData("Track 2", "Track 10", -1)]
    [InlineData("Track 10", "Track 2", 1)]
    [InlineData("a01", "a1", 1)]
    [InlineData("Chapter 9.mp3", "chapter 9.mp3", 0)]
    [InlineData("1", "a", -1)]
    public void NaturalOrderComparesNumbersAsNumbers(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(ImportScanner.NaturalComparer.Instance.Compare(a, b)));
}

public class ImportGuessTests
{
    private static AudioFileInfo Info(string? title = null, string? artist = null, string? albumArtist = null, string? album = null) =>
        new("x.mp3", 100, "mp3", title, artist, albumArtist, album);

    [Fact]
    public void ALoneFileUsesItsTitleAndArtistTags()
    {
        var guess = ImportGuessBuilder.Build("Dune.m4b", false, [Info(title: "Dune", artist: "Frank Herbert")]);

        Assert.Equal(new ImportGuess("Dune", "Frank Herbert"), guess);
    }

    [Fact]
    public void PartsOfOneBookUseTheAlbumAsTheBookNotTheChapterTitle()
    {
        var files = new[] { Info("Chapter 1", "Frank Herbert", null, "Dune"), Info("Chapter 2", "Frank Herbert", null, "Dune") };

        var guess = ImportGuessBuilder.Build("whatever", true, files);

        Assert.Equal("Dune", guess.Title);
        Assert.Equal("Frank Herbert", guess.Author);
    }

    [Theory]
    [InlineData("Chapter 1")]
    [InlineData("Track 07")]
    [InlineData("Part 2")]
    [InlineData("12")]
    public void ALoneFileWhoseTitleIsJustAChapterNamesTheBookByItsAlbum(string chapterTitle)
    {
        var guess = ImportGuessBuilder.Build("Dune", true, [Info(title: chapterTitle, artist: "Frank Herbert", album: "Dune")]);

        Assert.Equal("Dune", guess.Title);
    }

    [Fact]
    public void ALoneFileWithARealTitleKeepsIt()
    {
        var guess = ImportGuessBuilder.Build("Dune.m4b", false, [Info(title: "Dune Messiah", album: "Dune Chronicles")]);

        Assert.Equal("Dune Messiah", guess.Title);
    }

    [Fact]
    public void AlbumArtistBeatsArtist_BecauseArtistOftenHoldsTheNarrator()
    {
        var guess = ImportGuessBuilder.Build("x", false, [Info(title: "Dune", artist: "Scott Brick", albumArtist: "Frank Herbert")]);

        Assert.Equal("Frank Herbert", guess.Author);
    }

    [Fact]
    public void WithoutTagsTheNamesDoTheTalking()
    {
        Assert.Equal(new ImportGuess("Project Hail Mary", "Andy Weir"), ImportGuessBuilder.Build("Andy Weir - Project Hail Mary", true, []));
        Assert.Equal(new ImportGuess("Dune", "Frank Herbert"), ImportGuessBuilder.Build("Frank Herbert/Dune", true, []));
        Assert.Equal(new ImportGuess("Some Title", null), ImportGuessBuilder.Build("Some Title", true, []));
    }

    [Fact]
    public void ATitleIsNotMistakenForAnAuthor()
    {
        // "The Dawn of the Cursed Queen - Book 3" splits at the dash but isn't "Author - Title".
        var guess = ImportGuessBuilder.Build("The Dawn of the Cursed Queen - Book 3", true, []);

        Assert.Null(guess.Author);
        Assert.Equal("The Dawn of the Cursed Queen - Book 3", guess.Title);
    }

    [Theory]
    [InlineData("Dune (Unabridged)", "Dune")]
    [InlineData("Dune [MP3]", "Dune")]
    [InlineData("01 - Dune", "Dune")]
    [InlineData("Dune_Messiah", "Dune Messiah")]
    [InlineData("  Dune   Messiah  ", "Dune Messiah")]
    [InlineData("Dune (Gods & Monsters, #1)", "Dune (Gods & Monsters, #1)")]
    public void TidyRemovesNoiseButKeepsTheTitle(string raw, string expected) => Assert.Equal(expected, ImportGuessBuilder.Tidy(raw));
}

public class FfprobeParsingTests
{
    [Fact]
    public void ReadsDurationCodecAndTags_IgnoringTagCase()
    {
        const string json = """
            {"streams":[{"codec_name":"aac","tags":{"language":"eng"}}],
             "format":{"duration":"39489.769977","tags":{"TITLE":"The Throne","ARTIST":"Amber V. Nicole","Album":"Gods","album_artist":"Amber V. Nicole"}}}
            """;

        var info = FfprobeAudioProbe.Parse("a.m4b", json);

        Assert.Equal("a.m4b", info.Path);
        Assert.Equal(39489.769977, info.DurationSeconds, 5);
        Assert.Equal("aac", info.Codec);
        Assert.Equal("The Throne", info.Title);
        Assert.Equal("Amber V. Nicole", info.Artist);
        Assert.Equal("Amber V. Nicole", info.AlbumArtist);
        Assert.Equal("Gods", info.Album);
    }

    [Fact]
    public void CopesWithMissingPieces()
    {
        var info = FfprobeAudioProbe.Parse("a.mp3", """{"format":{}}""");

        Assert.Equal(0, info.DurationSeconds);
        Assert.Null(info.Codec);
        Assert.Null(info.Title);
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsAnError()
    {
        var runner = new FakeProcessRunner();
        runner.Results["ffprobe"] = new Queue<ProcessResult>([new ProcessResult(1, "", "Invalid data found")]);
        var probe = new FfprobeAudioProbe(runner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.ProbeAsync("broken.mp3"));

        Assert.Contains("broken.mp3", ex.Message);
        Assert.Contains("Invalid data", ex.Message);
    }
}

public class RequireAacTests
{
    private static readonly BookTags Tags = new("Dune", "Frank Herbert", null);

    // The combiner asks ffprobe about each file's codec first and, for several files, then each one's length (for chapters).
    private static FakeProcessRunner Runner(params string[] codecs)
    {
        var runner = new FakeProcessRunner();
        var answers = codecs.Select(c => new ProcessResult(0, $"{c},44100,2", "")).ToList();
        if (codecs.Length > 1)
        {
            answers.AddRange(codecs.Select(_ => new ProcessResult(0, "60.0", "")));
        }
        runner.Results["ffprobe"] = new Queue<ProcessResult>(answers);
        return runner;
    }

    private static async Task<List<(string FileName, List<string> Arguments)>> Run(FakeProcessRunner runner, string[] sources, bool requireAac)
    {
        var output = Path.GetTempFileName() + ".m4b";
        try
        {
            await new FfmpegAudioCombiner(runner).CombineAsync(sources, output, Tags, null, ct: default, requireAac: requireAac);
            return runner.Calls;
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task AnMp3IsReencodedToAac()
    {
        var calls = await Run(Runner("mp3"), ["a.mp3"], requireAac: true);

        var first = calls.First(c => c.FileName == "ffmpeg");
        Assert.Contains("aac", first.Arguments);
        Assert.DoesNotContain("copy", first.Arguments);
    }

    [Fact]
    public async Task AnAacFileIsStillJustCopied()
    {
        var calls = await Run(Runner("aac"), ["a.m4b"], requireAac: true);

        var first = calls.First(c => c.FileName == "ffmpeg");
        Assert.Contains("copy", first.Arguments);
    }

    [Fact]
    public async Task SeveralMp3sAreReencodedEvenThoughTheyMatch()
    {
        var calls = await Run(Runner("mp3", "mp3"), ["a.mp3", "b.mp3"], requireAac: true);

        var concat = calls.First(c => c.FileName == "ffmpeg" && c.Arguments.Contains("concat"));
        Assert.Contains("aac", concat.Arguments);
    }

    [Fact]
    public async Task SeveralAacFilesAreStillStreamCopied()
    {
        var calls = await Run(Runner("aac", "aac"), ["a.m4a", "b.m4a"], requireAac: true);

        var concat = calls.First(c => c.FileName == "ffmpeg" && c.Arguments.Contains("concat"));
        Assert.Contains("copy", concat.Arguments);
    }

    [Fact]
    public async Task WithoutTheRequirementAnMp3IsStillCopied_AsBefore()
    {
        var calls = await Run(Runner("mp3"), ["a.mp3"], requireAac: false);

        Assert.Contains("copy", calls.First(c => c.FileName == "ffmpeg").Arguments);
        Assert.DoesNotContain(calls, c => c.FileName == "ffprobe");
    }
}
