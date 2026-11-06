namespace AudiobookRequest.Core.Entities;

// Explicit values: the enum is persisted as a plain INTEGER column (no HasConversion in
// AppDbContext), so inserting a new member positionally would silently renumber every status
// after it for rows already in a live database. New members must be appended with their own
// explicit value, never inserted between existing ones.
public enum BookRequestStatus
{
    Requested = 0,
    Searching = 1,
    Downloading = 2,
    Processing = 3,
    Ready = 4,
    Failed = 5,
    Snatched = 6,
    /// <summary>Waiting on an admin to approve (only when AppSettings.RequireApproval is on).</summary>
    PendingApproval = 7,
    /// <summary>An admin declined it; FailureReason carries their note.</summary>
    Denied = 8,
    /// <summary>The book isn't out yet: held in its own queue (checked on a slow schedule) until its release date, then handed to the normal flow.</summary>
    Upcoming = 9
}

public class BookRequest
{
    public int Id { get; set; }
    public int RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }

    public required string Title { get; set; }
    public required string Author { get; set; }
    public string? Series { get; set; }
    public string? CoverUrl { get; set; }

    /// <summary>The book id as returned by Chaptarr's lookup endpoint (foreignBookId).</summary>
    public required string ChaptarrForeignBookId { get; set; }
    /// <summary>Chaptarr's internal numeric book id, populated once the author/book has been added.</summary>
    public int? ChaptarrBookId { get; set; }

    public BookRequestStatus Status { get; set; } = BookRequestStatus.Requested;
    public string? FailureReason { get; set; }

    /// <summary>Latest "what Chaptarr says is wrong" text (blocked import, failed download, ...) while the request is stuck; cleared once it moves on.</summary>
    public string? DiagnosticMessage { get; set; }

    /// <summary>Keep re-searching on a schedule instead of timing out into Failed when nothing is found.</summary>
    public bool Wishlisted { get; set; }
    public DateTimeOffset? LastSearchAt { get; set; }

    /// <summary>When the book comes out, if known (midnight UTC of that day). While it is in the future the request waits as Upcoming.</summary>
    public DateTimeOffset? ReleaseDate { get; set; }

    /// <summary>When the app last tried to import this request's blocked download on its own; it only ever tries once per attempt.</summary>
    public DateTimeOffset? AutoFixAttemptedAt { get; set; }

    /// <summary>When Chaptarr's history first showed a "grabbed" event for this book.</summary>
    public DateTimeOffset? SnatchedAt { get; set; }
    /// <summary>0-1 fraction downloaded, from the Chaptarr queue item's size/sizeleft. Null until snatched.</summary>
    public double? DownloadProgressPercent { get; set; }
    public double? DownloadSizeBytes { get; set; }
    public double? DownloadSizeLeftBytes { get; set; }
    public string? DownloadClientName { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadyAt { get; set; }

    public ProcessedBook? ProcessedBook { get; set; }
}
