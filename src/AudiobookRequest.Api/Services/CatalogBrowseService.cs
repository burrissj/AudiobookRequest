using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Core.Recommendations;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Services;

/// <param name="Asin">Audible's id for the book; the key the client uses to track a card before it has a Chaptarr id.</param>
public record BrowseItemDto(
    string Asin,
    string Title,
    string Author,
    string CoverUrl,
    int? RuntimeMinutes,
    double? Rating,
    string? SeriesTitle,
    bool AlreadyInLibrary,
    string? SimilarInLibraryTitle,
    bool AlreadyRequested,
    string? ReleaseDate = null);

public record CategoryInfoDto(string Id, string Title, string Description, string Layout, bool HasKeywordSort);

public record CategorySectionDto(CategoryInfoDto Category, IReadOnlyList<BrowseItemDto> Items, int TotalTitles);

public record CategoryPageDto(CategoryInfoDto Category, IReadOnlyList<BrowseItemDto> Items, int TotalTitles, int Page, bool HasMore);

/// <summary>
/// Browsing by category without touching Chaptarr: covers, length and ratings come straight from
/// Audible's catalog (cached), flagged against your library and existing requests. A book is only matched
/// to a Chaptarr entry when someone opens or requests it (<see cref="ResolveAsync"/>), which keeps a page of
/// two dozen covers instant instead of two dozen sequential lookups.
/// </summary>
public class CatalogBrowseService(
    AppDbContext db,
    SettingsService settingsService,
    IAudiobookMetadataClient metadataClient,
    IChaptarrClient chaptarr,
    LibraryCatalogService libraryCatalog,
    IMemoryCache cache)
{
    public const int PageSize = 24;
    private static readonly TimeSpan PageTtl = TimeSpan.FromMinutes(30);
    private static readonly HashSet<string> AllowedSorts = ["BestSellers", "-ReleaseDate", "Relevance"];

    public static CategoryInfoDto Describe(BrowseCategory c) =>
        new(c.Id, c.Title, c.Description, c.Layout.ToString().ToLowerInvariant(), c.Keywords is not null);

    public async Task<IReadOnlyList<CategorySectionDto>> HomeAsync(int seed, int count, CancellationToken ct)
    {
        var categories = CategoryCatalog.PickForHome(seed, count);
        var flags = await LoadFlagsAsync(ct);

        var pages = await Task.WhenAll(categories.Select(c => FetchAsync(c, c.Sort, 0, PreviewSize(c.Layout), ct)));
        return categories
            .Select((c, i) => new CategorySectionDto(Describe(c), Annotate(pages[i].Items, flags), pages[i].TotalResults))
            .Where(s => s.Items.Count > 0)
            .ToList();
    }

    public async Task<CategoryPageDto> PageAsync(BrowseCategory category, string? sort, int page, CancellationToken ct)
    {
        var effectiveSort = sort is not null && AllowedSorts.Contains(sort) ? sort : category.Sort;
        var flags = await LoadFlagsAsync(ct);
        var result = await FetchAsync(category, effectiveSort, Math.Max(0, page), PageSize, ct);
        var hasMore = result.Items.Count > 0 && (page + 1) * PageSize < result.TotalResults;
        return new CategoryPageDto(Describe(category), Annotate(result.Items, flags), result.TotalResults, page, hasMore);
    }

    /// <summary>
    /// Finds the Chaptarr entry for a catalog book: same author, then the closest title. Null when your
    /// Chaptarr doesn't know the book at all, which the UI reports instead of offering a Request that can't work.
    /// </summary>
    public async Task<BookSearchResultDto?> ResolveAsync(string title, string author, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ChaptarrUrl) || string.IsNullOrWhiteSpace(settings.ChaptarrApiKey))
        {
            return null;
        }

        var library = await libraryCatalog.GetIndexAsync(ct);
        foreach (var term in new[] { title, $"{title} {author}" })
        {
            var results = await chaptarr.LookupBooksAsync(settings.ChaptarrUrl, settings.ChaptarrApiKey, term, ct);
            var match = BestMatch(results, title, author);
            if (match is not null)
            {
                return BookEndpoints.ToDto(match, library, null);
            }
        }
        return null;
    }

    public static ChaptarrBookLookupResult? BestMatch(IEnumerable<ChaptarrBookLookupResult> results, string title, string author)
    {
        var sameAuthor = results
            .Where(r => TitleMatcher.AuthorsMatch(r.AuthorName, author)
                        || r.AuthorName.Contains(author, StringComparison.OrdinalIgnoreCase)
                        || author.Contains(r.AuthorName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var wanted = TitleMatcher.NormalizeTitle(title);

        return sameAuthor.FirstOrDefault(r => TitleMatcher.NormalizeTitle(r.Title) == wanted)
            ?? sameAuthor.FirstOrDefault(r => TitleMatcher.IsSimilarTitle(r.Title, title))
            ?? sameAuthor.FirstOrDefault(r =>
            {
                var candidate = TitleMatcher.NormalizeTitle(r.Title);
                return candidate.Length > 3 && wanted.Length > 3 && (candidate.Contains(wanted) || wanted.Contains(candidate));
            });
    }

    private static int PreviewSize(CategoryLayout layout) => layout switch
    {
        CategoryLayout.Tilted => 12,
        CategoryLayout.Shelf => 10,
        _ => 8, // the grid shows six; two spare for any that get filtered out client-side
    };

    private async Task<AudiobookPage> FetchAsync(BrowseCategory category, string sort, int page, int size, CancellationToken ct)
    {
        var key = $"browse|{category.AudibleCategoryId}|{category.Keywords}|{category.Author}|{sort}|{page}|{size}";
        if (cache.TryGetValue(key, out AudiobookPage? cached) && cached is not null)
        {
            return cached;
        }

        var result = await metadataClient.BrowseAsync(category.AudibleCategoryId, category.Keywords, sort, page, size, category.Author, ct);
        if (category.Author is not null)
        {
            // Audible's author filter is fuzzy; keep only books that really credit this author.
            result = result with
            {
                Items = result.Items
                    .Where(i => (i.AllAuthors is { Count: > 0 } ? i.AllAuthors : [i.Author]).Any(a => TitleMatcher.AuthorsMatch(a, category.Author)))
                    .ToList(),
            };
        }
        // Don't pin an empty result (a transient Audible hiccup) for half an hour.
        cache.Set(key, result, result.Items.Count > 0 ? PageTtl : TimeSpan.FromSeconds(20));
        return result;
    }

    private sealed record Flags(LibraryIndex Library, List<(string Title, string Author)> Requested);

    private async Task<Flags> LoadFlagsAsync(CancellationToken ct)
    {
        var library = await libraryCatalog.GetIndexAsync(ct);
        var requested = await db.BookRequests
            .Where(r => r.Status != BookRequestStatus.Denied)
            .Select(r => new { r.Title, r.Author })
            .ToListAsync(ct);
        return new Flags(library, requested.Select(r => (r.Title, r.Author)).ToList());
    }

    private static IReadOnlyList<BrowseItemDto> Annotate(IReadOnlyList<AudiobookListing> items, Flags flags) =>
        items.Select(i =>
        {
            var normalized = TitleMatcher.NormalizeTitle(i.Title);
            var requested = flags.Requested.Any(r =>
                TitleMatcher.NormalizeTitle(r.Title) == normalized && TitleMatcher.AuthorsMatch(r.Author, i.Author));
            return new BrowseItemDto(
                i.Asin, i.Title, i.Author, i.CoverUrl, i.RuntimeMinutes, i.Rating, i.SeriesTitle,
                flags.Library.IsOwned(i.Title, i.Author),
                flags.Library.FindSimilar(i.Title, i.Author),
                requested,
                i.ReleaseDate);
        }).ToList();

    /// <summary>Flags catalog listings against the library and existing requests, for callers outside the browse screens.</summary>
    public async Task<IReadOnlyList<BrowseItemDto>> AnnotateAsync(IReadOnlyList<AudiobookListing> items, CancellationToken ct) =>
        Annotate(items, await LoadFlagsAsync(ct));
}
