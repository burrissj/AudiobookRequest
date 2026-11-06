namespace AudiobookRequest.Core.Entities;

public enum UserRole
{
    User,
    Admin
}

public class AppUser
{
    public int Id { get; set; }
    public required string JellyfinUserId { get; set; }
    public required string Username { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Personal notification channels, chosen by the user themselves. They sit alongside the household
    // channels an admin sets up and only ever carry this user's own events. The ntfy topic is appended to
    // the ntfy server the admin configured, and email and Telegram use the admin's SMTP server / bot.
    public string? NotifyNtfyTopic { get; set; }
    public string? NotifyTelegramChatId { get; set; }
    public string? NotifyEmail { get; set; }
    public bool NotifyOnReady { get; set; } = true;
    public bool NotifyOnFailed { get; set; } = true;
    public bool NotifyOnNewRelease { get; set; } = true;

    public ICollection<BookRequest> Requests { get; set; } = [];
}
