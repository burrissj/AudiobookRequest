using System.Net;
using System.Text.Json;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public class ExternalNotifierTests
{
    private static (ExternalNotifier Notifier, FakeHttpHandler Handler) Build(HttpStatusCode? status = null)
    {
        var handler = new FakeHttpHandler(status ?? HttpStatusCode.OK);
        return (new ExternalNotifier(new HttpClient(handler), Nulls.Logger<ExternalNotifier>()), handler);
    }

    private static BookRequest Request() => new()
    {
        Title = "Project Hail Mary",
        Author = "Andy Weir",
        ChaptarrForeignBookId = "gr:1",
        RequestedByUser = new AppUser { JellyfinUserId = "j", Username = "victoria" },
    };

    [Fact]
    public async Task ReadyNotification_GoesToEveryConfiguredChannel()
    {
        var (notifier, handler) = Build();
        var settings = new AppSettings
        {
            NotifyDiscordWebhookUrl = "https://discord.test/hook",
            NotifyTelegramBotToken = "123:abc",
            NotifyTelegramChatId = "42",
            NotifyNtfyUrl = "https://ntfy.test/audiobooks",
            NotifyWebhookUrl = "https://hooks.test/abr",
        };

        await notifier.NotifyAsync(settings, ExternalNotifier.Ready(Request()));

        Assert.Equal(4, handler.Requests.Count);
        var discord = handler.Requests.Single(r => r.Uri!.Host == "discord.test");
        Assert.Contains("Project Hail Mary", discord.Body);
        Assert.Contains("requested by victoria", discord.Body);

        var telegram = handler.Requests.Single(r => r.Uri!.Host == "api.telegram.org");
        Assert.Equal("/bot123:abc/sendMessage", telegram.Uri!.AbsolutePath);
        Assert.Equal("42", JsonDocument.Parse(telegram.Body).RootElement.GetProperty("chat_id").GetString());

        var ntfy = handler.Requests.Single(r => r.Uri!.Host == "ntfy.test");
        Assert.Equal("/", ntfy.Uri!.AbsolutePath);
        Assert.Equal("audiobooks", JsonDocument.Parse(ntfy.Body).RootElement.GetProperty("topic").GetString());

        var webhook = JsonDocument.Parse(handler.Requests.Single(r => r.Uri!.Host == "hooks.test").Body).RootElement;
        Assert.Equal("RequestReady", webhook.GetProperty("event").GetString());
        Assert.Equal("victoria", webhook.GetProperty("user").GetString());
    }

    [Fact]
    public async Task DisabledEventKind_SendsNothing()
    {
        var (notifier, handler) = Build();
        var settings = new AppSettings { NotifyDiscordWebhookUrl = "https://discord.test/hook", NotifyOnReady = false };

        await notifier.NotifyAsync(settings, ExternalNotifier.Ready(Request()));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NoChannelsConfigured_IsANoOp()
    {
        var (notifier, handler) = Build();

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.Ready(Request()));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FailingChannel_NeverThrowsOrBlocksOthers()
    {
        var (notifier, handler) = Build(HttpStatusCode.InternalServerError);
        var settings = new AppSettings { NotifyDiscordWebhookUrl = "https://discord.test/hook", NotifyWebhookUrl = "https://hooks.test/abr" };

        await notifier.NotifyAsync(settings, ExternalNotifier.Failed(Request(), "nothing found"));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Test_ReportsEachChannelsOutcomeEvenWhenEventSwitchesAreOff()
    {
        var (notifier, _) = Build(HttpStatusCode.BadRequest);
        var settings = new AppSettings
        {
            NotifyOnReady = false,
            NotifyOnFailed = false,
            NotifyOnPendingApproval = false,
            NotifyDiscordWebhookUrl = "https://discord.test/hook",
        };

        var results = await notifier.TestAsync(settings);

        var result = Assert.Single(results);
        Assert.Equal("Discord", result.Channel);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ApprovalNeeded_NamesTheRequesterAndBook()
    {
        var (notifier, handler) = Build();

        await notifier.NotifyAsync(new AppSettings { NotifyDiscordWebhookUrl = "https://discord.test/hook" }, ExternalNotifier.NeedsApproval(Request()));

        var body = handler.Requests.Single().Body;
        Assert.Contains("victoria requested Project Hail Mary by Andy Weir", body);
    }
}
