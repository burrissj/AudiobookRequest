using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Services;

public enum NotificationKind { RequestReady, RequestFailed, ApprovalNeeded, RequestDenied, NewRelease, Test }

/// <param name="Recipient">The user this event is about (who asked for the book, who follows the author). Their own personal channels get it too.</param>
public record ExternalNotification(NotificationKind Kind, string Title, string Message, string? Author = null, string? RequestedBy = null, AppUser? Recipient = null);

public record ChannelResult(string Channel, bool Success, string? Error);

public interface IExternalNotifier
{
    /// <summary>Sends to every configured channel that the settings say wants this kind of event. Never throws.</summary>
    Task NotifyAsync(AppSettings settings, ExternalNotification notification, CancellationToken ct = default);

    /// <summary>Sends a test message to every configured channel regardless of the per-event switches, reporting each outcome.</summary>
    Task<IReadOnlyList<ChannelResult>> TestAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>Sends a test message to one user's own channels, regardless of their per-event choices.</summary>
    Task<IReadOnlyList<ChannelResult>> TestUserAsync(AppSettings settings, AppUser user, CancellationToken ct = default);
}

/// <summary>
/// Pushes request events (ready, failed, needs approval) to Discord, Telegram, ntfy, a generic webhook
/// and/or email, so people don't have to keep checking the Requests tab. Channels are global (one
/// Discord channel, one ntfy topic...) and each message names who asked for the book.
/// </summary>
public class ExternalNotifier(HttpClient http, ILogger<ExternalNotifier> logger) : IExternalNotifier
{
    public async Task NotifyAsync(AppSettings settings, ExternalNotification notification, CancellationToken ct = default)
    {
        var householdWanted = notification.Kind switch
        {
            NotificationKind.RequestReady => settings.NotifyOnReady,
            NotificationKind.RequestFailed or NotificationKind.RequestDenied => settings.NotifyOnFailed,
            NotificationKind.ApprovalNeeded => settings.NotifyOnPendingApproval,
            NotificationKind.NewRelease => settings.NotifyOnNewRelease,
            _ => true,
        };

        var jobs = new List<Task<ChannelResult>>();
        if (householdWanted)
        {
            jobs.AddRange(HouseholdJobs(settings, notification, ct));
        }
        if (notification.Recipient is { } user && UserWants(user, notification.Kind))
        {
            jobs.AddRange(PersonalJobs(settings, user, notification, ct));
        }

        foreach (var failure in (await Task.WhenAll(jobs)).Where(r => !r.Success))
        {
            logger.LogWarning("Notification via {Channel} failed: {Error}", failure.Channel, failure.Error);
        }
    }

    // A user's own events: ready/failed/declined for their requests, and new books from what they follow.
    // (Approval requests go to admins through the household channels, never to the requester.)
    private static bool UserWants(AppUser user, NotificationKind kind) => kind switch
    {
        NotificationKind.RequestReady => user.NotifyOnReady,
        NotificationKind.RequestFailed or NotificationKind.RequestDenied => user.NotifyOnFailed,
        NotificationKind.NewRelease => user.NotifyOnNewRelease,
        _ => false,
    };

    public async Task<IReadOnlyList<ChannelResult>> TestUserAsync(AppSettings settings, AppUser user, CancellationToken ct = default)
    {
        var test = new ExternalNotification(NotificationKind.Test, "Audiobook Request", "Test notification -- if you can read this, this channel works.", Recipient: user);
        return await Task.WhenAll(PersonalJobs(settings, user, test, ct));
    }

    public async Task<IReadOnlyList<ChannelResult>> TestAsync(AppSettings settings, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(HouseholdJobs(settings, new ExternalNotification(
            NotificationKind.Test, "Audiobook Request", "Test notification -- if you can read this, this channel works."), ct));
        return results;
    }

    /// <summary>One user's own channels. ntfy rides on the admin's ntfy server (or ntfy.sh), Telegram on the admin's bot, email on the admin's SMTP server.</summary>
    private List<Task<ChannelResult>> PersonalJobs(AppSettings s, AppUser user, ExternalNotification n, CancellationToken ct)
    {
        var jobs = new List<Task<ChannelResult>>();
        var text = Compose(n);

        if (!string.IsNullOrWhiteSpace(user.NotifyNtfyTopic))
        {
            var root = string.IsNullOrWhiteSpace(s.NotifyNtfyUrl) ? "https://ntfy.sh/" : new Uri(s.NotifyNtfyUrl).GetLeftPart(UriPartial.Authority) + "/";
            var topic = user.NotifyNtfyTopic.Trim();
            jobs.Add(Run("Your ntfy topic", () => PostJson(root, new { topic, title = n.Title, message = n.Message, tags = new[] { TagFor(n.Kind) } }, ct)));
        }
        if (!string.IsNullOrWhiteSpace(user.NotifyTelegramChatId) && !string.IsNullOrWhiteSpace(s.NotifyTelegramBotToken))
        {
            var url = $"https://api.telegram.org/bot{s.NotifyTelegramBotToken}/sendMessage";
            jobs.Add(Run("Your Telegram", () => PostJson(url, new { chat_id = user.NotifyTelegramChatId.Trim(), text = Truncate(text, 4000) }, ct)));
        }
        if (!string.IsNullOrWhiteSpace(user.NotifyEmail) && !string.IsNullOrWhiteSpace(s.SmtpHost) && !string.IsNullOrWhiteSpace(s.SmtpFrom))
        {
            jobs.Add(Run("Your email", () => SendEmail(s, n, text, user.NotifyEmail.Trim(), ct)));
        }
        return jobs;
    }

    private List<Task<ChannelResult>> HouseholdJobs(AppSettings s, ExternalNotification n, CancellationToken ct)
    {
        var jobs = new List<Task<ChannelResult>>();
        var text = Compose(n);

        if (!string.IsNullOrWhiteSpace(s.NotifyDiscordWebhookUrl))
        {
            jobs.Add(Run("Discord", () => PostJson(s.NotifyDiscordWebhookUrl, new { content = Truncate(text, 1900) }, ct)));
        }
        if (!string.IsNullOrWhiteSpace(s.NotifyTelegramBotToken) && !string.IsNullOrWhiteSpace(s.NotifyTelegramChatId))
        {
            var url = $"https://api.telegram.org/bot{s.NotifyTelegramBotToken}/sendMessage";
            jobs.Add(Run("Telegram", () => PostJson(url, new { chat_id = s.NotifyTelegramChatId, text = Truncate(text, 4000) }, ct)));
        }
        if (!string.IsNullOrWhiteSpace(s.NotifyNtfyUrl))
        {
            jobs.Add(Run("ntfy", () => SendNtfy(s.NotifyNtfyUrl, n, ct)));
        }
        if (!string.IsNullOrWhiteSpace(s.NotifyWebhookUrl))
        {
            jobs.Add(Run("Webhook", () => PostJson(s.NotifyWebhookUrl, new
            {
                @event = n.Kind.ToString(), title = n.Title, author = n.Author, user = n.RequestedBy, message = n.Message,
            }, ct)));
        }
        if (!string.IsNullOrWhiteSpace(s.SmtpHost) && !string.IsNullOrWhiteSpace(s.SmtpFrom) && !string.IsNullOrWhiteSpace(s.SmtpTo))
        {
            jobs.Add(Run("Email", () => SendEmail(s, n, text, s.SmtpTo, ct)));
        }

        return jobs;
    }

    private static async Task<ChannelResult> Run(string channel, Func<Task> send)
    {
        try
        {
            await send();
            return new ChannelResult(channel, true, null);
        }
        catch (Exception ex)
        {
            return new ChannelResult(channel, false, ex.Message);
        }
    }

    private async Task PostJson(string url, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendNtfy(string topicUrl, ExternalNotification n, CancellationToken ct)
    {
        // JSON publishing (POST to the server root) avoids ntfy's ASCII-only header limitation for titles.
        var uri = new Uri(topicUrl);
        var topic = uri.Segments.Last().Trim('/');
        var root = uri.GetLeftPart(UriPartial.Authority) + "/";
        await PostJson(root, new { topic, title = n.Title, message = n.Message, tags = new[] { TagFor(n.Kind) } }, ct);
    }

    private static async Task SendEmail(AppSettings s, ExternalNotification n, string text, string recipients, CancellationToken ct)
    {
        using var client = new SmtpClient(s.SmtpHost, s.SmtpPort) { EnableSsl = s.SmtpUseSsl };
        if (!string.IsNullOrWhiteSpace(s.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(s.SmtpUsername, s.SmtpPassword);
        }
        using var mail = new MailMessage { From = new MailAddress(s.SmtpFrom!), Subject = n.Title, Body = text };
        foreach (var to in recipients.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            mail.To.Add(to);
        }
        await client.SendMailAsync(mail, ct);
    }

    private static string Compose(ExternalNotification n) =>
        n.Kind == NotificationKind.Test ? n.Message : $"{n.Title}\n{n.Message}";

    private static string TagFor(NotificationKind kind) => kind switch
    {
        NotificationKind.RequestReady => "white_check_mark",
        NotificationKind.RequestFailed or NotificationKind.RequestDenied => "x",
        NotificationKind.ApprovalNeeded => "raised_hand",
        NotificationKind.NewRelease => "sparkles",
        _ => "bell",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // Convenience builders so call sites stay one-liners and the wording lives in one place.
    public static ExternalNotification Ready(BookRequest r) =>
        new(NotificationKind.RequestReady, $"Ready to listen: {r.Title}", $"{r.Title} by {r.Author} is in the library.{By(r)}", r.Author, r.RequestedByUser?.Username, r.RequestedByUser);

    public static ExternalNotification Failed(BookRequest r, string reason) =>
        new(NotificationKind.RequestFailed, $"Request failed: {r.Title}", $"{r.Title} by {r.Author} failed: {reason}{By(r)}", r.Author, r.RequestedByUser?.Username, r.RequestedByUser);

    public static ExternalNotification NeedsApproval(BookRequest r) =>
        new(NotificationKind.ApprovalNeeded, $"Approval needed: {r.Title}", $"{r.RequestedByUser?.Username ?? "Someone"} requested {r.Title} by {r.Author}.", r.Author, r.RequestedByUser?.Username, r.RequestedByUser);

    public static ExternalNotification Denied(BookRequest r, string? note) =>
        new(NotificationKind.RequestDenied, $"Request declined: {r.Title}", $"{r.Title} by {r.Author} was declined{(string.IsNullOrWhiteSpace(note) ? "." : $": {note}")}{By(r)}", r.Author, r.RequestedByUser?.Username, r.RequestedByUser);

    public static ExternalNotification NewRelease(string followName, AppUser user, IReadOnlyList<string> titles, bool autoRequested)
    {
        var shown = string.Join(", ", titles.Take(4)) + (titles.Count > 4 ? $" and {titles.Count - 4} more" : "");
        var headline = titles.Count == 1 ? $"New from {followName}: {titles[0]}" : $"{titles.Count} new from {followName}";
        var message = $"{shown}.{(autoRequested ? " Requested automatically." : "")} (followed by {user.Username})";
        return new ExternalNotification(NotificationKind.NewRelease, headline, message, followName, user.Username, user);
    }

    private static string By(BookRequest r) => r.RequestedByUser is null ? "" : $" (requested by {r.RequestedByUser.Username})";
}
