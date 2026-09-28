using Microsoft.Extensions.Logging;

namespace AudiobookRequest.Core.Library;

public class CoverArtDownloader(HttpClient httpClient, ILogger<CoverArtDownloader> logger) : ICoverArtDownloader
{
    public async Task<string?> DownloadAsync(string? coverUrl, string destinationDirectory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            return null;
        }

        try
        {
            using var response = await httpClient.GetAsync(coverUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cover art download from {Url} returned {Status}", coverUrl, response.StatusCode);
                return null;
            }

            var extension = response.Content.Headers.ContentType?.MediaType switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/webp" => ".webp",
                _ => Path.GetExtension(new Uri(coverUrl).AbsolutePath) is { Length: > 1 } ext ? ext : ".jpg"
            };

            var path = Path.Combine(destinationDirectory, "cover" + extension);
            await using var fileStream = File.Create(path);
            await response.Content.CopyToAsync(fileStream, ct);
            return path;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download cover art from {Url}", coverUrl);
            return null;
        }
    }
}
