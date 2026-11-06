using AudiobookRequest.Core.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Caches Audible/Audnexus detail lookups (listening time, narrators...) so the detail page and the
/// narrator/length search filters don't repeat two external calls per book. Misses are cached briefly so
/// an unresolvable title isn't retried on every keystroke.
/// </summary>
public class MetadataLookupService(IAudiobookMetadataClient client, IMemoryCache cache)
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(10);

    public async Task<AudiobookMetadata?> GetAsync(string title, string author, CancellationToken ct)
    {
        var key = $"meta|{title.Trim().ToLowerInvariant()}|{author.Trim().ToLowerInvariant()}";
        if (cache.TryGetValue(key, out AudiobookMetadata? cached))
        {
            return cached;
        }

        var metadata = await client.TryResolveDetailAsync(title, author, ct);
        cache.Set(key, metadata, metadata is null ? MissTtl : HitTtl);
        return metadata;
    }

    /// <summary>Resolves many books with bounded parallelism, preserving input order.</summary>
    public async Task<IReadOnlyList<AudiobookMetadata?>> GetManyAsync(IReadOnlyList<(string Title, string Author)> books, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(4);
        var tasks = books.Select(async b =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await GetAsync(b.Title, b.Author, ct);
            }
            finally
            {
                gate.Release();
            }
        });
        return await Task.WhenAll(tasks);
    }
}
