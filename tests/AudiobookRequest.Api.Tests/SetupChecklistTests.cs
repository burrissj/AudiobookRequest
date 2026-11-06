using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public class SetupChecklistTests
{
    private static HealthCheck Check(string name, string status = "ok", string detail = "fine") => new(name, status, detail);

    private static List<HealthCheck> AllGood() =>
    [
        Check("Chaptarr"), Check("Jellyfin"), Check("ffmpeg"), Check("ffprobe"),
        Check("Staging folder"), Check("Library folder"), Check("Chaptarr files reachable"),
    ];

    private static LibraryStatus Library(int books = 12) => new(books, DateTimeOffset.UtcNow, true);

    private static ChecklistItem Item(IReadOnlyList<ChecklistItem> list, string key) => list.Single(i => i.Key == key);

    [Fact]
    public void EverythingWorking_MeansAllRequiredItemsAreDone()
    {
        var list = SetupChecklist.Build(AllGood(), new AppSettings(), Library(), backupCount: 2, isHttps: true);

        foreach (var key in new[] { "chaptarr", "jellyfin", "ffmpeg", "folders", "files", "library" })
        {
            Assert.Equal("done", Item(list, key).Status);
            Assert.Null(Item(list, key).Action);
        }
    }

    [Theory]
    [InlineData("Chaptarr", "chaptarr")]
    [InlineData("Jellyfin", "jellyfin")]
    [InlineData("ffmpeg", "ffmpeg")]
    [InlineData("ffprobe", "ffmpeg")]
    [InlineData("Staging folder", "folders")]
    [InlineData("Library folder", "folders")]
    [InlineData("Chaptarr files reachable", "files")]
    public void AFailingCheck_TurnsItsRequiredItemIntoATodoWithAWayToFixIt(string failing, string key)
    {
        var checks = AllGood().Select(c => c.Name == failing ? c with { Status = "fail", Detail = "broken because reasons" } : c).ToList();

        var item = Item(SetupChecklist.Build(checks, new AppSettings(), Library(), 1, true), key);

        Assert.Equal("todo", item.Status);
        Assert.False(string.IsNullOrWhiteSpace(item.Action));
    }

    [Fact]
    public void AWarningOnTheFilePathCheck_IsStillATodo_BecauseBooksCannotBePickedUp()
    {
        var checks = AllGood().Select(c => c.Name == "Chaptarr files reachable" ? c with { Status = "warn" } : c).ToList();

        Assert.Equal("todo", Item(SetupChecklist.Build(checks, new AppSettings(), Library(), 1, true), "files").Status);
    }

    [Fact]
    public void ANotConfiguredCheck_IsATodoToo()
    {
        var checks = AllGood().Select(c => c.Name == "Chaptarr" ? c with { Status = "skip", Detail = "Not configured." } : c).ToList();

        var item = Item(SetupChecklist.Build(checks, new AppSettings(), Library(), 1, true), "chaptarr");

        Assert.Equal("todo", item.Status);
        Assert.Equal("Not configured.", item.Detail);
    }

    [Fact]
    public void AnEmptyLibraryScan_IsATodo()
    {
        var item = Item(SetupChecklist.Build(AllGood(), new AppSettings(), Library(books: 0), 1, true), "library");

        Assert.Equal("todo", item.Status);
        Assert.Contains("Rescan", item.Action);
    }

    [Fact]
    public void Notifications_AreOptionalUntilAnyHouseholdChannelExists()
    {
        Assert.Equal("optional", Item(SetupChecklist.Build(AllGood(), new AppSettings(), Library(), 1, true), "notifications").Status);

        foreach (var configured in new Action<AppSettings>[]
        {
            s => s.NotifyDiscordWebhookUrl = "https://discord.test/h",
            s => s.NotifyNtfyUrl = "https://ntfy.test/t",
            s => s.NotifyWebhookUrl = "https://hooks.test/x",
            s => { s.NotifyTelegramBotToken = "1:a"; s.NotifyTelegramChatId = "5"; },
            s => { s.SmtpHost = "smtp.test"; s.SmtpFrom = "a@b.test"; s.SmtpTo = "c@d.test"; },
        })
        {
            var settings = new AppSettings();
            configured(settings);
            Assert.Equal("done", Item(SetupChecklist.Build(AllGood(), settings, Library(), 1, true), "notifications").Status);
        }
    }

    [Fact]
    public void ATelegramBotWithoutAChatIsNotAChannel()
    {
        var settings = new AppSettings { NotifyTelegramBotToken = "1:a" };

        Assert.Equal("optional", Item(SetupChecklist.Build(AllGood(), settings, Library(), 1, true), "notifications").Status);
    }

    [Fact]
    public void Https_FollowsTheConnection()
    {
        Assert.Equal("done", Item(SetupChecklist.Build(AllGood(), new AppSettings(), Library(), 1, isHttps: true), "https").Status);
        Assert.Equal("optional", Item(SetupChecklist.Build(AllGood(), new AppSettings(), Library(), 1, isHttps: false), "https").Status);
    }

    [Fact]
    public void Backups_AreDoneOnlyWhenOnAndAtLeastOneExists()
    {
        var on = new AppSettings { BackupRetentionCount = 14 };

        Assert.Equal("done", Item(SetupChecklist.Build(AllGood(), on, Library(), backupCount: 1, true), "backups").Status);

        var waiting = Item(SetupChecklist.Build(AllGood(), on, Library(), backupCount: 0, true), "backups");
        Assert.Equal("optional", waiting.Status);
        Assert.Contains("waiting", waiting.Detail);

        var off = Item(SetupChecklist.Build(AllGood(), new AppSettings { BackupRetentionCount = 0 }, Library(), 0, true), "backups");
        Assert.Equal("optional", off.Status);
        Assert.Contains("off", off.Detail);
        Assert.Equal("Settings → Backups", off.Action);
    }

    [Fact]
    public void TheJellyfinLinkAddress_IsDoneOnceSet()
    {
        Assert.Equal("optional", Item(SetupChecklist.Build(AllGood(), new AppSettings(), Library(), 1, true), "jellyfin-link").Status);

        var set = new AppSettings { JellyfinPublicUrl = "https://jf.example.net" };
        var item = Item(SetupChecklist.Build(AllGood(), set, Library(), 1, true), "jellyfin-link");
        Assert.Equal("done", item.Status);
        Assert.Contains("https://jf.example.net", item.Detail);
    }

    [Fact]
    public void RequiredItemsComeFirst_SoTheProgressCountIsMeaningful()
    {
        var list = SetupChecklist.Build(AllGood(), new AppSettings(), Library(), 0, false);

        var firstOptional = list.ToList().FindIndex(i => i.Status == "optional");
        var lastRequired = list.ToList().FindLastIndex(i => i.Key is "chaptarr" or "jellyfin" or "ffmpeg" or "folders" or "files" or "library");
        Assert.True(lastRequired < firstOptional);
    }
}
