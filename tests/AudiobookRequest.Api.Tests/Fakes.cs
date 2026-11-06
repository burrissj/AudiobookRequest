using System.Net;
using System.Text;
using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookRequest.Api.Tests;

/// <summary>A throwaway in-memory SQLite database with the real schema, plus one admin and one regular user.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public AppDbContext Db { get; }
    public AppUser Admin { get; }
    public AppUser Victoria { get; }

    public TestDb()
    {
        connection.Open();
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        Db.Database.EnsureCreated();

        Admin = new AppUser { JellyfinUserId = "j-admin", Username = "john", Role = UserRole.Admin };
        Victoria = new AppUser { JellyfinUserId = "j-vic", Username = "victoria", Role = UserRole.User };
        Db.Users.AddRange(Admin, Victoria);
        Db.Settings.Add(new AppSettings
        {
            SetupCompleted = true,
            ChaptarrUrl = "http://chaptarr",
            ChaptarrApiKey = "key",
            ChaptarrRootFolderPath = "/audiobooks",
            JellyfinUrl = "http://jellyfin",
            JellyfinApiKey = "key",
        });
        Db.SaveChanges();
    }

    public SettingsService Settings => new(Db);

    public AppSettings CurrentSettings => Db.Settings.Single();

    public BookRequest AddRequest(AppUser user, BookRequestStatus status, string title = "A Book", Action<BookRequest>? configure = null)
    {
        var request = new BookRequest
        {
            RequestedByUserId = user.Id,
            Title = title,
            Author = "An Author",
            ChaptarrForeignBookId = "fb:" + Guid.NewGuid().ToString("N"),
            Status = status,
        };
        configure?.Invoke(request);
        Db.BookRequests.Add(request);
        Db.SaveChanges();
        Db.Entry(request).Reference(r => r.RequestedByUser).Load();
        return request;
    }

    public void Dispose()
    {
        Db.Dispose();
        connection.Dispose();
    }
}

public class FakeChaptarrClient : IChaptarrClient
{
    public List<ChaptarrQueueItem> Queue { get; } = [];
    public List<ChaptarrHistoryItem> History { get; } = [];
    public List<ChaptarrBookFile> Files { get; } = [];
    public List<int> SearchedBookIds { get; } = [];
    public List<int> DeletedBookIds { get; } = [];
    public int NextBookId { get; set; } = 100;

    public Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default) => Task.FromResult(true);
    public Dictionary<string, List<ChaptarrBookLookupResult>> Lookups { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> LookupTerms { get; } = [];
    public Task<IReadOnlyList<ChaptarrBookLookupResult>> LookupBooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default)
    {
        LookupTerms.Add(term);
        return Task.FromResult<IReadOnlyList<ChaptarrBookLookupResult>>(Lookups.TryGetValue(term, out var found) ? found : []);
    }
    public Task<ChaptarrRootFolder?> FindRootFolderAsync(string baseUrl, string apiKey, string rootFolderPath, CancellationToken ct = default) =>
        Task.FromResult<ChaptarrRootFolder?>(new ChaptarrRootFolder(rootFolderPath, 1, 1));
    public int AddBookCalls { get; private set; }
    public Exception? AddBookError { get; set; }
    /// <summary>Foreign ids Chaptarr's exact lookup no longer finds.</summary>
    public HashSet<string> UnknownForeignIds { get; } = [];
    public List<string> AddedForeignIds { get; } = [];
    public Task<int> AddBookAsync(string baseUrl, string apiKey, string foreignBookId, ChaptarrRootFolder rootFolder, CancellationToken ct = default)
    {
        AddBookCalls++;
        if (UnknownForeignIds.Contains(foreignBookId))
        {
            return Task.FromException<int>(new ChaptarrBookNotFoundException(foreignBookId));
        }
        AddedForeignIds.Add(foreignBookId);
        return AddBookError is not null ? Task.FromException<int>(AddBookError) : Task.FromResult(NextBookId++);
    }
    public Task TriggerBookSearchAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        SearchedBookIds.Add(bookId);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<ChaptarrQueueItem>> GetQueueAsync(string baseUrl, string apiKey, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChaptarrQueueItem>>(Queue);
    public Task<IReadOnlyList<ChaptarrHistoryItem>> GetHistorySinceAsync(string baseUrl, string apiKey, DateTimeOffset since, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChaptarrHistoryItem>>(History.Where(h => h.Date >= since).ToList());
    public Task<IReadOnlyList<ChaptarrBookFile>> GetBookFilesAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChaptarrBookFile>>(Files.Where(f => f.BookId == bookId).ToList());
    public Task DeleteBookAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default)
    {
        DeletedBookIds.Add(bookId);
        return Task.CompletedTask;
    }

    public List<ChaptarrImportCandidate> ImportCandidates { get; } = [];
    public Dictionary<int, ChaptarrBookInfo> BookInfos { get; } = [];
    public List<(IReadOnlyList<System.Text.Json.Nodes.JsonObject> Files, string Mode)> ManualImports { get; } = [];
    public Queue<ChaptarrCommandStatus> CommandStatuses { get; } = new();
    public Exception? ManualImportError { get; set; }

    public Task<IReadOnlyList<ChaptarrImportCandidate>> GetManualImportCandidatesAsync(string baseUrl, string apiKey, string downloadId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChaptarrImportCandidate>>(ImportCandidates);
    public Task<ChaptarrBookInfo?> GetBookInfoAsync(string baseUrl, string apiKey, int bookId, CancellationToken ct = default) =>
        Task.FromResult(BookInfos.GetValueOrDefault(bookId));
    public Task<int> StartManualImportAsync(string baseUrl, string apiKey, IReadOnlyList<System.Text.Json.Nodes.JsonObject> files, string importMode, CancellationToken ct = default)
    {
        if (ManualImportError is not null)
        {
            throw ManualImportError;
        }
        ManualImports.Add((files, importMode));
        return Task.FromResult(900);
    }
    public Task<ChaptarrCommandStatus> GetCommandStatusAsync(string baseUrl, string apiKey, int commandId, CancellationToken ct = default) =>
        Task.FromResult(CommandStatuses.Count > 0 ? CommandStatuses.Dequeue() : new ChaptarrCommandStatus(commandId, "completed", "successful", null));
}

public class FakeMetadataClient : IAudiobookMetadataClient
{
    public AudiobookPage Page { get; set; } = new([], 0);
    public List<(string? CategoryId, string? Keywords, string Sort, int PageNumber, int Size)> BrowseCalls { get; } = [];
    public List<string?> BrowseAuthors { get; } = [];

    /// <summary>Per-author pages for follow checks; falls back to <see cref="Page"/> when an author isn't listed.</summary>
    public Dictionary<string, AudiobookPage> AuthorPages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the series search (used for series follows) returns.</summary>
    public List<AudiobookCatalogEntry> SeriesBooks { get; } = [];

    public Task<AudiobookPage> BrowseAsync(string? audibleCategoryId, string? keywords, string sort, int page, int pageSize, string? author = null, CancellationToken ct = default)
    {
        BrowseCalls.Add((audibleCategoryId, keywords, sort, page, pageSize));
        BrowseAuthors.Add(author);
        return Task.FromResult(author is not null && AuthorPages.TryGetValue(author, out var authorPage) ? authorPage : Page);
    }
    /// <summary>What the title search returns; unlike <see cref="Page"/>, it can differ from the keyword search.</summary>
    public AudiobookPage TitlePage { get; set; } = new([], 0);
    public Dictionary<string, AudiobookPage> TitleAuthorPages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Title, string? Author)> SearchCalls { get; } = [];
    public Task<AudiobookPage> SearchAsync(string title, string? author, int pageSize, CancellationToken ct = default)
    {
        SearchCalls.Add((title, author));
        return Task.FromResult(author is not null && TitleAuthorPages.TryGetValue(author, out var page) ? page : TitlePage);
    }
    public Task<AudiobookMetadata?> TryResolveDetailAsync(string title, string authorName, CancellationToken ct = default) =>
        Task.FromResult<AudiobookMetadata?>(null);
    public Task<IReadOnlyList<AudiobookCatalogEntry>> GetSeriesBooksAsync(string seriesTitle, string authorName, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AudiobookCatalogEntry>>(SeriesBooks);
}

public class FakeJellyfinClient : IJellyfinClient
{
    public int LibraryRefreshes { get; private set; }
    public List<JellyfinPlayedItem> Library { get; } = [];

    public Task<JellyfinAuthResult?> AuthenticateAsync(string baseUrl, string username, string password, CancellationToken ct = default, JellyfinDevice? device = null) =>
        Task.FromResult<JellyfinAuthResult?>(null);
    public Task<bool> TestConnectionAsync(string baseUrl, string apiKey, CancellationToken ct = default) => Task.FromResult(true);
    public Task<IReadOnlyList<JellyfinPlayedItem>> GetPlayedItemsAsync(string baseUrl, string apiKey, string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JellyfinPlayedItem>>([]);
    /// <summary>When set, reading the library fails with this (Jellyfin down).</summary>
    public Exception? LibraryError { get; set; }
    public Task<IReadOnlyList<JellyfinPlayedItem>> GetLibraryAudiobooksAsync(string baseUrl, string apiKey, CancellationToken ct = default) =>
        LibraryError is not null ? Task.FromException<IReadOnlyList<JellyfinPlayedItem>>(LibraryError) : Task.FromResult<IReadOnlyList<JellyfinPlayedItem>>(Library);
    public List<string> SearchTerms { get; } = [];
    public Exception? SearchError { get; set; }
    public Task<IReadOnlyList<JellyfinPlayedItem>> SearchAudiobooksAsync(string baseUrl, string apiKey, string term, CancellationToken ct = default)
    {
        SearchTerms.Add(term);
        if (SearchError is not null)
        {
            throw SearchError;
        }
        return Task.FromResult<IReadOnlyList<JellyfinPlayedItem>>(Library.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList());
    }
    public Task RefreshItemAsync(string baseUrl, string apiKey, string itemId, CancellationToken ct = default) => Task.CompletedTask;
    public Task RefreshLibraryAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        LibraryRefreshes++;
        return Task.CompletedTask;
    }
}

public class FakeJellyfinPlayerClient : IJellyfinPlayerClient
{
    public List<JellyfinPlayerItem> Items { get; } = [];
    public List<JellyfinItemQuery> Queries { get; } = [];
    public List<JellyfinPlaybackReport> Reports { get; } = [];
    public List<(string ItemId, bool Played)> PlayedChanges { get; } = [];
    public List<(string Path, string? Range)> Opened { get; } = [];
    public List<string> BaseUrls { get; } = [];
    public JellyfinSession? LastSession { get; private set; }
    public bool Unauthorized { get; set; }
    public HttpStatusCode OpenStatus { get; set; } = HttpStatusCode.OK;

    private void Check(string baseUrl, JellyfinSession session)
    {
        BaseUrls.Add(baseUrl);
        LastSession = session;
        if (Unauthorized)
        {
            throw new JellyfinUnauthorizedException("rejected");
        }
    }

    public Task<JellyfinItemsResponseRaw> GetItemsAsync(string baseUrl, JellyfinSession session, JellyfinItemQuery query, CancellationToken ct = default)
    {
        Check(baseUrl, session);
        Queries.Add(query);
        return Task.FromResult(new JellyfinItemsResponseRaw { Items = Items, TotalRecordCount = Items.Count });
    }

    public Task<JellyfinPlayerItem?> GetItemAsync(string baseUrl, JellyfinSession session, string itemId, CancellationToken ct = default)
    {
        Check(baseUrl, session);
        return Task.FromResult(Items.FirstOrDefault(i => i.Id == itemId));
    }

    public Task<HttpResponseMessage> OpenAsync(string baseUrl, JellyfinSession session, string pathAndQuery, string? rangeHeader, CancellationToken ct = default)
    {
        Check(baseUrl, session);
        Opened.Add((pathAndQuery, rangeHeader));
        return Task.FromResult(new HttpResponseMessage(OpenStatus) { Content = new ByteArrayContent([1, 2, 3]) });
    }

    public Task ReportPlaybackAsync(string baseUrl, JellyfinSession session, JellyfinPlaybackReport report, CancellationToken ct = default)
    {
        Check(baseUrl, session);
        Reports.Add(report);
        return Task.CompletedTask;
    }

    public Task SetPlayedAsync(string baseUrl, JellyfinSession session, string itemId, bool played, CancellationToken ct = default)
    {
        Check(baseUrl, session);
        PlayedChanges.Add((itemId, played));
        return Task.CompletedTask;
    }

    public int Logouts { get; private set; }
    public List<string> StoppedTranscodes { get; } = [];

    public Task LogoutAsync(string baseUrl, JellyfinSession session, CancellationToken ct = default)
    {
        Logouts++;
        return Task.CompletedTask;
    }

    public Task StopTranscodeAsync(string baseUrl, JellyfinSession session, string playSessionId, CancellationToken ct = default)
    {
        StoppedTranscodes.Add(playSessionId);
        return Task.CompletedTask;
    }
}

public class FakeNotificationSender : INotificationSender
{
    public List<BookRequestDto> Updates { get; } = [];
    public List<int> Removed { get; } = [];
    public Task RequestUpdatedAsync(BookRequestDto request, CancellationToken ct = default)
    {
        Updates.Add(request);
        return Task.CompletedTask;
    }
    public Task RequestRemovedAsync(int requestId, CancellationToken ct = default)
    {
        Removed.Add(requestId);
        return Task.CompletedTask;
    }
}

public class FakeExternalNotifier : IExternalNotifier
{
    public List<ExternalNotification> Sent { get; } = [];
    public Task NotifyAsync(AppSettings settings, ExternalNotification notification, CancellationToken ct = default)
    {
        Sent.Add(notification);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<ChannelResult>> TestAsync(AppSettings settings, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChannelResult>>([]);
    public Task<IReadOnlyList<ChannelResult>> TestUserAsync(AppSettings settings, AppUser user, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChannelResult>>([]);
}

/// <summary>Writes a tiny file at the requested output path so the pipeline's File.Move has something to move.</summary>
public class FakeAudioCombiner : IAudioCombiner
{
    public List<(IReadOnlyList<string> Sources, bool EmbedTags, string? Cover)> Calls { get; } = [];
    public List<(string Output, BookTags Tags, bool RequireAac)> Details { get; } = [];
    public Task CombineAsync(IReadOnlyList<string> sourceFiles, string outputPath, BookTags tags, string? coverImagePath, bool embedTags = true, CancellationToken ct = default, bool requireAac = false)
    {
        Calls.Add((sourceFiles, embedTags, coverImagePath));
        Details.Add((outputPath, tags, requireAac));
        File.WriteAllText(outputPath, "combined");
        return Task.CompletedTask;
    }
}

public class FakeCoverArtDownloader : ICoverArtDownloader
{
    public int Downloads { get; private set; }
    public Task<string?> DownloadAsync(string? coverUrl, string destinationDirectory, CancellationToken ct = default)
    {
        Downloads++;
        return Task.FromResult<string?>("cover.jpg");
    }
}

public class FakeHttpHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public List<(Uri? Uri, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.RequestUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
        return new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}

public static class Nulls
{
    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;
}
