using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace AudiobookRequest.Core.Clients;

public class AudiobookMetadataClient(HttpClient httpClient, ILogger<AudiobookMetadataClient> logger) : IAudiobookMetadataClient
{
    private const string AudibleBaseUrl = "https://api.audible.com/1.0/catalog/products";
    private const string AudnexusBaseUrl = "https://api.audnex.us/books";

    public async Task<AudiobookPage> BrowseAsync(string? audibleCategoryId, string? keywords, string sort, int page, int pageSize, string? author = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"{AudibleBaseUrl}?num_results={pageSize}&page={page}&products_sort_by={Uri.EscapeDataString(sort)}" +
                      "&response_groups=contributors,media,rating,product_attrs,series&image_sizes=500" +
                      // "Product" = actual audiobooks; without it genre results are mostly podcast episodes.
                      "&content_type=Product";
            if (!string.IsNullOrWhiteSpace(audibleCategoryId))
            {
                url += $"&category_id={Uri.EscapeDataString(audibleCategoryId)}";
            }
            if (!string.IsNullOrWhiteSpace(keywords))
            {
                url += $"&keywords={Uri.EscapeDataString(keywords)}";
            }
            if (!string.IsNullOrWhiteSpace(author))
            {
                url += $"&author={Uri.EscapeDataString(author)}";
            }

            return await FetchPageAsync(url, ct);
        }
        catch (Exception ex)
        {
            logger.LogFailure(ex, "Audible browse failed (category {Category}, keywords '{Keywords}')", audibleCategoryId, keywords);
            return new AudiobookPage([], 0);
        }
    }

    public async Task<AudiobookPage> SearchAsync(string title, string? author, int pageSize, CancellationToken ct = default)
    {
        try
        {
            // No page= here: Audible counts pages from 0, so page=1 would be the (usually empty) second page.
            var url = $"{AudibleBaseUrl}?num_results={pageSize}&products_sort_by=Relevance" +
                      "&response_groups=contributors,media,rating,product_attrs,series&image_sizes=500&content_type=Product" +
                      $"&title={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrWhiteSpace(author))
            {
                url += $"&author={Uri.EscapeDataString(author)}";
            }
            return await FetchPageAsync(url, ct);
        }
        catch (Exception ex)
        {
            logger.LogFailure(ex, "Audible title search failed for '{Title}'", title);
            return new AudiobookPage([], 0);
        }
    }

    private async Task<AudiobookPage> FetchPageAsync(string url, CancellationToken ct)
    {
        var response = await httpClient.GetFromJsonAsync<AudibleSearchResponse>(url, ct);
        var items = (response?.Products ?? [])
            .Select(ToListing)
            .OfType<AudiobookListing>()
            .ToList();
        return new AudiobookPage(items, response?.TotalResults ?? 0);
    }

    // Skips anything a browse grid can't show well: no cover, non-English, a podcast, or a placeholder for an unannounced book.
    private static AudiobookListing? ToListing(AudibleProductRaw p)
    {
        var cover = p.ProductImages is not null && p.ProductImages.TryGetValue("500", out var c) ? c : p.ProductImages?.Values.FirstOrDefault();
        if (string.IsNullOrEmpty(p.Asin) || string.IsNullOrEmpty(p.Title) || string.IsNullOrEmpty(cover)
            || (p.Language is not null && !p.Language.Equals("english", StringComparison.OrdinalIgnoreCase))
            || p.Title.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase)
            || (p.ContentDeliveryType is not null && p.ContentDeliveryType is not ("SinglePartBook" or "MultiPartBook")))
        {
            return null;
        }

        var distribution = p.Rating?.OverallDistribution;
        var series = PrimarySeries(p);
        return new AudiobookListing(
            p.Asin, p.Title, p.Authors?.FirstOrDefault()?.Name ?? "Unknown author", cover,
            p.RuntimeLengthMin is > 0 ? p.RuntimeLengthMin : null,
            distribution?.NumRatings is > 0 ? distribution.AverageRating : null,
            distribution?.NumRatings,
            series?.Title, series?.Sequence,
            (p.Narrators ?? []).Select(n => n.Name).OfType<string>().ToList(),
            (p.Authors ?? []).Select(a => a.Name).OfType<string>().ToList(),
            p.ReleaseDate,
            series?.Asin);
    }

    /// <summary>
    /// The series a book is really numbered in. Audible can list several -- "The Cosmere" (no number, a whole universe) ahead of
    /// "The Mistborn Saga" (book 1) -- so the first one that has a number is taken, else the first listed.
    /// </summary>
    private static AudibleSeriesRaw? PrimarySeries(AudibleProductRaw p) =>
        p.Series?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.Sequence)) ?? p.Series?.FirstOrDefault();

    public async Task<AudiobookMetadata?> TryResolveDetailAsync(string title, string authorName, CancellationToken ct = default)
    {
        try
        {
            var product = await FindProductAsync(title, authorName, ct);
            if (string.IsNullOrEmpty(product?.Asin))
            {
                return null;
            }

            var detail = await httpClient.GetFromJsonAsync<AudnexusBookRaw>($"{AudnexusBaseUrl}/{product.Asin}?region=us", ct);
            if (detail is null)
            {
                return null;
            }

            var narrators = (detail.Narrators ?? [])
                .Select(n => n.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();
            var genres = (detail.Genres ?? [])
                .Where(g => g.Type == "genre" && !string.IsNullOrEmpty(g.Name))
                .Select(g => g.Name!)
                .ToList();
            var series = PrimarySeries(product);

            return new AudiobookMetadata(
                detail.RuntimeLengthMin, narrators, genres,
                detail.Summary, detail.FormatType, detail.Language, detail.PublisherName,
                series?.Title, series?.Asin, series?.Sequence);
        }
        catch (Exception ex)
        {
            logger.LogFailure(ex, "Audiobook metadata resolution failed for '{Title}' by {Author}", title, authorName);
            return null;
        }
    }

    /// <summary>
    /// Finds the Audible listing for a book. Titles from a media library are often not Audible's exact title ("... (Unabridged)")
    /// and some books have no author on file, so after the exact title-and-author search it tries the title with the extras
    /// removed, then the title alone -- accepting a title-only hit only when its title really is the same.
    /// </summary>
    private async Task<AudibleProductRaw?> FindProductAsync(string title, string authorName, CancellationToken ct)
    {
        var hasAuthor = !string.IsNullOrWhiteSpace(authorName);
        var cleaned = CleanTitle(title);
        var attempts = new List<(string Title, string? Author)>();
        if (hasAuthor)
        {
            attempts.Add((title, authorName));
            if (cleaned.Length > 0 && cleaned != title)
            {
                attempts.Add((cleaned, authorName));
            }
        }
        attempts.Add((cleaned.Length > 0 ? cleaned : title, null));

        foreach (var (searchTitle, searchAuthor) in attempts)
        {
            var url = $"{AudibleBaseUrl}?title={Uri.EscapeDataString(searchTitle)}" +
                      (searchAuthor is null ? "" : $"&author={Uri.EscapeDataString(searchAuthor)}") +
                      $"&num_results={(searchAuthor is null ? 5 : 1)}&response_groups=product_desc,series";
            var response = await httpClient.GetFromJsonAsync<AudibleSearchResponse>(url, ct);
            var wanted = NormalizeTitle(searchTitle);
            var product = searchAuthor is null
                ? response?.Products.FirstOrDefault(p => !string.IsNullOrEmpty(p.Asin) && NormalizeTitle(p.Title ?? "") == wanted)
                : response?.Products.FirstOrDefault();
            if (!string.IsNullOrEmpty(product?.Asin))
            {
                return product;
            }
        }

        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex BracketedText = new(@"[\(\[][^\)\]]*[\)\]]");
    private static readonly System.Text.RegularExpressions.Regex AbridgedSuffix = new(@"\s*[-:–]\s*(un)?abridged\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The title without "(Unabridged)", "[Dramatized Adaptation]" and the like.</summary>
    private static string CleanTitle(string title) =>
        AbridgedSuffix.Replace(BracketedText.Replace(title, " "), "").Trim();

    private static string NormalizeTitle(string title) =>
        new string(CleanTitle(title).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public async Task<IReadOnlyList<AudiobookCatalogEntry>> GetSeriesBooksAsync(string seriesTitle, string authorName, CancellationToken ct = default)
    {
        try
        {
            var url = $"{AudibleBaseUrl}?keywords={Uri.EscapeDataString(seriesTitle)}&author={Uri.EscapeDataString(authorName)}&num_results=20&response_groups=product_desc,contributors,series";
            var response = await httpClient.GetFromJsonAsync<AudibleSearchResponse>(url, ct);
            return (response?.Products ?? [])
                .Where(p => !string.IsNullOrEmpty(p.Asin) && !string.IsNullOrEmpty(p.Title))
                .Select(p =>
                {
                    var series = PrimarySeries(p);
                    return new AudiobookCatalogEntry(p.Asin!, p.Title!, p.Authors?.FirstOrDefault()?.Name, series?.Asin, series?.Sequence);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogFailure(ex, "Audible series search failed for '{SeriesTitle}' by {Author}", seriesTitle, authorName);
            return [];
        }
    }
}
