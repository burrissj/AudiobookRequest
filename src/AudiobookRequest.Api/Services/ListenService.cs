using System.Text.RegularExpressions;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Library;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Services;

/// <param name="MyRating">The signed-in person's own 1-5 star rating, if they gave one.</param>
/// <param name="Series">The series the title says the book belongs to, with its number; both null when the title doesn't say.</param>
public record ListenItemDto(
    string Id,
    string Title,
    string? Author,
    int DurationSeconds,
    int PositionSeconds,
    bool Played,
    bool HasCover,
    int? MyRating = null,
    string? Series = null,
    decimal? SeriesNumber = null);

public record ListenLibraryDto(IReadOnlyList<ListenItemDto> Items, int Total);

/// <summary>How the Library can be sorted into piles: one per author, or one per series.</summary>
public enum ListenGrouping { Author, Series }

/// <summary>One pile of the library. <paramref name="Key"/> is what to ask for the books with; "-" is the pile of books with no author / no series.</summary>
/// <param name="CoverIds">Up to three books of the pile that have a cover, for the stack picture.</param>
/// <param name="By">For a series, the author who wrote most of it.</param>
public record ListenGroupDto(string Key, string Name, int Count, int Finished, IReadOnlyList<string> CoverIds, string? By = null);

/// <param name="Pending">How many books haven't been looked up on Audible for their series yet (they are grouped by title meanwhile).</param>
public record ListenGroupsDto(IReadOnlyList<ListenGroupDto> Groups, int Pending);

/// <summary>A pile with its books already in reading order.</summary>
public record BookGroup(string Key, string Name, string? By, IReadOnlyList<JellyfinPlayerItem> Books);

public record ListenChapterDto(string Name, double StartSeconds);

/// <param name="StreamMode">"direct" streams the original file; "hls" has Jellyfin convert it to AAC on the fly (for formats browsers can't play); "none" can't be played here.</param>
/// <param name="CanDownload">Only unconverted books can be saved for offline listening.</param>
public record ListenDetailDto(
    string Id,
    string Title,
    string? Author,
    int DurationSeconds,
    int PositionSeconds,
    bool Played,
    bool HasCover,
    string? MediaSourceId,
    IReadOnlyList<ListenChapterDto> Chapters,
    bool CanPlay,
    string? CantPlayReason,
    string StreamMode = "direct",
    bool CanDownload = true,
    string? Overview = null,
    string? Series = null,
    decimal? SeriesNumber = null);

/// <param name="ListenedSeconds">Seconds of the book that played since the last report that got through.</param>
/// <param name="WallSeconds">Seconds of real time spent listening over the same stretch.</param>
/// <param name="LocalDate">The listener's own date (yyyy-MM-dd), for the Stats page.</param>
public record ProgressRequest(
    double PositionSeconds,
    bool IsPaused,
    string? Event,
    string? PlaySessionId,
    double? ListenedSeconds = null,
    double? WallSeconds = null,
    string? LocalDate = null,
    string? Title = null,
    string? Author = null);

/// <summary>The listening filter: where the person left off, newest arrivals, everything A to Z, or finished books.</summary>
public enum ListenFilter { Resume, Recent, All, Finished }

public enum PlaybackMode { Direct, Hls, None }

/// <param name="Reason">Why it can't be played at all (None), or a note on why it's converted (Hls).</param>
public record PlaybackPlan(PlaybackMode Mode, string? Reason);

/// <summary>Thrown for conditions the endpoint turns into plain error responses.</summary>
public class ListenException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// The in-app player's brain: lists the person's own audiobooks from Jellyfin, tells the browser how a book can be
/// played there, and relays where they are up to. All calls run as the signed-in person.
/// </summary>
public partial class ListenService(SettingsService settingsService, IJellyfinPlayerClient player, IMemoryCache cache)
{
    private const long TicksPerSecond = 10_000_000;

    // Browsers (via <audio>) play these as-is; anything else (E-AC3, AC3, DTS...) is converted to AAC by Jellyfin.
    private static readonly HashSet<string> PlayableCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "mp3", "opus", "vorbis", "flac", "alac",
    };

    private static readonly HashSet<string> PlayableContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4a", "m4b", "mov", "mp3", "aac", "ogg", "oga", "opus", "flac", "webm", "matroska", "mka",
    };

    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex ItemIdPattern();

    // The only files the HLS proxy will fetch from Jellyfin: numbered segments of a conversion it started.
    [GeneratedRegex(@"^hls1/[A-Za-z0-9_-]{1,40}/\d{1,6}\.(ts|aac|mp4|m4s)$")]
    private static partial Regex HlsSegmentPattern();

    public static bool IsValidItemId(string? id) => id is not null && ItemIdPattern().IsMatch(id);

    public static bool IsValidHlsSegment(string? path) => path is not null && HlsSegmentPattern().IsMatch(path);

    public static ListenItemDto ToItem(JellyfinPlayerItem i)
    {
        var series = SeriesParser.Parse(i.Name);
        return new ListenItemDto(
            i.Id,
            i.Name ?? "Untitled",
            AuthorOf(i),
            Seconds(i.RunTimeTicks),
            Seconds(i.UserData?.PlaybackPositionTicks),
            i.UserData?.Played == true,
            i.ImageTags?.ContainsKey("Primary") == true,
            Series: series?.Name,
            SeriesNumber: series?.Number);
    }

    public static ListenDetailDto ToDetail(JellyfinPlayerItem i)
    {
        var source = i.MediaSources?.FirstOrDefault();
        var plan = Plan(source);
        var chapters = (i.Chapters ?? [])
            .Select((c, n) => new ListenChapterDto(ChapterName(c.Name, n), c.StartPositionTicks / (double)TicksPerSecond))
            .ToList();

        return new ListenDetailDto(
            i.Id,
            i.Name ?? "Untitled",
            AuthorOf(i),
            Seconds(i.RunTimeTicks),
            Seconds(i.UserData?.PlaybackPositionTicks),
            i.UserData?.Played == true,
            i.ImageTags?.ContainsKey("Primary") == true,
            source?.Id,
            chapters,
            plan.Mode != PlaybackMode.None,
            plan.Mode == PlaybackMode.None ? plan.Reason : null,
            plan.Mode == PlaybackMode.Hls ? "hls" : "direct",
            plan.Mode == PlaybackMode.Direct,
            string.IsNullOrWhiteSpace(i.Overview) ? null : i.Overview.Trim(),
            SeriesParser.Parse(i.Name)?.Name,
            SeriesParser.Parse(i.Name)?.Number);
    }

    /// <summary>The book's page as the Library shows it: an admin's author correction, and the series and number from Audible or an admin (none, when an admin says so).</summary>
    public static ListenDetailDto WithMetadata(ListenDetailDto detail, BookMetadataIndex known)
    {
        if (known.AuthorOf(detail.Id) is { } author)
        {
            detail = detail with { Author = author };
        }
        if (known.IsNotInSeries(detail.Id))
        {
            return detail with { Series = null, SeriesNumber = null };
        }
        return known.Series.TryGetValue(detail.Id, out var s) ? detail with { Series = s.Name, SeriesNumber = s.Number } : detail;
    }

    // Many audiobooks name chapters just "1", "2"... which reads oddly in a list; empty names get a number too.
    private static string ChapterName(string? name, int index) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().All(char.IsDigit) ? $"Chapter {index + 1}" : name.Trim();

    /// <summary>Whether a browser can play this file as-is, and if not, a plain-English reason.</summary>
    public static (bool CanPlay, string? Reason) Evaluate(JellyfinMediaSource? source)
    {
        if (source is null)
        {
            return (false, "Jellyfin has no playable file for this book.");
        }

        var container = (source.Container ?? "").Split(',')[0].Trim();
        var audio = source.MediaStreams?.FirstOrDefault(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase));
        var codec = audio?.Codec;

        if (!string.IsNullOrEmpty(codec) && !PlayableCodecs.Contains(codec))
        {
            return (false, $"This book is encoded as {codec.ToUpperInvariant()}, which web browsers can't play.");
        }

        if (!string.IsNullOrEmpty(container) && !PlayableContainers.Contains(container))
        {
            return (false, $"This book's file type ({container}) can't be played in a browser.");
        }

        return (true, null);
    }

    /// <summary>How to play a book: its own file if the browser can, otherwise ask Jellyfin to convert it to AAC.</summary>
    public static PlaybackPlan Plan(JellyfinMediaSource? source)
    {
        if (source is null)
        {
            return new PlaybackPlan(PlaybackMode.None, "Jellyfin has no playable file for this book.");
        }

        var (direct, reason) = Evaluate(source);
        return direct
            ? new PlaybackPlan(PlaybackMode.Direct, null)
            : new PlaybackPlan(PlaybackMode.Hls, reason);
    }

    /// <summary>The Content-Type to give the browser: Jellyfin labels .m4b oddly, and <c>audio/mp4</c> is what browsers want.</summary>
    public static string? ContentTypeFor(string? container, string? upstream)
    {
        var c = (container ?? "").Split(',')[0].Trim().ToLowerInvariant();
        return c switch
        {
            "mp4" or "m4a" or "m4b" or "mov" => "audio/mp4",
            "mp3" => "audio/mpeg",
            "flac" => "audio/flac",
            "ogg" or "oga" or "opus" => "audio/ogg",
            _ => upstream,
        };
    }

    public static long ToTicks(double seconds) => (long)(Math.Max(0, double.IsFinite(seconds) ? seconds : 0) * TicksPerSecond);

    public async Task<ListenLibraryDto> GetLibraryAsync(JellyfinSession session, ListenFilter filter, string? search, int start, int limit, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        limit = Math.Clamp(limit, 1, 100);
        start = Math.Max(0, start);

        var query = filter switch
        {
            ListenFilter.Resume => new JellyfinItemQuery(ResumableOnly: true, SortBy: "DatePlayed", Descending: true, StartIndex: start, Limit: limit),
            ListenFilter.Recent => new JellyfinItemQuery(SortBy: "DateCreated", Descending: true, StartIndex: start, Limit: limit),
            ListenFilter.Finished => new JellyfinItemQuery(PlayedOnly: true, SortBy: "DatePlayed", Descending: true, StartIndex: start, Limit: limit),
            _ => new JellyfinItemQuery(SortBy: "SortName", StartIndex: start, Limit: limit),
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query with { SearchTerm = search.Trim() };
        }

        var result = await player.GetItemsAsync(baseUrl, session, query, ct);
        return new ListenLibraryDto(result.Items.Select(ToItem).ToList(), result.TotalRecordCount);
    }

    /// <summary>The given books, in the order given (Jellyfin returns them in its own order). Books that no longer exist are skipped.</summary>
    public async Task<IReadOnlyList<ListenItemDto>> GetItemsByIdsAsync(JellyfinSession session, IReadOnlyList<string> orderedIds, CancellationToken ct)
    {
        if (orderedIds.Count == 0)
        {
            return [];
        }

        var baseUrl = await RequireBaseUrlAsync(ct);
        var result = await player.GetItemsAsync(baseUrl, session, new JellyfinItemQuery(Ids: orderedIds, Limit: orderedIds.Count), ct);
        var byId = result.Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        return orderedIds.Where(byId.ContainsKey).Select(id => ToItem(byId[id])).ToList();
    }

    /// <summary>The library sorted into piles, A to Z, with the pile of books that fit nowhere last.</summary>
    public async Task<ListenGroupsDto> GetGroupsAsync(JellyfinSession session, ListenGrouping by, BookMetadataIndex known, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        var library = await GetWholeLibraryAsync(baseUrl, session, ct);
        var groups = GroupBooks(library, by, known)
            .Select(g => new ListenGroupDto(
                g.Key,
                g.Name,
                g.Books.Count,
                g.Books.Count(b => b.UserData?.Played == true),
                g.Books.Where(b => b.ImageTags?.ContainsKey("Primary") == true).Take(3).Select(b => b.Id).ToList(),
                g.By))
            .ToList();
        return new ListenGroupsDto(groups, library.Count(b => !known.Checked.Contains(b.Id)));
    }

    /// <summary>The books of one pile in reading order, or null when there is no such pile (any more).</summary>
    public async Task<IReadOnlyList<ListenItemDto>?> GetGroupBooksAsync(JellyfinSession session, ListenGrouping by, string key, BookMetadataIndex known, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        var library = await GetWholeLibraryAsync(baseUrl, session, ct);
        var group = GroupBooks(library, by, known).FirstOrDefault(g => g.Key == key);
        if (group is null)
        {
            return null;
        }

        // The library list is kept for a couple of minutes; where the person is up to in each book must be current.
        var books = new List<ListenItemDto>();
        foreach (var chunk in group.Books.Select(b => b.Id).Chunk(50))
        {
            books.AddRange(await GetItemsByIdsAsync(session, chunk, ct));
        }
        return books.Select(b => WithMetadata(b, known)).ToList();
    }

    /// <summary>The book as the Library should show it: an admin's author correction, and its series and number from Audible or an admin (or none, when an admin says so).</summary>
    public static ListenItemDto WithMetadata(ListenItemDto item, BookMetadataIndex known)
    {
        if (known.AuthorOf(item.Id) is { } author)
        {
            item = item with { Author = author };
        }
        if (known.IsNotInSeries(item.Id))
        {
            return item with { Series = null, SeriesNumber = null };
        }
        return known.Series.TryGetValue(item.Id, out var s) ? item with { Series = s.Name, SeriesNumber = s.Number } : item;
    }

    private sealed record Entry(JellyfinPlayerItem Book, string? SeriesKey, string? SeriesName, decimal? Number, string? Author);

    /// <summary>
    /// Sorts books into piles by their first listed author (spellings like "J.R. Ward" and "J. R. Ward", or "Amber Nicole"
    /// and "Amber V. Nicole", are one author), or by series: as Audible lists the book when that is known, otherwise as the
    /// title says. Piles come A to Z; books with no author / no series go in a last pile keyed "-".
    /// Within a pile: a series in order of book number, an author's books series by series, then the stand-alones.
    /// </summary>
    public static IReadOnlyList<BookGroup> GroupBooks(IReadOnlyList<JellyfinPlayerItem> library, ListenGrouping by, BookMetadataIndex? known = null)
    {
        var entries = library.Select(b =>
        {
            string? key = null, name = null;
            decimal? number = null;
            if (known is not null && known.Series.TryGetValue(b.Id, out var listed) && SeriesParser.Normalise(listed.Name) is { Length: > 0 } listedKey)
            {
                (key, name, number) = (listedKey, listed.Name, listed.Number);
            }
            else if (known?.IsNotInSeries(b.Id) != true && SeriesParser.Parse(b.Name) is { } parsed)
            {
                (key, name, number) = (parsed.SeriesKey, parsed.Name, parsed.Number);
            }
            var author = known?.AuthorOf(b.Id) ?? FirstAuthor(b);
            return new Entry(b, key, name, number, author);
        }).ToList();

        // A title says "(Mistborn, #3)" where Audible says "The Mistborn Saga": a series named only by a title joins the one
        // Audible knows when its name is the start of exactly one of them, so the two don't sit side by side as separate piles.
        if (known is not null)
        {
            var listedKeys = entries.Where(e => e.SeriesKey is not null && known.Series.ContainsKey(e.Book.Id)).Select(e => (e.SeriesKey!, e.SeriesName!)).Distinct().ToList();
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.SeriesKey is null || known.Series.ContainsKey(e.Book.Id) || listedKeys.Any(l => l.Item1 == e.SeriesKey))
                {
                    continue;
                }
                var longer = listedKeys.Where(l => l.Item1.StartsWith(e.SeriesKey + " ", StringComparison.Ordinal)).Take(2).ToList();
                if (longer.Count == 1)
                {
                    entries[i] = e with { SeriesKey = longer[0].Item1, SeriesName = longer[0].Item2 };
                }
            }
        }
        var groups = new List<BookGroup>();

        if (by == ListenGrouping.Author)
        {
            foreach (var pile in entries.GroupBy(e => e.Author is null ? "-" : TitleMatcher.AuthorGroupKey(e.Author)))
            {
                var ordered = pile
                    .OrderBy(e => e.SeriesKey is null)
                    .ThenBy(e => e.SeriesKey, StringComparer.Ordinal)
                    .ThenBy(e => e.Number is null)
                    .ThenBy(e => e.Number)
                    .ThenBy(e => e.Book.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => e.Book)
                    .ToList();
                groups.Add(new BookGroup(pile.Key, pile.Key == "-" ? "Unknown author" : MostCommon(pile.Select(e => e.Author)) ?? pile.Key, null, ordered));
            }
        }
        else
        {
            foreach (var pile in entries.Where(e => e.SeriesKey is not null).GroupBy(e => e.SeriesKey!))
            {
                var ordered = pile
                    .OrderBy(e => e.Number is null)
                    .ThenBy(e => e.Number)
                    .ThenBy(e => e.Book.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => e.Book)
                    .ToList();
                groups.Add(new BookGroup(pile.Key, MostCommon(pile.Select(e => e.SeriesName)) ?? pile.Key, MostCommon(pile.Select(e => e.Author)), ordered));
            }
        }

        var result = groups.OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        if (by == ListenGrouping.Series)
        {
            var loose = entries.Where(e => e.SeriesKey is null).Select(e => e.Book).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (loose.Count > 0)
            {
                result.Add(new BookGroup("-", "Not in a series", null, loose));
            }
        }
        else
        {
            // "-" sorts before letters, so the unknown-author pile has to be moved to the end by hand.
            var unknown = result.FirstOrDefault(g => g.Key == "-");
            if (unknown is not null)
            {
                result.Remove(unknown);
                result.Add(unknown);
            }
        }
        return result;
    }

    private static string? FirstAuthor(JellyfinPlayerItem i) =>
        i.ArtistItems?.Select(a => a.Name?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n) && TitleMatcher.NormalizeAuthor(n).Length > 0);

    // The spelling most books use, so one oddly written copy doesn't name the pile; ties go to the fuller spelling ("Amber V. Nicole" over "Amber Nicole").
    private static string? MostCommon(IEnumerable<string?> names) =>
        names.OfType<string>().Where(n => n.Length > 0).GroupBy(n => n, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key.Length).Select(g => g.Key).FirstOrDefault();

    public async Task<ListenDetailDto?> GetDetailAsync(JellyfinSession session, string itemId, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        var item = await player.GetItemAsync(baseUrl, session, itemId, ct);
        return item is null ? null : ToDetail(item);
    }

    /// <summary>The next book in the same series that the person hasn't finished, or null.</summary>
    public async Task<ListenItemDto?> GetNextInSeriesAsync(JellyfinSession session, string itemId, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        var current = await player.GetItemAsync(baseUrl, session, itemId, ct);
        if (current is null)
        {
            return null;
        }

        var library = await GetWholeLibraryAsync(baseUrl, session, ct);
        var next = FindNextInSeries(current, library);
        return next is null ? null : ToItem(next);
    }

    /// <summary>The nearest higher-numbered book of the same series by the same author that isn't already finished.</summary>
    public static JellyfinPlayerItem? FindNextInSeries(JellyfinPlayerItem current, IReadOnlyList<JellyfinPlayerItem> library)
    {
        var position = SeriesParser.Parse(current.Name);
        if (position is null)
        {
            return null;
        }

        var authors = (current.ArtistItems ?? []).Select(a => a.Name).OfType<string>().ToList();

        return library
            .Where(b => b.Id != current.Id && b.UserData?.Played != true)
            .Select(b => (Book: b, Position: SeriesParser.Parse(b.Name)))
            .Where(x => x.Position is not null
                        && x.Position.SeriesKey == position.SeriesKey
                        && x.Position.Number > position.Number
                        && SameAuthor(authors, x.Book))
            .OrderBy(x => x.Position!.Number)
            .ThenBy(x => x.Book.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Book)
            .FirstOrDefault();
    }

    private static bool SameAuthor(List<string> authors, JellyfinPlayerItem other)
    {
        var theirs = (other.ArtistItems ?? []).Select(a => a.Name).OfType<string>().ToList();
        // No author data on either side: the series name has to carry it.
        return authors.Count == 0 || theirs.Count == 0 || authors.Any(a => theirs.Any(t => TitleMatcher.AuthorsMatch(a, t)));
    }

    private async Task<IReadOnlyList<JellyfinPlayerItem>> GetWholeLibraryAsync(string baseUrl, JellyfinSession session, CancellationToken ct)
    {
        var key = $"listen:library:{session.UserId}";
        if (cache.TryGetValue(key, out IReadOnlyList<JellyfinPlayerItem>? hit) && hit is not null)
        {
            return hit;
        }

        var all = new List<JellyfinPlayerItem>();
        const int pageSize = 200;
        for (var start = 0; start < 5000; start += pageSize)
        {
            var page = await player.GetItemsAsync(baseUrl, session, new JellyfinItemQuery(SortBy: "SortName", StartIndex: start, Limit: pageSize), ct);
            all.AddRange(page.Items);
            if (page.Items.Count < pageSize)
            {
                break;
            }
        }

        cache.Set(key, (IReadOnlyList<JellyfinPlayerItem>)all, TimeSpan.FromMinutes(2));
        return all;
    }

    /// <summary>Opens the book's audio, passing the browser's Range header through so seeking works.</summary>
    public async Task<(HttpResponseMessage Response, string? Container)> OpenStreamAsync(JellyfinSession session, string itemId, string? rangeHeader, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);

        // A browser fetches a book in many Range requests (every seek, plus buffering), and a book's file doesn't
        // change meanwhile, so look it up once per person per few minutes rather than on every request.
        var cacheKey = $"listen:target:{session.UserId}:{itemId}";
        if (!cache.TryGetValue(cacheKey, out StreamTarget? target) || target is null)
        {
            var item = await player.GetItemAsync(baseUrl, session, itemId, ct) ?? throw new ListenException(404, "Book not found.");
            var source = item.MediaSources?.FirstOrDefault();
            var plan = Plan(source);
            if (plan.Mode == PlaybackMode.None)
            {
                throw new ListenException(415, plan.Reason ?? "This book can't be played here.");
            }
            if (plan.Mode == PlaybackMode.Hls)
            {
                throw new ListenException(415, "This book has to be converted to play here; use its converted stream.");
            }

            target = new StreamTarget(source!.Id ?? itemId, source.Container);
            cache.Set(cacheKey, target, TimeSpan.FromMinutes(5));
        }

        var path = $"/Audio/{Uri.EscapeDataString(itemId)}/stream?static=true&MediaSourceId={Uri.EscapeDataString(target.MediaSourceId)}";
        var response = await player.OpenAsync(baseUrl, session, path, rangeHeader, ct);
        return (response, target.Container);
    }

    private sealed record StreamTarget(string MediaSourceId, string? Container);

    /// <summary>
    /// Starts (or fetches) Jellyfin's AAC conversion of a book as an HLS playlist. Segments are 30 seconds so a
    /// 12-hour book has about 1,400 of them rather than 14,000; Jellyfin converts each on demand, so seeking is quick.
    /// </summary>
    public async Task<HttpResponseMessage> OpenHlsPlaylistAsync(JellyfinSession session, string itemId, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        var item = await player.GetItemAsync(baseUrl, session, itemId, ct) ?? throw new ListenException(404, "Book not found.");
        var source = item.MediaSources?.FirstOrDefault();
        if (Plan(source).Mode != PlaybackMode.Hls)
        {
            throw new ListenException(409, "This book plays as-is; it doesn't need converting.");
        }

        var playSessionId = Guid.NewGuid().ToString("N");
        cache.Set($"listen:hls:{session.UserId}:{itemId}", playSessionId, TimeSpan.FromHours(6));

        var q = new[]
        {
            ("UserId", session.UserId),
            ("DeviceId", session.DeviceId),
            ("MediaSourceId", source!.Id ?? itemId),
            ("PlaySessionId", playSessionId),
            ("TranscodingProtocol", "hls"),
            ("TranscodingContainer", "ts"),
            ("Container", "ts"),
            ("AudioCodec", "aac"),
            ("AudioBitRate", "128000"),
            ("MaxStreamingBitrate", "140000000"),
            ("SegmentLength", "30"),
            ("MinSegments", "1"),
            ("TranscodeReasons", "AudioCodecNotSupported"),
        };
        var query = string.Join('&', q.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));

        var response = await player.OpenAsync(baseUrl, session, $"/Audio/{Uri.EscapeDataString(itemId)}/main.m3u8?{query}", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new ListenException(502, $"Jellyfin couldn't convert this book for playback (it answered {status}). Open it in Jellyfin instead.");
        }
        return response;
    }

    /// <summary>One segment of a conversion the playlist above started. The path is checked; the query is Jellyfin's own, passed back as given.</summary>
    public async Task<HttpResponseMessage> OpenHlsSegmentAsync(JellyfinSession session, string itemId, string segmentPath, string queryString, CancellationToken ct)
    {
        if (!IsValidHlsSegment(segmentPath) || queryString.Length > 2000)
        {
            throw new ListenException(404, "Not found.");
        }

        var baseUrl = await RequireBaseUrlAsync(ct);
        var response = await player.OpenAsync(baseUrl, session, $"/Audio/{Uri.EscapeDataString(itemId)}/{segmentPath}{queryString}", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new ListenException(502, $"Jellyfin couldn't provide that part of the book ({status}).");
        }
        return response;
    }

    /// <summary>Tells Jellyfin to stop converting a book, if we started one. Best effort.</summary>
    public async Task StopHlsAsync(JellyfinSession session, string itemId, CancellationToken ct)
    {
        var key = $"listen:hls:{session.UserId}:{itemId}";
        if (!cache.TryGetValue(key, out string? playSessionId) || playSessionId is null)
        {
            return;
        }

        cache.Remove(key);
        var baseUrl = await RequireBaseUrlAsync(ct);
        await player.StopTranscodeAsync(baseUrl, session, playSessionId, ct);
    }

    public async Task<HttpResponseMessage> OpenImageAsync(JellyfinSession session, string itemId, int maxWidth, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        var width = Math.Clamp(maxWidth, 64, 1000);
        return await player.OpenAsync(baseUrl, session, $"/Items/{Uri.EscapeDataString(itemId)}/Images/Primary?maxWidth={width}&quality=90", null, ct);
    }

    public async Task ReportProgressAsync(JellyfinSession session, string itemId, string? mediaSourceId, ProgressRequest req, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        var kind = req.Event?.ToLowerInvariant() switch
        {
            "start" => "Playing",
            "stop" => "Stopped",
            _ => "Progress",
        };

        await player.ReportPlaybackAsync(baseUrl, session,
            new JellyfinPlaybackReport(kind, itemId, mediaSourceId, ToTicks(req.PositionSeconds), req.IsPaused, req.PlaySessionId), ct);
    }

    public async Task SetPlayedAsync(JellyfinSession session, string itemId, bool played, CancellationToken ct)
    {
        var baseUrl = await RequireBaseUrlAsync(ct);
        await player.SetPlayedAsync(baseUrl, session, itemId, played, ct);
    }

    /// <summary>How many books the person has finished (in total, and this calendar year), or nulls if Jellyfin can't say.</summary>
    public async Task<(int? ThisYear, int? Total)> CountFinishedAsync(JellyfinSession session, int year, CancellationToken ct)
    {
        try
        {
            var baseUrl = await RequireBaseUrlAsync(ct);
            var page = await player.GetItemsAsync(baseUrl, session, new JellyfinItemQuery(PlayedOnly: true, SortBy: "DatePlayed", Descending: true, Limit: 500), ct);
            var thisYear = page.Items.Count(i => i.UserData?.LastPlayedDate?.Year == year);
            return (thisYear, page.TotalRecordCount);
        }
        catch (Exception ex) when (ex is HttpRequestException or ListenException or TaskCanceledException)
        {
            return (null, null);
        }
    }

    // The server talks to Jellyfin on its own (internal) address; the public one is only for links people click.
    private async Task<string> RequireBaseUrlAsync(CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(s.JellyfinUrl))
        {
            throw new ListenException(503, "Jellyfin isn't set up yet.");
        }
        return s.JellyfinUrl.Trim().TrimEnd('/');
    }

    private static int Seconds(long? ticks) => ticks is > 0 ? (int)Math.Min(int.MaxValue, ticks.Value / TicksPerSecond) : 0;

    private static string? AuthorOf(JellyfinPlayerItem i) =>
        i.ArtistItems is { Count: > 0 } ? string.Join(", ", i.ArtistItems.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n))) : null;
}
