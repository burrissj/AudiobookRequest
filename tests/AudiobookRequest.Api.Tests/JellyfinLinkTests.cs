using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public sealed class JellyfinLinkTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly FakeJellyfinClient jellyfin = new();

    private JellyfinLinkService Service() => new(t.Settings, jellyfin, Nulls.Logger<JellyfinLinkService>());

    private static JellyfinPlayedItem Item(string id, string name, params string[] artists) =>
        new() { Id = id, Name = name, ArtistItems = artists.Select(a => new JellyfinNameId { Name = a }).ToList() };

    [Fact]
    public void BaseUrl_PrefersTheAddressForUsersAndDropsTrailingSlashes()
    {
        var settings = new AppSettings { JellyfinUrl = "http://192.168.1.181:8096/", JellyfinPublicUrl = " https://jellyfin.example.net/ " };
        Assert.Equal("https://jellyfin.example.net", JellyfinLinkService.BaseUrl(settings));

        settings.JellyfinPublicUrl = null;
        Assert.Equal("http://192.168.1.181:8096", JellyfinLinkService.BaseUrl(settings));

        settings.JellyfinUrl = "";
        Assert.Null(JellyfinLinkService.BaseUrl(settings));
    }

    [Theory]
    [InlineData("Quicksilver (Fae & Alchemy, #1)", "Quicksilver")]
    [InlineData("Mistborn: The Final Empire", "Mistborn")]
    [InlineData("Project Hail Mary", "Project Hail Mary")]
    [InlineData("The Throne of Broken Gods (Part 2 of 2) [Dramatized Adaptation]", "The Throne of Broken Gods")]
    [InlineData("A (Odd) Title", "A (Odd) Title")] // a cut this early would leave nothing useful to search on
    public void SearchTerm_KeepsTheDistinctiveStartOfTheTitle(string title, string expected) =>
        Assert.Equal(expected, JellyfinLinkService.SearchTermFor(title));

    [Fact]
    public void IsSameBook_MatchesEditionNoiseAndAnyArtist_ButNotAnotherAuthor()
    {
        var item = Item("1", "Quicksilver (Fae & Alchemy #1) (Unabridged)", "Anthony Palmini", "Callie Hart");

        Assert.True(JellyfinLinkService.IsSameBook(item, "Quicksilver (Fae & Alchemy, #1)", "Callie Hart"));
        Assert.False(JellyfinLinkService.IsSameBook(item, "Quicksilver", "Neal Stephenson"));
        Assert.False(JellyfinLinkService.IsSameBook(item, "Brimstone", "Callie Hart"));
    }

    [Fact]
    public void DetailsUrl_PointsAtTheBooksOwnPage() =>
        Assert.Equal("https://jf.example.net/web/#/details?id=abc123", JellyfinLinkService.DetailsUrl("https://jf.example.net", "abc123"));

    [Fact]
    public async Task Link_GoesStraightToTheBookWhenJellyfinHasIt()
    {
        t.CurrentSettings.JellyfinPublicUrl = "https://jf.example.net";
        t.Db.SaveChanges();
        jellyfin.Library.Add(Item("wrong", "Quicksilver Reloaded", "Someone Else"));
        jellyfin.Library.Add(Item("right", "Quicksilver (Fae & Alchemy #1) (Unabridged)", "Callie Hart"));

        var link = await Service().LinkForBookAsync("Quicksilver (Fae & Alchemy, #1)", "Callie Hart", default);

        Assert.NotNull(link);
        Assert.True(link.Exact);
        Assert.Equal("https://jf.example.net/web/#/details?id=right", link.Url);
        Assert.Equal(["Quicksilver"], jellyfin.SearchTerms);
    }

    [Fact]
    public async Task Link_FallsBackToJellyfinsHomeWhenTheBookIsNotThereYet()
    {
        var link = await Service().LinkForBookAsync("A Book Not Scanned Yet", "Nobody", default);

        Assert.NotNull(link);
        Assert.False(link.Exact);
        Assert.Equal("http://jellyfin/web/", link.Url);
    }

    [Fact]
    public async Task Link_StillWorksWhenTheSearchFails()
    {
        jellyfin.SearchError = new HttpRequestException("Jellyfin is down");

        var link = await Service().LinkForBookAsync("Quicksilver", "Callie Hart", default);

        Assert.False(link!.Exact);
        Assert.Equal("http://jellyfin/web/", link.Url);
    }

    [Fact]
    public async Task Link_IsNullWhenJellyfinIsNotConfigured()
    {
        t.CurrentSettings.JellyfinUrl = null;
        t.CurrentSettings.JellyfinPublicUrl = null;
        t.Db.SaveChanges();

        Assert.Null(await Service().LinkForBookAsync("Quicksilver", "Callie Hart", default));
        Assert.Null((await Service().GetConfigAsync(default)).JellyfinUrl);
    }

    [Fact]
    public async Task Config_GivesTheHomeLinkAndTheOfficialAppStoreLinksButNeverAKey()
    {
        t.CurrentSettings.JellyfinPublicUrl = "https://jf.example.net";
        t.Db.SaveChanges();

        var config = await Service().GetConfigAsync(default);

        Assert.Equal("https://jf.example.net/web/", config.JellyfinUrl);
        Assert.StartsWith("https://apps.apple.com/", config.IosAppUrl);
        Assert.Contains("org.jellyfin.mobile", config.AndroidAppUrl);
        Assert.DoesNotContain("key", System.Text.Json.JsonSerializer.Serialize(config), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => t.Dispose();
}
