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

    public ICollection<BookRequest> Requests { get; set; } = [];
}
