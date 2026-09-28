namespace AudiobookRequest.Core.Library;

public interface ICoverArtDownloader
{
    /// <summary>Downloads the cover image to a file in destinationDirectory, returning its path, or null on failure/no URL.</summary>
    Task<string?> DownloadAsync(string? coverUrl, string destinationDirectory, CancellationToken ct = default);
}
