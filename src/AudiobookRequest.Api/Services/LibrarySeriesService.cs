using System.Globalization;
using System.Text.RegularExpressions;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <summary>A book's series and its number in it (null when the series doesn't say, e.g. an unnumbered companion).</summary>
public record SeriesInfo(string Name, decimal? Number);

/// <summary>
/// What is known beyond Jellyfin's own data about the library's books, by Jellyfin item id: the series of the books that have one
/// (Audible's, or an admin's correction), every book Audible has been asked about, the authors an admin has corrected, and the
/// books an admin has said are in no series.
/// </summary>
public record BookMetadataIndex(
    IReadOnlyDictionary<string, SeriesInfo> Series,
    IReadOnlySet<string> Checked,
    IReadOnlyDictionary<string, string>? Authors = null,
    IReadOnlySet<string>? NoSeries = null)
{
    public static readonly BookMetadataIndex Empty = new(new Dictionary<string, SeriesInfo>(), new HashSet<string>());

    public string? AuthorOf(string id) => Authors is not null && Authors.TryGetValue(id, out var author) ? author : null;

    public bool IsNotInSeries(string id) => NoSeries is not null && NoSeries.Contains(id);
}

/// <summary>An admin's correction for one book. Fields left empty mean "leave it to Jellyfin / Audible / the title".</summary>
public record BookOverrideDto(string? Author, string? Series, decimal? SeriesNumber, bool NotInSeries);

/// <summary>What Audible said about a book, to show beside the correction: not asked yet, or the series it found (if any).</summary>
public record AudibleSeriesDto(bool Checked, string? Series, decimal? Number);

public record BookMetadataEditDto(BookOverrideDto? Override, AudibleSeriesDto Audible);

/// <summary>What one look-up on Audible found: the book was recognised (or not), and the series it is in.</summary>
public record SeriesLookup(bool Matched, string? Series, string? Asin, string? Sequence);

/// <summary>
/// Finds out which series each book of the Jellyfin library belongs to, from Audible's catalog (Jellyfin keeps no series for
/// audiobooks, and titles only sometimes say). Works through the LibraryBookCache rows in small batches in the background,
/// remembering the answer on the row; a book only counts as found when Audible's title is the same book and, when both sides
/// name an author, the author matches, so a similar title by someone else never puts a book in the wrong series.
/// </summary>
public partial class LibrarySeriesService(AppDbContext db, IAudiobookMetadataClient audible)
{
    // A book Audible has a series for is unlikely to change; one it didn't is asked again sooner, in case it was only listed later.
    private static readonly TimeSpan RecheckWithSeries = TimeSpan.FromDays(60);
    private static readonly TimeSpan RecheckWithoutSeries = TimeSpan.FromDays(3);
    private const int Parallel = 3;

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"\s*[-:–]\s*(un)?abridged\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AbridgedSuffix();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex FirstNumber();

    private const int MaxNameLength = 200;

    /// <summary>The series of every book Audible gave one for, every book that has been asked about, and the admins' corrections laid over them.</summary>
    public async Task<BookMetadataIndex> GetIndexAsync(CancellationToken ct)
    {
        var rows = await db.LibraryBooks
            .Select(b => new { b.JellyfinItemId, b.SeriesName, b.SeriesSequence, b.SeriesCheckedAt })
            .ToListAsync(ct);
        var series = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.SeriesName))
            .ToDictionary(r => r.JellyfinItemId, r => new SeriesInfo(r.SeriesName!.Trim(), ParseSequence(r.SeriesSequence)), StringComparer.OrdinalIgnoreCase);
        var asked = rows.Where(r => r.SeriesCheckedAt is not null).Select(r => r.JellyfinItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var authors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in await db.BookOverrides.ToListAsync(ct))
        {
            if (!string.IsNullOrWhiteSpace(o.Author))
            {
                authors[o.JellyfinItemId] = o.Author;
            }
            if (o.NotInSeries)
            {
                series.Remove(o.JellyfinItemId);
                none.Add(o.JellyfinItemId);
            }
            else if (!string.IsNullOrWhiteSpace(o.Series))
            {
                series[o.JellyfinItemId] = new SeriesInfo(o.Series, o.SeriesNumber);
            }
        }
        return new BookMetadataIndex(series, asked, authors, none);
    }

    /// <summary>The correction saved for a book (if any), and what Audible said about it, for the edit form.</summary>
    public async Task<BookMetadataEditDto> GetEditAsync(string itemId, CancellationToken ct)
    {
        var o = await db.BookOverrides.FirstOrDefaultAsync(b => b.JellyfinItemId == itemId, ct);
        var cached = await db.LibraryBooks.Where(b => b.JellyfinItemId == itemId)
            .Select(b => new { b.SeriesName, b.SeriesSequence, b.SeriesCheckedAt })
            .FirstOrDefaultAsync(ct);
        return new BookMetadataEditDto(
            o is null ? null : new BookOverrideDto(o.Author, o.Series, o.SeriesNumber, o.NotInSeries),
            new AudibleSeriesDto(cached?.SeriesCheckedAt is not null, cached?.SeriesName, ParseSequence(cached?.SeriesSequence)));
    }

    /// <summary>
    /// Saves an admin's correction for a book, or removes it when nothing is left to correct. Returns what was saved (null when removed).
    /// A series needs a name before it can have a number, and "not in a series" can't also name one.
    /// </summary>
    public async Task<BookOverrideDto?> SetOverrideAsync(string itemId, BookOverrideDto request, CancellationToken ct)
    {
        var author = Tidy(request.Author);
        var series = Tidy(request.Series);
        if (author?.Length > MaxNameLength || series?.Length > MaxNameLength)
        {
            throw new ListenException(400, $"Names can be at most {MaxNameLength} characters.");
        }
        if (request.NotInSeries && (series is not null || request.SeriesNumber is not null))
        {
            throw new ListenException(400, "A book can't be both in a series and not in one.");
        }
        if (series is null && request.SeriesNumber is not null)
        {
            throw new ListenException(400, "Give the series a name to number the book in it.");
        }
        if (request.SeriesNumber is < 0 or > 10_000)
        {
            throw new ListenException(400, "That book number isn't valid.");
        }

        var existing = await db.BookOverrides.FirstOrDefaultAsync(b => b.JellyfinItemId == itemId, ct);
        if (author is null && series is null && !request.NotInSeries)
        {
            if (existing is not null)
            {
                db.BookOverrides.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            return null;
        }

        if (existing is null)
        {
            existing = new BookMetadataOverride { JellyfinItemId = itemId };
            db.BookOverrides.Add(existing);
        }
        existing.Author = author;
        existing.Series = series;
        existing.SeriesNumber = request.SeriesNumber;
        existing.NotInSeries = request.NotInSeries;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new BookOverrideDto(author, series, request.SeriesNumber, request.NotInSeries);
    }

    // Typed names get their spaces tidied so "  Amber   Nicole " is stored as "Amber Nicole".
    private static string? Tidy(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Audible's "3" is book 3; "1-3" is an omnibus, listed at its first book; blank or no digits is no number.</summary>
    public static decimal? ParseSequence(string? sequence)
    {
        var m = sequence is null ? null : FirstNumber().Match(sequence);
        return m is { Success: true } && decimal.TryParse(m.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>
    /// Asks Audible about up to <paramref name="max"/> books that haven't been (or are due to be asked again).
    /// Returns how many were settled; 0 when there is nothing to do or Audible seems unreachable.
    /// </summary>
    public async Task<int> EnrichBatchAsync(int max, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Compared here rather than in SQL: SQLite can't compare DateTimeOffset columns server-side.
        var stamps = await db.LibraryBooks.Select(b => new { b.Id, b.SeriesName, b.SeriesCheckedAt }).ToListAsync(ct);
        var due = stamps
            .Where(s => s.SeriesCheckedAt is null
                        || now - s.SeriesCheckedAt > (string.IsNullOrWhiteSpace(s.SeriesName) ? RecheckWithoutSeries : RecheckWithSeries))
            .OrderBy(s => s.SeriesCheckedAt is null ? 0 : 1)
            .ThenBy(s => s.Id)
            .Take(max)
            .Select(s => s.Id)
            .ToList();
        if (due.Count == 0)
        {
            return 0;
        }

        var rows = await db.LibraryBooks.Where(b => due.Contains(b.Id)).ToListAsync(ct);
        var results = new SeriesLookup?[rows.Count];
        using var gate = new SemaphoreSlim(Parallel);
        await Task.WhenAll(rows.Select(async (row, i) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                results[i] = await LookupAsync(row.Title, row.Author, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        // Not one book got any answer at all: Audible (or the connection) is down, so nothing is recorded and it is tried again
        // later. (Only judged on a few books or more: one or two that Audible has never heard of are simply recorded as not found.)
        if (rows.Count >= 3 && results.All(r => r is null))
        {
            return 0;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var found = results[i];
            rows[i].SeriesCheckedAt = now;
            rows[i].SeriesName = found?.Series;
            rows[i].SeriesAsin = found?.Asin;
            rows[i].SeriesSequence = found?.Sequence;
        }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Searches Audible for the book: by the title as it is, then without "(Unabridged)" and other bracketed extras, then by
    /// title alone when no author is on file. Null when Audible gave no results at all (so it may just be unreachable);
    /// otherwise whether the book was recognised and which series it is in.
    /// </summary>
    public async Task<SeriesLookup?> LookupAsync(string title, string? author, CancellationToken ct)
    {
        var cleaned = Clean(title);
        var attempts = new List<(string Title, string? Author)> { (title, author) };
        if (cleaned.Length > 0 && cleaned != title)
        {
            attempts.Add((cleaned, author));
        }
        if (!string.IsNullOrWhiteSpace(author))
        {
            attempts.Add((cleaned.Length > 0 ? cleaned : title, null));
        }

        var anyResults = false;
        foreach (var (searchTitle, searchAuthor) in attempts.Distinct())
        {
            var page = await audible.SearchAsync(searchTitle, string.IsNullOrWhiteSpace(searchAuthor) ? null : searchAuthor, 5, ct);
            anyResults |= page.Items.Count > 0;
            var match = Pick(title, author, page.Items);
            if (match is not null)
            {
                return new SeriesLookup(true, match.SeriesTitle, match.SeriesAsin, match.SeriesSequence);
            }
        }
        return anyResults ? new SeriesLookup(false, null, null, null) : null;
    }

    /// <summary>The Audible listing that is this book: the same title and, when both know an author, the same author. One with a series is preferred.</summary>
    public static AudiobookListing? Pick(string title, string? author, IEnumerable<AudiobookListing> listings) =>
        listings
            .Where(l => TitleMatcher.IsSimilarTitle(l.Title, title) && AuthorFits(author, l))
            .OrderByDescending(l => !string.IsNullOrWhiteSpace(l.SeriesTitle))
            .FirstOrDefault();

    private static bool AuthorFits(string? author, AudiobookListing listing)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return true;
        }
        var names = listing.AllAuthors is { Count: > 0 } ? listing.AllAuthors : [listing.Author];
        return names.Any(n => TitleMatcher.AuthorsMatch(author, n));
    }

    private static string Clean(string title) => AbridgedSuffix().Replace(Bracketed().Replace(title, ""), "").Trim();
}
