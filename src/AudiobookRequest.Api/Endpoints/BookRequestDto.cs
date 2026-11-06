using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Endpoints;

public record BookRequestDto(
    int Id,
    string ChaptarrForeignBookId,
    string Title,
    string Author,
    string? Series,
    string? CoverUrl,
    string Status,
    string? FailureReason,
    double? DownloadProgressPercent,
    double? DownloadSizeBytes,
    double? DownloadSizeLeftBytes,
    string? DownloadClientName,
    string RequestedByUsername,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SnatchedAt,
    DateTimeOffset? ReadyAt,
    string? Diagnostic = null,
    bool Wishlisted = false,
    DateTimeOffset? ReleaseDate = null,
    DateTimeOffset? NextSearchAt = null)
{
    public static BookRequestDto From(BookRequest r) => new(
        r.Id, r.ChaptarrForeignBookId, r.Title, r.Author, r.Series, r.CoverUrl, r.Status.ToString(), r.FailureReason,
        r.DownloadProgressPercent, r.DownloadSizeBytes, r.DownloadSizeLeftBytes, r.DownloadClientName,
        r.RequestedByUser?.Username ?? "", r.RequestedAt, r.SnatchedAt, r.ReadyAt, r.DiagnosticMessage, r.Wishlisted,
        r.ReleaseDate, NextSearchFor(r));

    // Only an upcoming request has a "next check": the first is immediate, then the wait that is right for how close the release is.
    private static DateTimeOffset? NextSearchFor(BookRequest r) =>
        r.Status == BookRequestStatus.Upcoming && r.ReleaseDate is { } release
            ? Core.Library.ReleaseSchedule.NextUpcomingCheck(r.LastSearchAt, r.RequestedAt, release, DateTimeOffset.UtcNow)
            : null;
}
