using System.Text.Json;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Core.Recommendations;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public record FollowDto(int Id, string Kind, string Name, string? SeriesAsin, bool AutoRequest, DateTimeOffset CreatedAt, DateTimeOffset? LastCheckedAt);

public record FollowFindDto(int Id, int FollowId, string FollowName, BrowseItemDto Item, DateTimeOffset FoundAt);

public record CreateFollowRequest(string Kind, string Name, string? SeriesAsin, string? SeriesAuthor, bool AutoRequest);

/// <summary>
/// Follows an author or series and watches Audible for new books. New books are recorded as finds (shown
/// on For You), announced on the notification channels, and -- for follows with auto-request on --
/// requested automatically: every new book for an author, or the next book after the last one finished for a series.
/// </summary>
public class FollowService(
    AppDbContext db,
    SettingsService settingsService,
    IAudiobookMetadataClient metadataClient,
    CatalogBrowseService browse,
    RequestCreationService creation,
    IExternalNotifier externalNotifier,
    ILogger<FollowService> logger)
{
    // How many of an author's newest books a check looks at; new releases always sort to the front.
    private const int WindowSize = 50;
    private const int MaxAutoRequestsPerCheck = 5;
    private static readonly TimeSpan AutoRequestGiveUp = TimeSpan.FromDays(60);

    public async Task<IReadOnlyList<FollowDto>> ListAsync(int userId, CancellationToken ct)
    {
        var follows = await db.Follows.Where(f => f.AppUserId == userId).ToListAsync(ct);
        return follows.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();
    }

    /// <summary>Starts following. Idempotent: following something already followed returns the existing follow.</summary>
    public async Task<FollowDto?> CreateAsync(int userId, CreateFollowRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<FollowKind>(req.Kind, true, out var kind) || string.IsNullOrWhiteSpace(req.Name))
        {
            return null;
        }
        if (kind == FollowKind.Series && (string.IsNullOrWhiteSpace(req.SeriesAsin) || string.IsNullOrWhiteSpace(req.SeriesAuthor)))
        {
            return null;
        }

        var name = req.Name.Trim();
        var existing = await db.Follows.FirstOrDefaultAsync(f => f.AppUserId == userId && f.Kind == kind && f.Name == name, ct);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        var follow = new Follow
        {
            AppUserId = userId,
            Kind = kind,
            Name = name,
            SeriesAsin = kind == FollowKind.Series ? req.SeriesAsin : null,
            SeriesAuthor = kind == FollowKind.Series ? req.SeriesAuthor?.Trim() : null,
            AutoRequest = req.AutoRequest,
        };
        db.Follows.Add(follow);
        await db.SaveChangesAsync(ct);

        // Take the baseline now, so only books released after this moment ever count as new.
        await BaselineAsync(follow, ct);
        return ToDto(follow);
    }

    public async Task<FollowDto?> SetAutoRequestAsync(int userId, int id, bool autoRequest, CancellationToken ct)
    {
        var follow = await db.Follows.FirstOrDefaultAsync(f => f.Id == id && f.AppUserId == userId, ct);
        if (follow is null)
        {
            return null;
        }
        follow.AutoRequest = autoRequest;
        await db.SaveChangesAsync(ct);
        return ToDto(follow);
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct)
    {
        var follow = await db.Follows.FirstOrDefaultAsync(f => f.Id == id && f.AppUserId == userId, ct);
        if (follow is null)
        {
            return false;
        }
        db.Follows.Remove(follow);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>New books for this user's follows that they don't already own or have requested.</summary>
    public async Task<IReadOnlyList<FollowFindDto>> FindsAsync(int userId, CancellationToken ct)
    {
        var finds = await db.FollowFinds
            .Include(f => f.Follow)
            .Where(f => f.Follow!.AppUserId == userId && !f.Dismissed)
            .ToListAsync(ct);
        finds = finds.OrderByDescending(f => f.FoundAt).Take(60).ToList();

        var listings = finds.Select(f => new AudiobookListing(
            f.Asin, f.Title, f.Author, f.CoverUrl ?? "", f.RuntimeMinutes, null, null, null, null, [], null, f.ReleaseDate)).ToList();
        var annotated = await browse.AnnotateAsync(listings, ct);

        return finds
            .Select((f, i) => (Find: f, Item: annotated[i]))
            .Where(x => !x.Item.AlreadyInLibrary && !x.Item.AlreadyRequested)
            .Select(x => new FollowFindDto(x.Find.Id, x.Find.FollowId, x.Find.Follow!.Name, x.Item, x.Find.FoundAt))
            .ToList();
    }

    public async Task<bool> DismissFindAsync(int userId, int findId, CancellationToken ct)
    {
        var find = await db.FollowFinds.Include(f => f.Follow).FirstOrDefaultAsync(f => f.Id == findId && f.Follow!.AppUserId == userId, ct);
        if (find is null)
        {
            return false;
        }
        find.Dismissed = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task CheckAllAsync(CancellationToken ct)
    {
        var ids = await db.Follows.Select(f => f.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                await CheckOneAsync(id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Follow check failed for follow {FollowId}", id);
            }
        }
    }

    public async Task CheckOneAsync(int followId, CancellationToken ct)
    {
        var follow = await db.Follows.Include(f => f.AppUser).FirstOrDefaultAsync(f => f.Id == followId, ct);
        if (follow is null)
        {
            return;
        }

        if (follow.LastCheckedAt is null)
        {
            await BaselineAsync(follow, ct);
            return;
        }

        var window = await FetchWindowAsync(follow, ct);
        if (window.Count == 0)
        {
            return; // Audible hiccup: change nothing, try again next time.
        }

        var known = Known(follow);
        var fresh = window.Where(l => !known.Contains(l.Asin)).ToList();
        foreach (var listing in fresh)
        {
            db.FollowFinds.Add(new FollowFind
            {
                FollowId = follow.Id,
                Asin = listing.Asin,
                Title = listing.Title,
                Author = listing.Author,
                CoverUrl = listing.CoverUrl,
                RuntimeMinutes = listing.RuntimeMinutes,
                ReleaseDate = listing.ReleaseDate,
            });
            known.Add(listing.Asin);
        }
        follow.KnownAsinsJson = JsonSerializer.Serialize(known);
        follow.LastCheckedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var autoRequested = new List<string>();
        if (follow.AutoRequest)
        {
            autoRequested = await AutoRequestAsync(follow, window, ct);
        }

        if (fresh.Count > 0)
        {
            var settings = await settingsService.GetAsync(ct);
            await externalNotifier.NotifyAsync(settings,
                ExternalNotifier.NewRelease(follow.Name, follow.AppUser!, fresh.Select(l => l.Title).ToList(), autoRequested.Count > 0), ct);
        }
    }

    private async Task BaselineAsync(Follow follow, CancellationToken ct)
    {
        var window = await FetchWindowAsync(follow, ct);
        if (window.Count == 0)
        {
            return; // stays un-baselined; the next check tries again, and never floods with the back catalog
        }

        follow.KnownAsinsJson = JsonSerializer.Serialize(window.Select(l => l.Asin).ToList());
        follow.LastCheckedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<AudiobookListing>> FetchWindowAsync(Follow follow, CancellationToken ct)
    {
        if (follow.Kind == FollowKind.Author)
        {
            var page = await metadataClient.BrowseAsync(null, null, "-ReleaseDate", 0, WindowSize, follow.Name, ct);
            return page.Items
                .Where(i => (i.AllAuthors is { Count: > 0 } ? i.AllAuthors : [i.Author]).Any(a => TitleMatcher.AuthorsMatch(a, follow.Name)))
                .ToList();
        }

        // A series is found by its name plus author; keep only members carrying the followed series' id and a plain book number.
        var series = await metadataClient.BrowseAsync(null, follow.Name, "-ReleaseDate", 0, WindowSize, follow.SeriesAuthor, ct);
        return series.Items
            .Where(i => i.SeriesAsin == follow.SeriesAsin && AudiobookSeries.IsSingleBook(i.SeriesSequence))
            .ToList();
    }

    private async Task<List<string>> AutoRequestAsync(Follow follow, IReadOnlyList<AudiobookListing> window, CancellationToken ct)
    {
        var requested = new List<string>();
        var user = follow.AppUser ?? await db.Users.FirstAsync(u => u.Id == follow.AppUserId, ct);
        var isAdmin = user.Role == UserRole.Admin;

        var candidates = follow.Kind == FollowKind.Author
            ? await PendingAuthorFindsAsync(follow, ct)
            : await NextInSeriesAsync(follow, window, ct);

        foreach (var find in candidates.Take(MaxAutoRequestsPerCheck))
        {
            var match = await browse.ResolveAsync(find.Title, find.Author, ct);
            if (match is null)
            {
                continue; // not findable in Chaptarr yet (often an announcement); try again next check
            }

            var result = await creation.CreateAsync(user.Id, isAdmin, match.ForeignBookId, match.Title, match.AuthorName, match.SeriesTitle, match.CoverUrl, match.ReleaseDate, ct);
            if (result.Outcome == CreateOutcome.LimitReached)
            {
                break;
            }

            // Created, or already requested/owned: either way there's nothing more to do for this one.
            find.AutoRequestedAt = DateTimeOffset.UtcNow;
            if (result.Outcome == CreateOutcome.Created)
            {
                requested.Add(find.Title);
            }
        }

        await db.SaveChangesAsync(ct);
        return requested;
    }

    private async Task<List<FollowFind>> PendingAuthorFindsAsync(Follow follow, CancellationToken ct)
    {
        var finds = await db.FollowFinds
            .Where(f => f.FollowId == follow.Id && f.AutoRequestedAt == null && !f.Dismissed)
            .ToListAsync(ct);
        var cutoff = DateTimeOffset.UtcNow - AutoRequestGiveUp;
        return finds.Where(f => f.FoundAt >= cutoff).OrderBy(f => f.FoundAt).ToList();
    }

    // The one book after the highest-numbered member the user has finished, if it isn't already owned or requested.
    private async Task<List<FollowFind>> NextInSeriesAsync(Follow follow, IReadOnlyList<AudiobookListing> window, CancellationToken ct)
    {
        var members = window
            .Select(l => (Listing: l, Number: AudiobookSeries.ParseSingleBookNumber(l.SeriesSequence) ?? 0))
            .Where(m => m.Number > 0)
            .GroupBy(m => m.Number)
            .Select(g => g.First())
            .OrderBy(m => m.Number)
            .ToList();

        var played = await db.ListeningHistory
            .Where(h => h.AppUserId == follow.AppUserId && h.PlayCount > 0)
            .Select(h => h.Title)
            .ToListAsync(ct);

        var finished = members.Where(m => played.Any(t => TitleMatcher.IsSimilarTitle(t, m.Listing.Title))).Select(m => m.Number).ToList();
        if (finished.Count == 0)
        {
            return [];
        }

        var next = members.FirstOrDefault(m => m.Number == finished.Max() + 1);
        if (next.Listing is null)
        {
            return [];
        }

        var flagged = (await browse.AnnotateAsync([next.Listing], ct))[0];
        if (flagged.AlreadyInLibrary || flagged.AlreadyRequested)
        {
            return [];
        }

        var find = await db.FollowFinds.FirstOrDefaultAsync(f => f.FollowId == follow.Id && f.Asin == next.Listing.Asin, ct);
        if (find is null)
        {
            find = new FollowFind
            {
                FollowId = follow.Id,
                Asin = next.Listing.Asin,
                Title = next.Listing.Title,
                Author = next.Listing.Author,
                CoverUrl = next.Listing.CoverUrl,
                RuntimeMinutes = next.Listing.RuntimeMinutes,
                ReleaseDate = next.Listing.ReleaseDate,
            };
            db.FollowFinds.Add(find);
            await db.SaveChangesAsync(ct);
        }
        return find.AutoRequestedAt is null ? [find] : [];
    }

    private static HashSet<string> Known(Follow follow) =>
        (JsonSerializer.Deserialize<List<string>>(follow.KnownAsinsJson) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static FollowDto ToDto(Follow f) =>
        new(f.Id, f.Kind.ToString(), f.Name, f.SeriesAsin, f.AutoRequest, f.CreatedAt, f.LastCheckedAt);
}
