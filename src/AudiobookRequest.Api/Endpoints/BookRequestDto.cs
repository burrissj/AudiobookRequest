using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Endpoints;

public record BookRequestDto(
    int Id,
    string ReadarrForeignBookId,
    string Title,
    string Author,
    string? Series,
    string? CoverUrl,
    string Status,
    string? FailureReason,
    string RequestedByUsername,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReadyAt)
{
    public static BookRequestDto From(BookRequest r) => new(
        r.Id, r.ReadarrForeignBookId, r.Title, r.Author, r.Series, r.CoverUrl, r.Status.ToString(), r.FailureReason,
        r.RequestedByUser?.Username ?? "", r.RequestedAt, r.ReadyAt);
}
