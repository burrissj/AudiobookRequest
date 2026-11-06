using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record BookmarkDto(int Id, double PositionSeconds, string? Note, string? Label, DateTimeOffset CreatedAt);

public record PersonalBookDto(int? Rating, IReadOnlyList<string> Tags, IReadOnlyList<BookmarkDto> Bookmarks);

public record TagCountDto(string Tag, int Count);

public record SetRatingRequest(int Stars);

public record AddTagRequest(string Tag);

public record AddBookmarkRequest(double PositionSeconds, string? Note, string? Label, string? ClientId);

public record UpdateBookmarkRequest(string? Note);

/// <summary>
/// One person's own star ratings, tags and bookmarks. Everything is scoped to the signed-in person: another
/// person's rows can't be read, changed or deleted through here, and ids that aren't theirs look like "not found".
/// </summary>
public class PersonalBookService(AppDbContext db)
{
    public const int MaxTagLength = 30;
    public const int MaxTagsPerBook = 20;
    public const int MaxTagsPerPerson = 300;
    public const int MaxBookmarksPerBook = 200;
    public const int MaxNoteLength = 200;
    public const int MaxLabelLength = 80;

    /// <summary>Tidies what was typed: control characters out, any run of whitespace becomes one space, a leading "#" dropped. Null if nothing usable is left.</summary>
    public static (string Display, string Key)? NormalizeTag(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var chars = raw.Select(c => char.IsWhiteSpace(c) ? ' ' : c).Where(c => !char.IsControl(c)).ToArray();
        var collapsed = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).TrimStart('#').Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }
        if (collapsed.Length > MaxTagLength)
        {
            collapsed = collapsed[..MaxTagLength].TrimEnd();
        }
        return (collapsed, collapsed.ToLowerInvariant());
    }

    public async Task<PersonalBookDto> GetAsync(int userId, string itemId, CancellationToken ct)
    {
        var rating = await db.BookRatings.AsNoTracking().Where(r => r.AppUserId == userId && r.JellyfinItemId == itemId).Select(r => (int?)r.Stars).FirstOrDefaultAsync(ct);
        var tags = await db.BookTags.AsNoTracking().Where(t => t.AppUserId == userId && t.JellyfinItemId == itemId).OrderBy(t => t.Id).Select(t => t.Tag).ToListAsync(ct);
        var bookmarks = await db.BookBookmarks.AsNoTracking().Where(b => b.AppUserId == userId && b.JellyfinItemId == itemId)
            .OrderBy(b => b.PositionSeconds).ThenBy(b => b.Id).ToListAsync(ct);
        return new PersonalBookDto(rating, tags, bookmarks.Select(ToDto).ToList());
    }

    /// <param name="stars">1-5 to rate, 0 to clear.</param>
    /// <returns>The rating now in effect, or null when cleared.</returns>
    public async Task<int?> SetRatingAsync(int userId, string itemId, int stars, CancellationToken ct)
    {
        if (stars is < 0 or > 5)
        {
            throw new ListenException(400, "A rating is 1 to 5 stars.");
        }

        var row = await db.BookRatings.FirstOrDefaultAsync(r => r.AppUserId == userId && r.JellyfinItemId == itemId, ct);
        if (stars == 0)
        {
            if (row is not null)
            {
                db.BookRatings.Remove(row);
                await db.SaveChangesAsync(ct);
            }
            return null;
        }

        if (row is null)
        {
            db.BookRatings.Add(new BookRating { AppUserId = userId, JellyfinItemId = itemId, Stars = stars });
        }
        else
        {
            row.Stars = stars;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return stars;
    }

    /// <returns>The tag as stored. Adding one the book already has (in any capitalisation) changes nothing.</returns>
    public async Task<string> AddTagAsync(int userId, string itemId, string? raw, CancellationToken ct)
    {
        var tag = NormalizeTag(raw) ?? throw new ListenException(400, "A tag can't be empty.");

        var existingOnBook = await db.BookTags.FirstOrDefaultAsync(t => t.AppUserId == userId && t.JellyfinItemId == itemId && t.TagKey == tag.Key, ct);
        if (existingOnBook is not null)
        {
            return existingOnBook.Tag;
        }

        if (await db.BookTags.CountAsync(t => t.AppUserId == userId && t.JellyfinItemId == itemId, ct) >= MaxTagsPerBook)
        {
            throw new ListenException(400, $"A book can have up to {MaxTagsPerBook} tags.");
        }

        // If the person already uses this tag on another book, keep that spelling so "Sci-Fi" never splits into "sci-fi".
        var display = await db.BookTags.Where(t => t.AppUserId == userId && t.TagKey == tag.Key).Select(t => t.Tag).FirstOrDefaultAsync(ct);
        if (display is null)
        {
            var distinct = await db.BookTags.Where(t => t.AppUserId == userId).Select(t => t.TagKey).Distinct().CountAsync(ct);
            if (distinct >= MaxTagsPerPerson)
            {
                throw new ListenException(400, $"You can have up to {MaxTagsPerPerson} different tags. Remove one you no longer use first.");
            }
            display = tag.Display;
        }

        db.BookTags.Add(new BookTag { AppUserId = userId, JellyfinItemId = itemId, Tag = display, TagKey = tag.Key });
        await db.SaveChangesAsync(ct);
        return display;
    }

    public async Task RemoveTagAsync(int userId, string itemId, string? raw, CancellationToken ct)
    {
        var tag = NormalizeTag(raw);
        if (tag is null)
        {
            return;
        }

        var row = await db.BookTags.FirstOrDefaultAsync(t => t.AppUserId == userId && t.JellyfinItemId == itemId && t.TagKey == tag.Value.Key, ct);
        if (row is not null)
        {
            db.BookTags.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Every tag the person uses, most-used first, for suggestions and the filter row.</summary>
    public async Task<IReadOnlyList<TagCountDto>> ListTagsAsync(int userId, CancellationToken ct)
    {
        var rows = await db.BookTags.AsNoTracking().Where(t => t.AppUserId == userId).Select(t => new { t.TagKey, t.Tag }).ToListAsync(ct);
        return rows
            .GroupBy(r => r.TagKey)
            .Select(g => new TagCountDto(g.First().Tag, g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The books carrying a tag, newest tagging first, one page at a time.</summary>
    public async Task<(IReadOnlyList<string> Ids, int Total)> ItemIdsWithTagAsync(int userId, string? raw, int start, int limit, CancellationToken ct)
    {
        var tag = NormalizeTag(raw);
        if (tag is null)
        {
            return ([], 0);
        }

        var query = db.BookTags.AsNoTracking().Where(t => t.AppUserId == userId && t.TagKey == tag.Value.Key);
        var total = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(t => t.Id)
            .Skip(Math.Max(0, start)).Take(Math.Clamp(limit, 1, 100)).Select(t => t.JellyfinItemId).ToListAsync(ct);
        return (ids, total);
    }

    /// <summary>The books the person has rated, best first, one page at a time.</summary>
    public async Task<(IReadOnlyList<string> Ids, int Total)> ItemIdsRatedAsync(int userId, int start, int limit, CancellationToken ct)
    {
        var query = db.BookRatings.AsNoTracking().Where(r => r.AppUserId == userId);
        var total = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(r => r.Stars).ThenByDescending(r => r.Id)
            .Skip(Math.Max(0, start)).Take(Math.Clamp(limit, 1, 100)).Select(r => r.JellyfinItemId).ToListAsync(ct);
        return (ids, total);
    }

    public async Task<Dictionary<string, int>> RatingsAsync(int userId, IEnumerable<string> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        return await db.BookRatings.AsNoTracking().Where(r => r.AppUserId == userId && ids.Contains(r.JellyfinItemId)).ToDictionaryAsync(r => r.JellyfinItemId, r => r.Stars, ct);
    }

    public async Task<BookmarkDto> AddBookmarkAsync(int userId, string itemId, AddBookmarkRequest req, CancellationToken ct)
    {
        if (!double.IsFinite(req.PositionSeconds) || req.PositionSeconds < 0 || req.PositionSeconds > 60 * 60 * 24 * 3)
        {
            throw new ListenException(400, "That isn't a place in the book.");
        }

        var clientId = string.IsNullOrWhiteSpace(req.ClientId) ? null : Trim(req.ClientId, 64);
        if (clientId is not null)
        {
            // The same bookmark sent again (the first attempt got through but its reply was lost): return it, don't duplicate.
            var repeat = await db.BookBookmarks.AsNoTracking().FirstOrDefaultAsync(b => b.AppUserId == userId && b.ClientId == clientId, ct);
            if (repeat is not null)
            {
                return ToDto(repeat);
            }
        }

        if (await db.BookBookmarks.CountAsync(b => b.AppUserId == userId && b.JellyfinItemId == itemId, ct) >= MaxBookmarksPerBook)
        {
            throw new ListenException(400, $"A book can have up to {MaxBookmarksPerBook} bookmarks.");
        }

        var row = new BookBookmark
        {
            AppUserId = userId,
            JellyfinItemId = itemId,
            PositionSeconds = Math.Round(req.PositionSeconds, 1),
            Note = Trim(req.Note, MaxNoteLength),
            Label = Trim(req.Label, MaxLabelLength),
            ClientId = clientId,
        };
        db.BookBookmarks.Add(row);
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<BookmarkDto> UpdateBookmarkAsync(int userId, int bookmarkId, string? note, CancellationToken ct)
    {
        var row = await db.BookBookmarks.FirstOrDefaultAsync(b => b.Id == bookmarkId && b.AppUserId == userId, ct)
                  ?? throw new ListenException(404, "Bookmark not found.");
        row.Note = Trim(note, MaxNoteLength);
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task DeleteBookmarkAsync(int userId, int bookmarkId, CancellationToken ct)
    {
        var row = await db.BookBookmarks.FirstOrDefaultAsync(b => b.Id == bookmarkId && b.AppUserId == userId, ct);
        if (row is not null)
        {
            db.BookBookmarks.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }

    private static BookmarkDto ToDto(BookBookmark b) => new(b.Id, b.PositionSeconds, b.Note, b.Label, b.CreatedAt);

    private static string? Trim(string? value, int max)
    {
        var t = value is null ? null : new string(value.Where(c => !char.IsControl(c) || c == ' ').ToArray()).Trim();
        if (string.IsNullOrEmpty(t))
        {
            return null;
        }
        return t.Length > max ? t[..max] : t;
    }
}
