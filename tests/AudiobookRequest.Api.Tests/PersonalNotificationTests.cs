using System.Text.Json;
using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Tests;

public class PersonalNotificationTests
{
    private static (ExternalNotifier Notifier, FakeHttpHandler Handler) Build()
    {
        var handler = new FakeHttpHandler();
        return (new ExternalNotifier(new HttpClient(handler), Nulls.Logger<ExternalNotifier>()), handler);
    }

    private static AppUser Victoria(Action<AppUser>? configure = null)
    {
        var user = new AppUser { JellyfinUserId = "j", Username = "victoria" };
        configure?.Invoke(user);
        return user;
    }

    private static BookRequest RequestBy(AppUser user) => new()
    {
        Title = "Project Hail Mary",
        Author = "Andy Weir",
        ChaptarrForeignBookId = "gr:1",
        RequestedByUser = user,
    };

    [Fact]
    public async Task ReadyForAUser_ReachesTheirOwnNtfyTopic_OnPublicNtfyWhenTheAdminHasNoServer()
    {
        var (notifier, handler) = Build();
        var user = Victoria(u => u.NotifyNtfyTopic = "victoria-books-x7");

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.Ready(RequestBy(user)));

        var call = Assert.Single(handler.Requests);
        Assert.Equal("ntfy.sh", call.Uri!.Host);
        var body = JsonDocument.Parse(call.Body).RootElement;
        Assert.Equal("victoria-books-x7", body.GetProperty("topic").GetString());
        Assert.Contains("Project Hail Mary", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PersonalNtfy_UsesTheAdminsNtfyServerWhenOneIsConfigured()
    {
        var (notifier, handler) = Build();
        var settings = new AppSettings { NotifyNtfyUrl = "https://ntfy.home.test/household-topic" };
        var user = Victoria(u => u.NotifyNtfyTopic = "victoria-only");

        await notifier.NotifyAsync(settings, ExternalNotifier.Ready(RequestBy(user)));

        // Household topic and the user's own topic, both on the admin's server.
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("ntfy.home.test", r.Uri!.Host));
        var topics = handler.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("topic").GetString()).ToList();
        Assert.Contains("household-topic", topics);
        Assert.Contains("victoria-only", topics);
    }

    [Fact]
    public async Task PersonalTelegram_NeedsTheAdminsBotAndTheUsersChatId()
    {
        var (notifier, handler) = Build();
        var user = Victoria(u => u.NotifyTelegramChatId = "123456789");

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.Ready(RequestBy(user)));
        Assert.Empty(handler.Requests); // no bot token configured, so nothing can be sent

        await notifier.NotifyAsync(new AppSettings { NotifyTelegramBotToken = "123:abc" }, ExternalNotifier.Ready(RequestBy(user)));
        var call = Assert.Single(handler.Requests);
        Assert.Equal("/bot123:abc/sendMessage", call.Uri!.AbsolutePath);
        Assert.Equal("123456789", JsonDocument.Parse(call.Body).RootElement.GetProperty("chat_id").GetString());
    }

    [Fact]
    public async Task ThePersonalEventSwitches_ControlOnlyThatUsersChannels()
    {
        var (notifier, handler) = Build();
        var user = Victoria(u =>
        {
            u.NotifyNtfyTopic = "victoria";
            u.NotifyOnReady = false;
        });

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.Ready(RequestBy(user)));
        Assert.Empty(handler.Requests);

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.Failed(RequestBy(user), "nothing found"));
        Assert.Single(handler.Requests); // failures are still on
    }

    [Fact]
    public async Task TheHouseholdSwitchesStillDecideWhatTheHouseholdChannelsGet_NotWhatAUserGets()
    {
        var (notifier, handler) = Build();
        var settings = new AppSettings { NotifyOnReady = false, NotifyDiscordWebhookUrl = "https://discord.test/hook" };
        var user = Victoria(u => u.NotifyNtfyTopic = "victoria");

        await notifier.NotifyAsync(settings, ExternalNotifier.Ready(RequestBy(user)));

        var call = Assert.Single(handler.Requests);
        Assert.Equal("ntfy.sh", call.Uri!.Host); // the household Discord is off for "ready", the user's own topic is not
    }

    [Fact]
    public async Task ApprovalRequests_GoToAdminsNeverBackToTheRequester()
    {
        var (notifier, handler) = Build();
        var user = Victoria(u => u.NotifyNtfyTopic = "victoria");

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.NeedsApproval(RequestBy(user)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NewReleases_ReachTheFollower()
    {
        var (notifier, handler) = Build();
        var user = Victoria(u => u.NotifyNtfyTopic = "victoria");

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.NewRelease("Brandon Sanderson", user, ["The Fires of December"], autoRequested: false));

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement;
        Assert.Contains("The Fires of December", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ADeclinedRequest_ReachesTheRequestersFailedChannel()
    {
        var (notifier, handler) = Build();
        var user = Victoria(u => u.NotifyNtfyTopic = "victoria");

        await notifier.NotifyAsync(new AppSettings(), ExternalNotifier.Denied(RequestBy(user), "Too many this week"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TestUser_ReportsEachOfTheirChannels_IgnoringTheirEventSwitches()
    {
        var (notifier, handler) = Build();
        var user = Victoria(u =>
        {
            u.NotifyNtfyTopic = "victoria";
            u.NotifyOnReady = u.NotifyOnFailed = u.NotifyOnNewRelease = false;
        });

        var results = await notifier.TestUserAsync(new AppSettings(), user);

        var result = Assert.Single(results);
        Assert.Equal("Your ntfy topic", result.Channel);
        Assert.True(result.Success);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TestUser_WithNothingConfigured_SendsNothing()
    {
        var (notifier, handler) = Build();

        var results = await notifier.TestUserAsync(new AppSettings(), Victoria());

        Assert.Empty(results);
        Assert.Empty(handler.Requests);
    }

    // ---- what a user may save as a destination ----

    private static UpdateAccountNotificationsRequest Update(string? topic = null, string? chat = null, string? email = null) =>
        new(topic, chat, email, true, true, true);

    [Theory]
    [InlineData("victoria-books")]
    [InlineData("abr_7f3k29x1")]
    [InlineData("  padded  ")]
    public void Validate_AcceptsOrdinaryTopics(string topic) =>
        Assert.Null(AccountEndpoints.Validate(Update(topic: topic)).Error);

    [Theory]
    [InlineData("has space")]
    [InlineData("slash/injection")]
    [InlineData("../../x")]
    [InlineData("topic?query=1")]
    public void Validate_RejectsTopicsThatCouldChangeWhereTheMessageGoes(string topic) =>
        Assert.NotNull(AccountEndpoints.Validate(Update(topic: topic)).Error);

    [Theory]
    [InlineData("123456789", true)]
    [InlineData("-1001234567890", true)]
    [InlineData("@somechannel", false)]
    [InlineData("12", false)]
    [InlineData("12 34", false)]
    public void Validate_TelegramChatIdMustBeANumber(string chat, bool ok) =>
        Assert.Equal(ok, AccountEndpoints.Validate(Update(chat: chat)).Error is null);

    [Theory]
    [InlineData("victoria@example.com", true)]
    [InlineData("v.k+books@mail.example.co", true)]
    [InlineData("not an email", false)]
    [InlineData("missing@tld", false)]
    [InlineData("Name <a@b.com>", false)]
    public void Validate_EmailMustBeABareAddress(string email, bool ok) =>
        Assert.Equal(ok, AccountEndpoints.Validate(Update(email: email)).Error is null);

    [Fact]
    public void Validate_TrimsAndTreatsBlankAsNotSet()
    {
        var result = AccountEndpoints.Validate(Update(topic: "  ", chat: "", email: "  v@example.com "));

        Assert.Null(result.Topic);
        Assert.Null(result.ChatId);
        Assert.Equal("v@example.com", result.Email);
    }
}
