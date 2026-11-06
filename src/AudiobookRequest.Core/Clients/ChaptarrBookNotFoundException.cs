namespace AudiobookRequest.Core.Clients;

/// <summary>Chaptarr's exact lookup for a book's foreign id came back empty: the listing is gone from its catalog (or never existed there).</summary>
public class ChaptarrBookNotFoundException(string foreignBookId, string? title = null)
    : InvalidOperationException(title is null
        ? $"Chaptarr no longer recognizes foreign book id '{foreignBookId}'."
        : $"Chaptarr no longer lists this listing of \"{title}\" (id '{foreignBookId}'), and a search by title and author found no match.")
{
    public string ForeignBookId { get; } = foreignBookId;
}
