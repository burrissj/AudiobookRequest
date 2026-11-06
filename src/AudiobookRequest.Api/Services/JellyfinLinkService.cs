using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;

namespace AudiobookRequest.Api.Services;

/// <param name="Url">Where to send the person: the book's own page when found, otherwise Jellyfin's home.</param>
/// <param name="Exact">True when the link goes straight to this book.</param>
/// <param name="ItemId">The book's Jellyfin id when found, so the in-app player can play it.</param>
public record JellyfinLinkDto(string Url, bool Exact, string? ItemId = null);

public record AppConfigDto(string? JellyfinUrl, string IosAppUrl, string AndroidAppUrl);

/// <summary>
/// Builds "listen in Jellyfin" links from the address people use on their own devices -- which can differ
/// from the address this server talks to Jellyfin on -- and finds the book live so a freshly finished
/// audiobook links straight to its page.
/// </summary>
public class JellyfinLinkService(SettingsService settingsService, IJellyfinClient jellyfin, ILogger<JellyfinLinkService> logger)
{
    // Jellyfin's own mobile app (it plays audiobooks, unlike the Swiftfin iOS client).
    public const string IosAppUrl = "https://apps.apple.com/us/app/jellyfin-mobile/id1480192618";
    public const string AndroidAppUrl = "https://play.google.com/store/apps/details?id=org.jellyfin.mobile";

    /// <summary>The address to show people: the "for users" one if set, else the server's, without a trailing slash.</summary>
    public static string? BaseUrl(AppSettings s)
    {
        var url = !string.IsNullOrWhiteSpace(s.JellyfinPublicUrl) ? s.JellyfinPublicUrl : s.JellyfinUrl;
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
    }

    public static string DetailsUrl(string baseUrl, string itemId) => $"{baseUrl}/web/#/details?id={Uri.EscapeDataString(itemId)}";

    public static string HomeUrl(string baseUrl) => $"{baseUrl}/web/";

    public async Task<AppConfigDto> GetConfigAsync(CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        var baseUrl = BaseUrl(s);
        return new AppConfigDto(baseUrl is null ? null : HomeUrl(baseUrl), IosAppUrl, AndroidAppUrl);
    }

    /// <returns>null when Jellyfin isn't configured.</returns>
    public async Task<JellyfinLinkDto?> LinkForBookAsync(string title, string author, CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        var baseUrl = BaseUrl(s);
        if (baseUrl is null)
        {
            return null;
        }

        var itemId = await FindItemIdAsync(s, title, author, ct);
        return itemId is null
            ? new JellyfinLinkDto(HomeUrl(baseUrl), false)
            : new JellyfinLinkDto(DetailsUrl(baseUrl, itemId), true, itemId);
    }

    private async Task<string?> FindItemIdAsync(AppSettings s, string title, string author, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.JellyfinUrl) || string.IsNullOrWhiteSpace(s.JellyfinApiKey))
        {
            return null;
        }

        try
        {
            var items = await jellyfin.SearchAudiobooksAsync(s.JellyfinUrl, s.JellyfinApiKey, SearchTermFor(title), ct);
            return items.FirstOrDefault(i => IsSameBook(i, title, author))?.Id;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Jellyfin search for '{Title}' failed; linking to Jellyfin's home page instead", title);
            return null;
        }
    }

    // Jellyfin matches the term inside names, so search on the part of the title before any "(...)" or ": subtitle".
    public static string SearchTermFor(string title)
    {
        var cut = title.IndexOfAny(['(', '[', ':']);
        var head = (cut > 2 ? title[..cut] : title).Trim();
        return head.Length >= 2 ? head : title.Trim();
    }

    public static bool IsSameBook(JellyfinPlayedItem item, string title, string author)
    {
        var titleMatches = TitleMatcher.NormalizeTitle(item.Name) == TitleMatcher.NormalizeTitle(title)
                           || TitleMatcher.IsSimilarTitle(item.Name, title);
        if (!titleMatches)
        {
            return false;
        }

        // Jellyfin's artist list mixes authors and narrators; any one matching is enough, and no artists means the title decides.
        var artists = (item.ArtistItems ?? []).Select(a => a.Name).OfType<string>().ToList();
        return artists.Count == 0 || string.IsNullOrWhiteSpace(author) || artists.Any(a => TitleMatcher.AuthorsMatch(a, author));
    }
}
