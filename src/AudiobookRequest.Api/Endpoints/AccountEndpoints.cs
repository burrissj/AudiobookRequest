using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Endpoints;

/// <param name="NtfyServer">Where a personal ntfy topic is published: the admin's ntfy server, or ntfy.sh when none is set.</param>
public record ChannelAvailability(bool Ntfy, bool Telegram, bool Email, string NtfyServer);

public record AccountNotificationsDto(
    string? NtfyTopic,
    string? TelegramChatId,
    string? Email,
    bool OnReady,
    bool OnFailed,
    bool OnNewRelease,
    ChannelAvailability Available);

public record UpdateAccountNotificationsRequest(
    string? NtfyTopic,
    string? TelegramChatId,
    string? Email,
    bool OnReady,
    bool OnFailed,
    bool OnNewRelease);

public record AccountTestResponse(IReadOnlyList<ChannelResult> Results, bool AnyConfigured);

/// <summary>A signed-in user's own settings: where they want their personal notifications sent.</summary>
public static partial class AccountEndpoints
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex TopicRegex();

    // Telegram chat ids are integers (negative for groups); the Telegram Bot API accepts nothing else we'd use here.
    [GeneratedRegex(@"^-?\d{3,20}$")]
    private static partial Regex ChatIdRegex();

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account").RequireAuthorization();

        group.MapGet("/notifications", async (ClaimsPrincipal principal, AppDbContext db, SettingsService settingsService, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId(principal), ct);
            return user is null ? Results.NotFound() : Results.Ok(await ToDto(user, settingsService, ct));
        });

        group.MapPut("/notifications", async (
            UpdateAccountNotificationsRequest req,
            ClaimsPrincipal principal,
            AppDbContext db,
            SettingsService settingsService,
            CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId(principal), ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            var (topic, chatId, email, error) = Validate(req);
            if (error is not null)
            {
                return Results.Problem(error, statusCode: 400);
            }

            user.NotifyNtfyTopic = topic;
            user.NotifyTelegramChatId = chatId;
            user.NotifyEmail = email;
            user.NotifyOnReady = req.OnReady;
            user.NotifyOnFailed = req.OnFailed;
            user.NotifyOnNewRelease = req.OnNewRelease;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ToDto(user, settingsService, ct));
        });

        group.MapPost("/notifications/test", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            SettingsService settingsService,
            IExternalNotifier notifier,
            CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId(principal), ct);
            if (user is null)
            {
                return Results.NotFound();
            }
            var results = await notifier.TestUserAsync(await settingsService.GetAsync(ct), user, ct);
            return Results.Ok(new AccountTestResponse(results, results.Count > 0));
        });

        return app;
    }

    /// <summary>Trims the three contact fields (blank = not set) and rejects anything that could not be used as a destination.</summary>
    public static (string? Topic, string? ChatId, string? Email, string? Error) Validate(UpdateAccountNotificationsRequest req)
    {
        var topic = Clean(req.NtfyTopic);
        if (topic is not null && !TopicRegex().IsMatch(topic))
        {
            return (null, null, null, "An ntfy topic can only use letters, numbers, dashes and underscores (up to 64).");
        }
        var chatId = Clean(req.TelegramChatId);
        if (chatId is not null && !ChatIdRegex().IsMatch(chatId))
        {
            return (null, null, null, "A Telegram chat ID is a number such as 123456789.");
        }
        var email = Clean(req.Email);
        if (email is not null && !IsEmail(email))
        {
            return (null, null, null, "That doesn't look like an email address.");
        }
        return (topic, chatId, email, null);
    }

    private static async Task<AccountNotificationsDto> ToDto(Core.Entities.AppUser user, SettingsService settingsService, CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        var ntfyServer = string.IsNullOrWhiteSpace(s.NotifyNtfyUrl) ? "https://ntfy.sh" : new Uri(s.NotifyNtfyUrl).GetLeftPart(UriPartial.Authority);
        var available = new ChannelAvailability(
            Ntfy: true,
            Telegram: !string.IsNullOrWhiteSpace(s.NotifyTelegramBotToken),
            Email: !string.IsNullOrWhiteSpace(s.SmtpHost) && !string.IsNullOrWhiteSpace(s.SmtpFrom),
            NtfyServer: ntfyServer);
        return new AccountNotificationsDto(
            user.NotifyNtfyTopic, user.NotifyTelegramChatId, user.NotifyEmail,
            user.NotifyOnReady, user.NotifyOnFailed, user.NotifyOnNewRelease, available);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return address.Address == value && value.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static int UserId(ClaimsPrincipal principal) => int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
