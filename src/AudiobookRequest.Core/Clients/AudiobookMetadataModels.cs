using System.Text.Json.Serialization;

namespace AudiobookRequest.Core.Clients;

/// <summary>
/// A catalog search hit from Audible's public product search -- used for genre/keyword discovery and
/// for series expansion. SeriesAsin/SeriesSequence are only populated when the caller requested the
/// "series" response group; see AudiobookSeries.IsSingleBook/IsBundle for how Sequence is interpreted
/// ("3" = the 3rd book, "1-3" = a 3-book omnibus).
/// </summary>
public record AudiobookCatalogEntry(string Asin, string Title, string? AuthorName, string? SeriesAsin = null, string? SeriesSequence = null);

/// <summary>A book as shown in a browse grid: enough for a cover card, no Chaptarr lookup involved.</summary>
public record AudiobookListing(
    string Asin,
    string Title,
    string Author,
    string CoverUrl,
    int? RuntimeMinutes,
    double? Rating,
    int? RatingCount,
    string? SeriesTitle,
    string? SeriesSequence,
    IReadOnlyList<string> Narrators,
    IReadOnlyList<string>? AllAuthors = null,
    string? ReleaseDate = null,
    string? SeriesAsin = null);

/// <summary>One page of a browse query. TotalResults is Audible's own (fuzzy, often huge) match count.</summary>
public record AudiobookPage(IReadOnlyList<AudiobookListing> Items, int TotalResults);

/// <summary>Best-effort audiobook detail resolved via Audible search + Audnexus lookup. All fields may be absent.</summary>
public record AudiobookMetadata(
    int? RuntimeMinutes,
    IReadOnlyList<string> Narrators,
    IReadOnlyList<string> Genres,
    string? Description,
    string? FormatType,
    string? Language,
    string? PublisherName,
    string? SeriesTitle,
    string? SeriesAsin,
    string? SeriesSequence);

internal class AudibleSearchResponse
{
    [JsonPropertyName("products")]
    public List<AudibleProductRaw> Products { get; set; } = [];

    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
}

internal class AudibleProductRaw
{
    [JsonPropertyName("asin")]
    public string? Asin { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("authors")]
    public List<AudibleContributorRaw>? Authors { get; set; }

    [JsonPropertyName("series")]
    public List<AudibleSeriesRaw>? Series { get; set; }

    [JsonPropertyName("narrators")]
    public List<AudibleContributorRaw>? Narrators { get; set; }

    // "2026-10-06" -- may be in the future for announced books.
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("runtime_length_min")]
    public int? RuntimeLengthMin { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    // SinglePartBook / MultiPartBook are audiobooks; the catalog also holds podcasts and their episodes.
    [JsonPropertyName("content_delivery_type")]
    public string? ContentDeliveryType { get; set; }

    [JsonPropertyName("product_images")]
    public Dictionary<string, string>? ProductImages { get; set; }

    [JsonPropertyName("rating")]
    public AudibleRatingRaw? Rating { get; set; }
}

internal class AudibleRatingRaw
{
    [JsonPropertyName("overall_distribution")]
    public AudibleRatingDistributionRaw? OverallDistribution { get; set; }
}

internal class AudibleRatingDistributionRaw
{
    [JsonPropertyName("average_rating")]
    public double? AverageRating { get; set; }

    [JsonPropertyName("num_ratings")]
    public int? NumRatings { get; set; }
}

internal class AudibleSeriesRaw
{
    [JsonPropertyName("asin")]
    public string? Asin { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // "3" for the 3rd book in the series; "1-3" for an omnibus spanning books 1 through 3; can be blank.
    [JsonPropertyName("sequence")]
    public string? Sequence { get; set; }
}

internal class AudibleContributorRaw
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class AudnexusBookRaw
{
    [JsonPropertyName("runtimeLengthMin")]
    public int? RuntimeLengthMin { get; set; }

    [JsonPropertyName("narrators")]
    public List<AudibleContributorRaw>? Narrators { get; set; }

    [JsonPropertyName("genres")]
    public List<AudnexusGenreRaw>? Genres { get; set; }

    // "description" is a short tagline; "summary" is the full, HTML-formatted blurb -- confirmed live
    // (Project Hail Mary: description was 153 chars, summary was 2013 chars of real synopsis).
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("formatType")]
    public string? FormatType { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("publisherName")]
    public string? PublisherName { get; set; }
}

internal class AudnexusGenreRaw
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
