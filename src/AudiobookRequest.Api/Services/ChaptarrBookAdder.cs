using System.Text.RegularExpressions;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Adds a request's book to Chaptarr. A stored foreign id can stop resolving (the catalog was reshuffled, or the request
/// predates the metadata source Chaptarr uses now); then the book is looked up again by title and author, and if Chaptarr
/// lists the same book under a new id the request is moved over to it.
/// </summary>
public static partial class ChaptarrBookAdder
{
    public static async Task<int> AddAsync(
        AppDbContext db, IChaptarrClient chaptarr, AppSettings settings, BookRequest request, ChaptarrRootFolder rootFolder, CancellationToken ct)
    {
        try
        {
            return await chaptarr.AddBookAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, request.ChaptarrForeignBookId, rootFolder, ct);
        }
        catch (ChaptarrBookNotFoundException missing)
        {
            var replacement = await FindSameBookAsync(db, chaptarr, settings, request, ct);
            if (replacement is null)
            {
                throw new ChaptarrBookNotFoundException(missing.ForeignBookId, request.Title);
            }

            var bookId = await chaptarr.AddBookAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, replacement, rootFolder, ct);
            request.ChaptarrForeignBookId = replacement;
            return bookId;
        }
    }

    private static async Task<string?> FindSameBookAsync(
        AppDbContext db, IChaptarrClient chaptarr, AppSettings settings, BookRequest request, CancellationToken ct)
    {
        var wantedTitle = Normalize(request.Title);
        var wantedAuthor = Normalize(request.Author);
        if (wantedTitle.Length == 0)
        {
            return null;
        }

        var found = await chaptarr.LookupBooksAsync(settings.ChaptarrUrl!, settings.ChaptarrApiKey!, $"{request.Title} {request.Author}", ct);
        foreach (var candidate in found.Where(c => Normalize(c.Title) == wantedTitle && Normalize(c.AuthorName) == wantedAuthor))
        {
            var taken = await db.BookRequests.AnyAsync(r => r.Id != request.Id && r.ChaptarrForeignBookId == candidate.ForeignBookId, ct);
            if (!taken)
            {
                return candidate.ForeignBookId;
            }
        }

        return null;
    }

    /// <summary>Lower-case letters and digits only, with any "(…)" / "[…]" suffix (editions, "Dramatized Adaptation") dropped.</summary>
    private static string Normalize(string text) =>
        NotAlphanumeric().Replace(Bracketed().Replace(text, " ").ToLowerInvariant(), "");

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotAlphanumeric();
}
