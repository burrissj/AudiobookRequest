using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Import;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookRequest.Api.Tests;

public class FakeAudioProbe : IAudioProbe
{
    /// <summary>What to report for a file, by file name. Anything not listed is a one-hour mp3 with no tags.</summary>
    public Dictionary<string, AudioFileInfo> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>File names that can't be read as audio.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<AudioFileInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        var name = Path.GetFileName(path);
        if (Unreadable.Contains(name))
        {
            throw new InvalidOperationException($"'{name}' can't be read as audio: Invalid data found");
        }
        return Task.FromResult(Files.TryGetValue(name, out var info) ? info with { Path = path } : new AudioFileInfo(path, 3600, "mp3", null, null, null, null));
    }
}

/// <summary>Holds a conversion open until the test releases it, so "one at a time" and cancelling can be tested.</summary>
public class GatedAudioCombiner : IAudioCombiner
{
    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? FailWith { get; set; }

    public void Release() => gate.TrySetResult();

    public async Task CombineAsync(IReadOnlyList<string> sourceFiles, string outputPath, BookTags tags, string? coverImagePath, bool embedTags = true, CancellationToken ct = default, bool requireAac = false)
    {
        Started.TrySetResult();
        await gate.Task.WaitAsync(ct);
        if (FailWith is not null)
        {
            throw FailWith;
        }
        await File.WriteAllTextAsync(outputPath, "combined", ct);
    }
}

public sealed class ImportServiceTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeAudioProbe probe = new();
    private readonly FakeMetadataClient metadata = new();
    private readonly FakeCoverArtDownloader cover = new();
    private readonly FakeAudioCombiner combiner = new();
    private readonly FakeJellyfinClient jellyfin = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "abr-import-" + Guid.NewGuid().ToString("N"));

    private string ImportDir => Path.Combine(root, "to sort");
    private string LibraryDir => Path.Combine(root, "library");
    private string StagingDir => Path.Combine(root, "staging");

    public ImportServiceTests()
    {
        Directory.CreateDirectory(ImportDir);
        t.CurrentSettings.LibraryPath = LibraryDir;
        t.CurrentSettings.StagingPath = StagingDir;
        t.CurrentSettings.ImportFolderPath = ImportDir;
        t.CurrentSettings.MinFreeDiskGb = 0;
        t.CurrentSettings.NamingPattern = "{Author}/{Title}/{Title}";
        t.Db.SaveChanges();
    }

    public void Dispose()
    {
        t.Dispose();
        try { Directory.Delete(root, recursive: true); } catch { /* scratch */ }
    }

    private ImportService Service() => new(
        t.Db, t.Settings, probe, metadata, cover, combiner, jellyfin, new LibraryCatalogService(t.Db, t.Settings, jellyfin), NullLogger<ImportService>.Instance);

    private string AddFile(string relative, int bytes = 20)
    {
        var path = Path.Combine(ImportDir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static string IdOf(string relative) => ImportScanner.IdFor(relative);

    private static ImportRequest Req(string title = "Dune", string author = "Frank Herbert", string? series = "Dune Chronicles", string action = "keep", bool overwrite = false) =>
        new(title, author, series, "http://cover/dune.jpg", action, overwrite);

    private static AudiobookListing Listing(string asin, string title, string author, int minutes, string? series = null, string? number = null) =>
        new(asin, title, author, "http://cover/" + asin, minutes, 4.5, 100, series, number, ["Narrator"]);

    private static Task<ImportResultDto> Import(ImportService s, string id, ImportRequest req) => s.ImportAsync(id, req, _ => { }, default);

    // ---- choosing the folder ----

    [Fact]
    public async Task TheFolderCanBeChosen_WithQuotesAndSpacesTidied()
    {
        var other = Path.Combine(root, "elsewhere");
        Directory.CreateDirectory(other);

        var dto = await Service().SetFolderAsync($"  \"{other}\"  ", default);

        Assert.Equal(other, dto.Folder);
        Assert.Equal(other, t.CurrentSettings.ImportFolderPath);
    }

    [Fact]
    public async Task ABlankFolderClearsIt()
    {
        var dto = await Service().SetFolderAsync("   ", default);

        Assert.Null(dto.Folder);
    }

    [Fact]
    public async Task AFolderThatDoesNotExistIsRefusedPlainly()
    {
        var ex = await Assert.ThrowsAsync<ImportException>(() => Service().SetFolderAsync(Path.Combine(root, "nope"), default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("doesn't exist", ex.Message);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("library/some author")]
    [InlineData("staging")]
    [InlineData("")] // the folder holding both the library and the staging folder
    public async Task TheLibraryAndStagingAreNeverImportSources(string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);

        var ex = await Assert.ThrowsAsync<ImportException>(() => Service().SetFolderAsync(path, default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("separate folder", ex.Message);
    }

    [Fact]
    public async Task ASiblingWithTheSamePrefixIsNotMistakenForInsideTheLibrary()
    {
        var sibling = Path.Combine(root, "library-old");
        Directory.CreateDirectory(sibling);

        var dto = await Service().SetFolderAsync(sibling, default);

        Assert.Equal(sibling, dto.Folder);
    }

    [Fact]
    public async Task SetupMustBeFinishedFirst()
    {
        t.CurrentSettings.StagingPath = null;
        await t.Db.SaveChangesAsync();

        var dto = await Service().GetFolderAsync(default);

        Assert.NotNull(dto.Problem);
    }

    // ---- scanning ----

    [Fact]
    public async Task ScanListsTheBooksInTheFolder()
    {
        AddFile("Dune/part1.mp3", 100);
        AddFile("Dune/part2.mp3", 100);
        AddFile("loose.m4b", 50);

        var scan = await Service().ScanAsync(default);

        Assert.Equal(ImportDir, scan.Folder);
        Assert.Equal(2, scan.Candidates.Count);
        var dune = scan.Candidates.Single(c => c.Name == "Dune");
        Assert.Equal(2, dune.FileCount);
        Assert.Equal(200, dune.TotalBytes);
        Assert.False(dune.Imported);
    }

    [Fact]
    public async Task ScanNeedsAFolder()
    {
        t.CurrentSettings.ImportFolderPath = null;
        await t.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ImportException>(() => Service().ScanAsync(default));

        Assert.Equal(400, ex.StatusCode);
    }

    // ---- identifying ----

    [Fact]
    public async Task IdentifyFillsInTheGuessFromTagsAndSearchesWithIt()
    {
        AddFile("Dune/part1.mp3");
        probe.Files["part1.mp3"] = new AudioFileInfo("", 3600, "mp3", "Chapter 1", "Frank Herbert", null, "Dune");
        metadata.TitlePage = new AudiobookPage([Listing("B1", "Dune", "Frank Herbert", 60), Listing("B2", "Dune", "Frank Herbert", 90), Listing("B3", "Dune", "Frank Herbert", 120)], 3);

        var dto = await Service().IdentifyAsync(IdOf("Dune"), new ImportIdentifyRequest(null, null), default);

        Assert.Equal("Dune", dto.GuessTitle);
        Assert.Equal("Frank Herbert", dto.GuessAuthor);
        Assert.Equal([("Dune", (string?)"Frank Herbert")], metadata.SearchCalls);
        Assert.Empty(metadata.BrowseCalls);                        // the title search was enough
        Assert.Equal(3, dto.Matches.Count);
        Assert.Equal(3600, dto.TotalSeconds);
    }

    [Fact]
    public async Task IdentifyBestMatchIsTheOneWithTheRightTitleAuthorAndLength()
    {
        AddFile("Dune/part1.mp3");
        probe.Files["part1.mp3"] = new AudioFileInfo("", 11 * 3600 + 2 * 60, "mp3", null, null, null, null); // 11h02m
        metadata.TitlePage = new AudiobookPage(
        [
            Listing("B-abridged", "Dune (Abridged)", "Frank Herbert", 240),
            Listing("B-other", "Dune Messiah", "Frank Herbert", 662),
            Listing("B-right", "Dune", "Frank Herbert", 665),
            Listing("B-wrong-author", "Dune", "Someone Else", 665),
        ], 4);

        var dto = await Service().IdentifyAsync(IdOf("Dune"), new ImportIdentifyRequest("Dune", "Frank Herbert"), default);

        Assert.Equal("B-right", dto.Matches[0].Asin);
        Assert.Contains("Same length", dto.Matches[0].LengthNote);
        Assert.True(dto.Matches[0].Score > dto.Matches[1].Score);
        Assert.Equal("B-abridged", dto.Matches.Single(m => m.Asin == "B-abridged").Asin);
        Assert.Contains("Very different length", dto.Matches.Single(m => m.Asin == "B-abridged").LengthNote);
    }

    [Fact]
    public async Task IdentifyLooksAgainWithoutTheAuthorWhenThatFindsNothing()
    {
        AddFile("Dune/part1.mp3");
        metadata.TitleAuthorPages["Wrong Author"] = new AudiobookPage([], 0);
        metadata.TitlePage = new AudiobookPage([Listing("B1", "Dune", "Frank Herbert", 60), Listing("B2", "Dune", "Frank Herbert", 70), Listing("B3", "Dune", "Frank Herbert", 80)], 3);

        var dto = await Service().IdentifyAsync(IdOf("Dune"), new ImportIdentifyRequest("Dune", "Wrong Author"), default);

        Assert.Equal(3, dto.Matches.Count);
        Assert.Equal([("Dune", (string?)"Wrong Author"), ("Dune", null)], metadata.SearchCalls);
    }

    [Fact]
    public async Task TheAdminCanOverrideTheSearch()
    {
        AddFile("Dune/part1.mp3");

        var dto = await Service().IdentifyAsync(IdOf("Dune"), new ImportIdentifyRequest("Project Hail Mary", "Andy Weir"), default);

        Assert.Equal("Project Hail Mary", dto.Query);
        // first with the author; then again without it, because nothing came back
        Assert.Equal("Andy Weir", metadata.SearchCalls[0].Author);
        Assert.All(metadata.SearchCalls, c => Assert.Equal("Project Hail Mary", c.Title));
        Assert.All(metadata.BrowseCalls, c => Assert.Equal("Project Hail Mary", c.Keywords));
    }

    [Fact]
    public async Task AKeywordSearchFillsInWhenTheTitleSearchFindsLittle()
    {
        AddFile("Dune/part1.mp3");
        metadata.TitlePage = new AudiobookPage([Listing("B1", "Dune", "Frank Herbert", 60)], 1);
        metadata.Page = new AudiobookPage([Listing("B1", "Dune", "Frank Herbert", 60), Listing("B2", "Dune Messiah", "Frank Herbert", 70)], 2);

        var dto = await Service().IdentifyAsync(IdOf("Dune"), new ImportIdentifyRequest("Dune", "Frank Herbert"), default);

        Assert.Equal(["B1", "B2"], dto.Matches.Select(m => m.Asin).Order().ToArray()); // the shared one appears once
        Assert.Equal(0, metadata.BrowseCalls.Single().PageNumber);   // Audible pages start at 0
    }

    [Fact]
    public async Task IdentifyReportsAFileThatCannotBeRead()
    {
        AddFile("Dune/part1.mp3");
        AddFile("Dune/part2.mp3");
        probe.Unreadable.Add("part2.mp3");

        var dto = await Service().IdentifyAsync(IdOf("Dune"), new ImportIdentifyRequest(null, null), default);

        Assert.Single(dto.Problems);
        Assert.Contains("part2.mp3", dto.Problems[0]);
        Assert.Equal(2, dto.Files.Count);
        Assert.NotNull(dto.Files.Single(f => f.Name == "part2.mp3").Problem);
    }

    [Fact]
    public async Task IdentifyAnUnknownBookIsNotFound()
    {
        var ex = await Assert.ThrowsAsync<ImportException>(() => Service().IdentifyAsync("0000000000000000", new ImportIdentifyRequest(null, null), default));

        Assert.Equal(404, ex.StatusCode);
    }

    // ---- previewing ----

    [Fact]
    public async Task PreviewShowsWhereItWillGo_AndWarnsOfDuplicates()
    {
        jellyfin.Library.Add(new JellyfinPlayedItem { Id = "j1", Name = "Dune", ArtistItems = [new JellyfinNameId { Name = "Frank Herbert" }] });
        await new LibraryCatalogService(t.Db, t.Settings, jellyfin).RefreshAsync(default);

        var fresh = await Service().PreviewAsync("Project Hail Mary", "Andy Weir", null, default);
        var dup = await Service().PreviewAsync("Dune", "Frank Herbert", null, default);

        Assert.Equal(Path.Combine("Andy Weir", "Project Hail Mary", "Project Hail Mary") + ".m4b", fresh.RelativePath);
        Assert.Equal(Path.Combine(LibraryDir, fresh.RelativePath), fresh.FullPath);
        Assert.False(fresh.FileExists);
        Assert.False(fresh.AlreadyInJellyfin);
        Assert.True(dup.AlreadyInJellyfin);
    }

    [Fact]
    public async Task APathThatWouldEscapeTheLibraryIsRefused()
    {
        var ex = await Assert.ThrowsAsync<ImportException>(() => Service().PreviewAsync("../../evil", "x", null, default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("outside the library", ex.Message);
    }

    // ---- importing ----

    [Fact]
    public async Task ImportFilesTheBookWhereTheNamingPatternSays_AndLeavesTheOriginalsAlone()
    {
        var p1 = AddFile("Dune/part1.mp3");
        var p2 = AddFile("Dune/part2.mp3");

        var result = await Import(Service(), IdOf("Dune"), Req());

        var expected = Path.Combine(LibraryDir, "Frank Herbert", "Dune", "Dune.m4b");
        Assert.Equal(expected, result.FinalPath);
        Assert.True(File.Exists(expected));
        Assert.True(File.Exists(p1) && File.Exists(p2));           // originals untouched
        Assert.False(result.MovedOriginals);
        Assert.Equal(1, jellyfin.LibraryRefreshes);                // Jellyfin told to rescan
        Assert.Empty(Directory.GetDirectories(StagingDir));        // scratch folder cleaned up

        var call = Assert.Single(combiner.Details);
        Assert.Equal(new BookTags("Dune", "Frank Herbert", "Dune Chronicles"), call.Tags);
        Assert.True(call.RequireAac);                              // an mp3 source must come out as real AAC
        Assert.Equal([p1, p2], combiner.Calls.Single().Sources);   // listening order
        Assert.True(combiner.Calls.Single().EmbedTags);
        Assert.Equal(1, cover.Downloads);
    }

    [Fact]
    public async Task ImportRecordsItInBothPlaces_AndTheOriginalsCleanupNeverTouchesThem()
    {
        AddFile("Dune/part1.mp3");

        await Import(Service(), IdOf("Dune"), Req());

        var processed = await t.Db.ProcessedBooks.SingleAsync();
        Assert.Null(processed.BookRequestId);
        Assert.True(processed.OriginalDeleted);
        var source = await t.Db.ImportedSources.SingleAsync();
        Assert.Equal("Dune", source.Title);
        Assert.Equal(Path.Combine(ImportDir, "Dune").ToLowerInvariant(), source.SourceKey);

        var scan = await Service().ScanAsync(default);
        var dune = scan.Candidates.Single();
        Assert.True(dune.Imported);
        Assert.Equal("Dune", dune.ImportedAs);
    }

    [Fact]
    public async Task ImportCanMoveTheOriginalsAside_NeverDeletingThem()
    {
        AddFile("Dune/part1.mp3");
        AddFile("Dune/part2.mp3");
        AddFile("Other/keep.mp3");

        var result = await Import(Service(), IdOf("Dune"), Req(action: "move"));

        Assert.True(result.MovedOriginals);
        Assert.True(File.Exists(Path.Combine(ImportDir, "_imported", "Dune", "part1.mp3")));
        Assert.True(File.Exists(Path.Combine(ImportDir, "_imported", "Dune", "part2.mp3")));
        Assert.False(Directory.Exists(Path.Combine(ImportDir, "Dune")));        // the emptied folder is tidied away
        Assert.True(File.Exists(Path.Combine(ImportDir, "Other", "keep.mp3")));  // other books untouched

        // and the scan no longer offers the moved ones (or looks inside _imported)
        var scan = await Service().ScanAsync(default);
        Assert.Equal(["Other"], scan.Candidates.Select(c => c.Name));
    }

    [Fact]
    public async Task MovingAsideNeverOverwritesAnEarlierFileOfTheSameName()
    {
        var first = AddFile("Dune/part1.mp3", 1);
        var imported = Path.Combine(ImportDir, "_imported", "Dune");
        Directory.CreateDirectory(imported);
        File.WriteAllBytes(Path.Combine(imported, "part1.mp3"), new byte[99]);

        await Import(Service(), IdOf("Dune"), Req(action: "move"));

        Assert.False(File.Exists(first));
        Assert.Equal(99, new FileInfo(Path.Combine(imported, "part1.mp3")).Length);
        Assert.Equal(1, new FileInfo(Path.Combine(imported, "part1 (2).mp3")).Length);
    }

    [Fact]
    public async Task AFailedMoveAsideDoesNotUndoTheImport()
    {
        var file = AddFile("loose.m4b");
        // a directory squatting on the name the file would move to makes the move fail
        Directory.CreateDirectory(Path.Combine(ImportDir, "_imported", "loose.m4b"));

        var result = await Import(Service(), IdOf("loose.m4b"), Req(title: "Loose", series: null, action: "move"));

        Assert.True(File.Exists(result.FinalPath));
        Assert.True(File.Exists(file));
        Assert.False(result.MovedOriginals);
        Assert.Contains("couldn't be moved aside", result.Note);
    }

    [Fact]
    public async Task ImportRefusesToDoTheSameBookTwiceUnlessAsked()
    {
        AddFile("Dune/part1.mp3");
        await Import(Service(), IdOf("Dune"), Req());

        var ex = await Assert.ThrowsAsync<ImportException>(() => Import(Service(), IdOf("Dune"), Req(title: "Dune Again")));
        Assert.Equal(409, ex.StatusCode);

        await Import(Service(), IdOf("Dune"), Req(title: "Dune Again", overwrite: true));
        Assert.Single(t.Db.ImportedSources);                      // one record, updated
        Assert.Equal("Dune Again", t.Db.ImportedSources.Single().Title);
    }

    [Fact]
    public async Task ImportNeverOverwritesAnExistingLibraryFileUnlessAsked()
    {
        AddFile("Dune/part1.mp3");
        var existing = Path.Combine(LibraryDir, "Frank Herbert", "Dune", "Dune.m4b");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "precious");

        var ex = await Assert.ThrowsAsync<ImportException>(() => Import(Service(), IdOf("Dune"), Req()));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("precious", await File.ReadAllTextAsync(existing));
        Assert.Empty(combiner.Calls);

        await Import(Service(), IdOf("Dune"), Req(overwrite: true));
        Assert.Equal("combined", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task ImportNeedsATitleAndAnAuthorAndASensibleChoice()
    {
        AddFile("Dune/part1.mp3");

        foreach (var bad in new[] { Req(title: " "), Req(author: ""), Req(title: new string('x', 201)), Req(action: "delete") })
        {
            var ex = await Assert.ThrowsAsync<ImportException>(() => Import(Service(), IdOf("Dune"), bad));
            Assert.Equal(400, ex.StatusCode);
        }
        Assert.Empty(combiner.Calls);
    }

    [Fact]
    public async Task AnUnreadableFileStopsTheImportBeforeAnythingIsConverted()
    {
        AddFile("Dune/part1.mp3");
        AddFile("Dune/part2.mp3");
        probe.Unreadable.Add("part2.mp3");

        var ex = await Assert.ThrowsAsync<ImportException>(() => Import(Service(), IdOf("Dune"), Req()));

        Assert.Equal(422, ex.StatusCode);
        Assert.Empty(combiner.Calls);
        Assert.False(Directory.Exists(LibraryDir));
        Assert.Empty(t.Db.ProcessedBooks);
    }

    [Fact]
    public async Task ImportStopsWhenThereIsNotEnoughRoom()
    {
        AddFile("Dune/part1.mp3", 1000);
        t.CurrentSettings.MinFreeDiskGb = int.MaxValue / 4096; // an absurd reserve
        await t.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ImportException>(() => Import(Service(), IdOf("Dune"), Req()));

        Assert.Equal(507, ex.StatusCode);
        Assert.Empty(combiner.Calls);
    }

    [Fact]
    public async Task AnImportOfABookThatHasVanishedIsNotFound()
    {
        var ex = await Assert.ThrowsAsync<ImportException>(() => Import(Service(), "ffffffffffffffff", Req()));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ImportReportsEachStageSoThePageCanShowProgress()
    {
        AddFile("Dune/part1.mp3");
        AddFile("Dune/part2.mp3");
        var stages = new List<string>();

        await Service().ImportAsync(IdOf("Dune"), Req(), stages.Add, default);

        Assert.Equal("Checking the files", stages.First());
        Assert.Contains(stages, s => s.StartsWith("Joining 2 files"));
        Assert.Equal("Telling Jellyfin", stages.Last());
    }

    [Fact]
    public async Task AJellyfinThatCannotBeToldDoesNotUndoTheImport()
    {
        AddFile("Dune/part1.mp3");
        var failing = new ThrowingRefreshJellyfin();
        var service = new ImportService(t.Db, t.Settings, probe, metadata, cover, combiner, failing, new LibraryCatalogService(t.Db, t.Settings, failing), NullLogger<ImportService>.Instance);

        var result = await Import(service, IdOf("Dune"), Req());

        Assert.True(File.Exists(result.FinalPath));
        Assert.Contains("couldn't be told to rescan", result.Note);
    }

    private sealed class ThrowingRefreshJellyfin : IJellyfinClient
    {
        public Task<JellyfinAuthResult?> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken ct = default, JellyfinDevice? device = null) => Task.FromResult<JellyfinAuthResult?>(null);
        public Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<JellyfinPlayedItem>> GetPlayedItemsAsync(string baseUrl, string apiKey, string userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<JellyfinPlayedItem>>([]);
        public Task<IReadOnlyList<JellyfinPlayedItem>> GetLibraryAudiobooksAsync(string baseUrl, string apiKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<JellyfinPlayedItem>>([]);
        public Task<IReadOnlyList<JellyfinPlayedItem>> SearchAudiobooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<JellyfinPlayedItem>>([]);
        public Task RefreshItemAsync(string baseUrl, string apiKey, string itemId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshLibraryAsync(string baseUrl, string apiKey, CancellationToken ct = default) => throw new HttpRequestException("down");
    }

    // ---- the background queue ----

    private sealed class JobEnv : IDisposable
    {
        public SqliteConnection Connection { get; } = new("DataSource=:memory:");
        public GatedAudioCombiner Combiner { get; } = new();
        public ImportJobService Jobs { get; }
        public string Library { get; }
        public string Staging { get; }
        public string Import { get; }
        private readonly string root = Path.Combine(Path.GetTempPath(), "abr-job-" + Guid.NewGuid().ToString("N"));

        public JobEnv()
        {
            Library = Path.Combine(root, "library");
            Staging = Path.Combine(root, "staging");
            Import = Path.Combine(root, "in");
            foreach (var book in new[] { "Dune", "Emma", "Persuasion" })
            {
                Directory.CreateDirectory(Path.Combine(Import, book));
                File.WriteAllBytes(Path.Combine(Import, book, "part1.mp3"), new byte[10]);
            }

            Connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options;
            using (var seed = new AppDbContext(options))
            {
                seed.Database.EnsureCreated();
                seed.Settings.Add(new AppSettings { SetupCompleted = true, LibraryPath = Library, StagingPath = Staging, ImportFolderPath = Import, MinFreeDiskGb = 0 });
                seed.SaveChanges();
            }

            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddScoped(sp => new AppDbContext(sp.GetRequiredService<DbContextOptions<AppDbContext>>()));
            services.AddScoped<SettingsService>();
            services.AddSingleton<IAudioProbe>(new FakeAudioProbe());
            services.AddSingleton<IAudiobookMetadataClient>(new FakeMetadataClient());
            services.AddSingleton<ICoverArtDownloader>(new FakeCoverArtDownloader());
            services.AddSingleton<IAudioCombiner>(Combiner);
            services.AddSingleton<IJellyfinClient>(new FakeJellyfinClient());
            services.AddScoped<LibraryCatalogService>();
            services.AddLogging();
            services.AddScoped<ImportService>();
            Jobs = new ImportJobService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<ImportJobService>.Instance);
        }

        public void Dispose()
        {
            Connection.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { /* scratch */ }
        }
    }

    private static ImportRequest EmmaReq() => Req(title: "Emma", author: "Jane Austen", series: null);
    private static ImportRequest PersuasionReq() => Req(title: "Persuasion", author: "Jane Austen", series: null);

    private static bool Open(ImportJobState j) => j.Status is "Queued" or "Running";

    /// <summary>Waits until nothing is waiting or running, and returns every job.</summary>
    private static async Task<IReadOnlyList<ImportJobState>> AllSettled(ImportJobService jobs)
    {
        for (var i = 0; i < 300; i++)
        {
            var all = jobs.Jobs;
            if (all.Count > 0 && !all.Any(Open))
            {
                return all;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("The import queue never finished.");
    }

    private static ImportJobState Of(IEnumerable<ImportJobState> jobs, string name) => jobs.Single(j => j.Name == name);

    [Fact]
    public async Task AJobRunsInTheBackgroundAndSucceeds()
    {
        using var env = new JobEnv();

        var job = env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req());
        Assert.NotNull(job);
        await env.Combiner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Running", env.Jobs.Current!.Status);
        Assert.NotNull(env.Jobs.Current.StartedAt);
        env.Combiner.Release();

        var done = Of(await AllSettled(env.Jobs), "Dune");

        Assert.Equal("Succeeded", done.Status);
        Assert.Null(done.Error);
        Assert.True(File.Exists(done.Result!.FinalPath));
        Assert.NotNull(done.FinishedAt);
        Assert.Null(env.Jobs.Current);
    }

    [Fact]
    public async Task BooksQueuedWhileOneConvertsWaitTheirTurnAndAllGetImported()
    {
        using var env = new JobEnv();
        env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req());
        await env.Combiner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var emma = env.Jobs.Enqueue(IdOf("Emma"), "Emma", EmmaReq());
        var persuasion = env.Jobs.Enqueue(IdOf("Persuasion"), "Persuasion", PersuasionReq());

        Assert.Equal("Queued", emma!.Status);
        Assert.Equal("Queued", persuasion!.Status);
        Assert.Equal(["Dune", "Emma", "Persuasion"], env.Jobs.Jobs.Select(j => j.Name));
        Assert.Equal(["Running", "Queued", "Queued"], env.Jobs.Jobs.Select(j => j.Status));

        env.Combiner.Release();
        var all = await AllSettled(env.Jobs);

        Assert.All(all, j => Assert.Equal("Succeeded", j.Status));
        Assert.All(all, j => Assert.True(File.Exists(j.Result!.FinalPath)));
        // one at a time, in the order they were queued
        Assert.True(Of(all, "Dune").FinishedAt <= Of(all, "Emma").StartedAt);
        Assert.True(Of(all, "Emma").FinishedAt <= Of(all, "Persuasion").StartedAt);
    }

    [Fact]
    public async Task TheSameBookCannotBeQueuedTwiceUntilItIsFinished()
    {
        using var env = new JobEnv();
        env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req());
        await env.Combiner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req()));   // converting now
        env.Jobs.Enqueue(IdOf("Emma"), "Emma", EmmaReq());
        Assert.Null(env.Jobs.Enqueue(IdOf("Emma"), "Emma", EmmaReq()));   // waiting

        env.Combiner.Release();
        await AllSettled(env.Jobs);

        Assert.NotNull(env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req(overwrite: true)));   // finished, so it may go again
        await AllSettled(env.Jobs);
    }

    [Fact]
    public async Task ABookWaitingInLineCanBeTakenOffWithoutBeingConverted()
    {
        using var env = new JobEnv();
        env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req());
        await env.Combiner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var emma = env.Jobs.Enqueue(IdOf("Emma"), "Emma", EmmaReq())!;
        env.Jobs.Enqueue(IdOf("Persuasion"), "Persuasion", PersuasionReq());

        Assert.True(env.Jobs.Cancel(emma.JobId));
        env.Combiner.Release();
        var all = await AllSettled(env.Jobs);

        Assert.Equal("Cancelled", Of(all, "Emma").Status);
        Assert.Contains("Taken off the queue", Of(all, "Emma").Error);
        Assert.Equal("Succeeded", Of(all, "Dune").Status);
        Assert.Equal("Succeeded", Of(all, "Persuasion").Status);   // the rest carry on
        Assert.False(Directory.Exists(Path.Combine(env.Library, "Jane Austen", "Emma")));
        Assert.False(env.Jobs.Cancel(emma.JobId));   // already finished
    }

    [Fact]
    public async Task CancellingTheRunningBookStopsItAddsNothingAndTheNextOneCarriesOn()
    {
        using var env = new JobEnv();
        var dune = env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req())!;
        await env.Combiner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        env.Jobs.Enqueue(IdOf("Emma"), "Emma", EmmaReq());

        Assert.True(env.Jobs.Cancel(dune.JobId));
        // the next book now starts and waits at the gate, which is opened here
        for (var i = 0; i < 200 && env.Jobs.Current?.Name != "Emma"; i++)
        {
            await Task.Delay(25);
        }
        env.Combiner.Release();
        var all = await AllSettled(env.Jobs);

        Assert.Equal("Cancelled", Of(all, "Dune").Status);
        Assert.False(Directory.Exists(Path.Combine(env.Library, "Frank Herbert")));
        Assert.Equal("Succeeded", Of(all, "Emma").Status);
    }

    [Fact]
    public async Task AFailureIsReportedWithItsReasonAndDoesNotStopTheQueue()
    {
        using var env = new JobEnv();
        env.Combiner.FailWith = new InvalidOperationException("ffmpeg failed to concatenate source files");
        env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req());
        env.Jobs.Enqueue(IdOf("Emma"), "Emma", EmmaReq());
        await env.Combiner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        env.Combiner.Release();

        var all = await AllSettled(env.Jobs);

        Assert.Equal("Failed", Of(all, "Dune").Status);
        Assert.Contains("ffmpeg failed", Of(all, "Dune").Error);
        Assert.Equal("Failed", Of(all, "Emma").Status);   // the same fake failure; the point is that it was tried
    }

    [Fact]
    public async Task ABadRequestFailsThePlainWay()
    {
        using var env = new JobEnv();

        env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req(title: ""));
        var done = Of(await AllSettled(env.Jobs), "Dune");

        Assert.Equal("Failed", done.Status);
        Assert.Contains("title and an author", done.Error);
    }

    [Fact]
    public async Task ClearingForgetsFinishedJobsButNotOnesStillWaiting()
    {
        using var env = new JobEnv();
        env.Jobs.Enqueue(IdOf("Dune"), "Dune", Req(title: ""));   // fails at once
        await AllSettled(env.Jobs);
        env.Jobs.Enqueue(IdOf("Emma"), "Emma", EmmaReq());
        await env.Combiner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        env.Jobs.Enqueue(IdOf("Persuasion"), "Persuasion", PersuasionReq());

        Assert.Equal(1, env.Jobs.ClearFinished());

        Assert.Equal(["Emma", "Persuasion"], env.Jobs.Jobs.Select(j => j.Name));
        env.Combiner.Release();
        await AllSettled(env.Jobs);
    }

    [Fact]
    public void AnEmptyQueueHasNothingToReportOrCancel()
    {
        using var env = new JobEnv();

        Assert.Empty(env.Jobs.Jobs);
        Assert.Null(env.Jobs.Current);
        Assert.False(env.Jobs.Cancel("nope"));
        Assert.Equal(0, env.Jobs.ClearFinished());
    }
}
