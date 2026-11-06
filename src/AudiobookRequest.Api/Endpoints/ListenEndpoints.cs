using System.Security.Claims;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;

namespace AudiobookRequest.Api.Endpoints;

/// <summary>
/// The in-app audiobook player's API. Audio and covers are streamed through here (as the signed-in person)
/// so the browser never needs a Jellyfin login or a direct route to Jellyfin.
/// </summary>
public static class ListenEndpoints
{
    private const string ReSignIn = "Sign out and back in once to turn on the in-app player.";

    public static IEndpointRouteBuilder MapListenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/listen").RequireAuthorization();

        group.MapGet("/library", (
            ClaimsPrincipal principal, ListenService listen, PersonalBookService personal, LibrarySeriesService series,
            string? filter, string? search, string? tag, int? start, int? limit, CancellationToken ct) =>
            Run(principal, async session =>
            {
                var uid = UserId(principal);
                var from = Math.Max(0, start ?? 0);
                var take = Math.Clamp(limit ?? 60, 1, 100);
                var known = await series.GetIndexAsync(ct);

                // "Rated" and a chosen tag are the person's own lists: the app knows which books, Jellyfin supplies the details.
                var own = !string.IsNullOrWhiteSpace(tag) ? "tag" : filter?.ToLowerInvariant() == "rated" ? "rated" : null;
                if (own is not null)
                {
                    var (ids, total) = own == "tag"
                        ? await personal.ItemIdsWithTagAsync(uid, tag, from, take, ct)
                        : await personal.ItemIdsRatedAsync(uid, from, take, ct);
                    var books = await listen.GetItemsByIdsAsync(session, ids, ct);
                    return Results.Ok(new ListenLibraryDto(WithMetadata(await WithRatingsAsync(personal, uid, books, ct), known), total));
                }

                var f = filter?.ToLowerInvariant() switch
                {
                    "resume" => ListenFilter.Resume,
                    "recent" => ListenFilter.Recent,
                    "finished" => ListenFilter.Finished,
                    _ => ListenFilter.All,
                };
                var lib = await listen.GetLibraryAsync(session, f, search, from, take, ct);
                return Results.Ok(lib with { Items = WithMetadata(await WithRatingsAsync(personal, uid, lib.Items, ct), known) });
            }));

        // The library sorted into piles by author or by series, and the books of one pile.
        group.MapGet("/groups", (ClaimsPrincipal principal, ListenService listen, LibrarySeriesService series, string? by, CancellationToken ct) =>
            Run(principal, async session =>
                Grouping(by) is { } grouping
                    ? Results.Ok(await listen.GetGroupsAsync(session, grouping, await series.GetIndexAsync(ct), ct))
                    : Results.BadRequest("by must be author or series.")));

        group.MapGet("/groups/books", (
            ClaimsPrincipal principal, ListenService listen, LibrarySeriesService series, PersonalBookService personal, string? by, string? key, CancellationToken ct) =>
            Run(principal, async session =>
            {
                if (Grouping(by) is not { } grouping || string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest("by must be author or series, and key is required.");
                }
                var books = await listen.GetGroupBooksAsync(session, grouping, key, await series.GetIndexAsync(ct), ct);
                return books is null
                    ? Results.NotFound()
                    : Results.Ok(new ListenLibraryDto(await WithRatingsAsync(personal, UserId(principal), books, ct), books.Count));
            }));

        group.MapGet("/items/{id}", (ClaimsPrincipal principal, ListenService listen, LibrarySeriesService series, string id, CancellationToken ct) =>
            Run(principal, async session =>
            {
                if (!ListenService.IsValidItemId(id))
                {
                    return Results.NotFound();
                }
                var detail = await listen.GetDetailAsync(session, id, ct);
                if (detail is null)
                {
                    return Results.NotFound();
                }
                // An admin's corrections show here too, so the player and the book's page agree with the Library.
                return Results.Ok(ListenService.WithMetadata(detail, await series.GetIndexAsync(ct)));
            }));

        // Correcting what the Library shows for a book (admins only; the correction is for everyone).
        group.MapGet("/items/{id}/metadata", (ClaimsPrincipal principal, LibrarySeriesService series, string id, CancellationToken ct) =>
            Personal(principal, id, async _ => Results.Ok(await series.GetEditAsync(id, ct)))).RequireAuthorization("AdminOnly");

        group.MapPut("/items/{id}/metadata", (ClaimsPrincipal principal, LibrarySeriesService series, string id, BookOverrideDto body, CancellationToken ct) =>
            Personal(principal, id, async _ =>
            {
                await series.SetOverrideAsync(id, body, ct);
                return Results.Ok(await series.GetEditAsync(id, ct));
            })).RequireAuthorization("AdminOnly");

        group.MapDelete("/items/{id}/metadata", (ClaimsPrincipal principal, LibrarySeriesService series, string id, CancellationToken ct) =>
            Personal(principal, id, async _ =>
            {
                await series.SetOverrideAsync(id, new BookOverrideDto(null, null, null, false), ct);
                return Results.NoContent();
            })).RequireAuthorization("AdminOnly");

        group.MapGet("/items/{id}/stream", async (
            HttpContext http, ClaimsPrincipal principal, ListenService listen, string id, CancellationToken ct) =>
        {
            var session = SessionClaims.JellyfinSessionOf(principal);
            if (session is null)
            {
                return Results.Problem(ReSignIn, statusCode: 401);
            }
            if (!ListenService.IsValidItemId(id))
            {
                return Results.NotFound();
            }

            try
            {
                var range = http.Request.Headers.Range.ToString();
                var (upstream, container) = await listen.OpenStreamAsync(session, id, string.IsNullOrWhiteSpace(range) ? null : range, ct);
                using (upstream)
                {
                    var status = (int)upstream.StatusCode;
                    if (status is not (200 or 206 or 416))
                    {
                        return Results.Problem("Jellyfin couldn't provide the audio.", statusCode: 502);
                    }

                    http.Response.StatusCode = status;
                    http.Response.Headers.AcceptRanges = "bytes";
                    var contentType = ListenService.ContentTypeFor(container, upstream.Content.Headers.ContentType?.ToString());
                    if (contentType is not null)
                    {
                        http.Response.ContentType = contentType;
                    }
                    if (upstream.Content.Headers.ContentLength is { } length)
                    {
                        http.Response.ContentLength = length;
                    }
                    if (upstream.Content.Headers.ContentRange is { } contentRange)
                    {
                        http.Response.Headers.ContentRange = contentRange.ToString();
                    }
                    // Audio is per-user and large; let the browser's media cache handle it, not shared caches.
                    http.Response.Headers.CacheControl = "private, no-transform";

                    await upstream.Content.CopyToAsync(http.Response.Body, ct);
                }
                return Results.Empty;
            }
            catch (JellyfinUnauthorizedException)
            {
                return Results.Problem("Jellyfin no longer accepts your sign-in. Sign out and back in.", statusCode: 401);
            }
            catch (ListenException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
            catch (OperationCanceledException)
            {
                // The browser closed the connection (seek, pause, navigation): normal.
                return Results.Empty;
            }
        });

        group.MapGet("/items/{id}/next", (ClaimsPrincipal principal, ListenService listen, string id, CancellationToken ct) =>
            Run(principal, async session =>
            {
                if (!ListenService.IsValidItemId(id))
                {
                    return Results.NotFound();
                }
                var next = await listen.GetNextInSeriesAsync(session, id, ct);
                return next is null ? Results.NoContent() : Results.Ok(next);
            }));

        // Books browsers can't play as-is are converted to AAC by Jellyfin and delivered as HLS: a playlist, then
        // short segments. Everything is fetched through here so the browser never needs Jellyfin's address or token.
        group.MapGet("/items/{id}/hls/{**rest}", async (
            HttpContext http, ClaimsPrincipal principal, ListenService listen, string id, string rest, CancellationToken ct) =>
        {
            var session = SessionClaims.JellyfinSessionOf(principal);
            if (session is null)
            {
                return Results.Problem(ReSignIn, statusCode: 401);
            }
            if (!ListenService.IsValidItemId(id))
            {
                return Results.NotFound();
            }

            try
            {
                var isPlaylist = rest == "main.m3u8";
                using var upstream = isPlaylist
                    ? await listen.OpenHlsPlaylistAsync(session, id, ct)
                    : await listen.OpenHlsSegmentAsync(session, id, rest, http.Request.QueryString.Value ?? "", ct);

                http.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? (isPlaylist ? "application/vnd.apple.mpegurl" : "video/mp2t");
                http.Response.Headers.CacheControl = isPlaylist ? "private, no-store" : "private, max-age=600";
                if (upstream.Content.Headers.ContentLength is { } length)
                {
                    http.Response.ContentLength = length;
                }
                await upstream.Content.CopyToAsync(http.Response.Body, ct);
                return Results.Empty;
            }
            catch (JellyfinUnauthorizedException)
            {
                return Results.Problem("Jellyfin no longer accepts your sign-in. Sign out and back in.", statusCode: 401);
            }
            catch (ListenException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
            catch (OperationCanceledException)
            {
                return Results.Empty;
            }
        });

        group.MapDelete("/items/{id}/hls", (ClaimsPrincipal principal, ListenService listen, string id, CancellationToken ct) =>
            Run(principal, async session =>
            {
                if (ListenService.IsValidItemId(id))
                {
                    await listen.StopHlsAsync(session, id, ct);
                }
                return Results.NoContent();
            }));

        group.MapGet("/stats", (
            ClaimsPrincipal principal, ListenService listen, ListeningStatsService stats, string? today, CancellationToken ct) =>
            Run(principal, async session =>
            {
                var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
                var day = ListeningStatsService.ResolveDay(today, utcToday);
                var (finishedYear, finishedTotal) = await listen.CountFinishedAsync(session, day.Year, ct);
                return Results.Ok(await stats.GetAsync(UserId(principal), day, finishedYear, finishedTotal, ct));
            }));

        // ---- the signed-in person's own rating, tags and bookmarks ----

        group.MapGet("/items/{id}/mine", (ClaimsPrincipal principal, PersonalBookService personal, string id, CancellationToken ct) =>
            Personal(principal, id, async uid => Results.Ok(await personal.GetAsync(uid, id, ct))));

        group.MapPut("/items/{id}/rating", (ClaimsPrincipal principal, PersonalBookService personal, string id, SetRatingRequest req, CancellationToken ct) =>
            Personal(principal, id, async uid => Results.Ok(new { rating = await personal.SetRatingAsync(uid, id, req.Stars, ct) })));

        group.MapPost("/items/{id}/tags", (ClaimsPrincipal principal, PersonalBookService personal, string id, AddTagRequest req, CancellationToken ct) =>
            Personal(principal, id, async uid => Results.Ok(new { tag = await personal.AddTagAsync(uid, id, req.Tag, ct) })));

        // The tag travels as a query value, not in the path: tags may contain "/" and other characters paths handle badly.
        group.MapDelete("/items/{id}/tags", (ClaimsPrincipal principal, PersonalBookService personal, string id, string tag, CancellationToken ct) =>
            Personal(principal, id, async uid =>
            {
                await personal.RemoveTagAsync(uid, id, tag, ct);
                return Results.NoContent();
            }));

        group.MapGet("/tags", (ClaimsPrincipal principal, PersonalBookService personal, CancellationToken ct) =>
            Personal(principal, null, async uid => Results.Ok(await personal.ListTagsAsync(uid, ct))));

        group.MapPost("/items/{id}/bookmarks", (ClaimsPrincipal principal, PersonalBookService personal, string id, AddBookmarkRequest req, CancellationToken ct) =>
            Personal(principal, id, async uid => Results.Ok(await personal.AddBookmarkAsync(uid, id, req, ct))));

        group.MapPut("/bookmarks/{bookmarkId:int}", (ClaimsPrincipal principal, PersonalBookService personal, int bookmarkId, UpdateBookmarkRequest req, CancellationToken ct) =>
            Personal(principal, null, async uid => Results.Ok(await personal.UpdateBookmarkAsync(uid, bookmarkId, req.Note, ct))));

        group.MapDelete("/bookmarks/{bookmarkId:int}", (ClaimsPrincipal principal, PersonalBookService personal, int bookmarkId, CancellationToken ct) =>
            Personal(principal, null, async uid =>
            {
                await personal.DeleteBookmarkAsync(uid, bookmarkId, ct);
                return Results.NoContent();
            }));

        group.MapGet("/items/{id}/image", async (
            HttpContext http, ClaimsPrincipal principal, ListenService listen, string id, int? w, CancellationToken ct) =>
        {
            var session = SessionClaims.JellyfinSessionOf(principal);
            if (session is null)
            {
                return Results.Problem(ReSignIn, statusCode: 401);
            }
            if (!ListenService.IsValidItemId(id))
            {
                return Results.NotFound();
            }

            try
            {
                using var upstream = await listen.OpenImageAsync(session, id, w ?? 400, ct);
                if (!upstream.IsSuccessStatusCode)
                {
                    return Results.NotFound();
                }

                http.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
                http.Response.Headers.CacheControl = "private, max-age=86400";
                await upstream.Content.CopyToAsync(http.Response.Body, ct);
                return Results.Empty;
            }
            catch (JellyfinUnauthorizedException)
            {
                return Results.Problem("Jellyfin no longer accepts your sign-in. Sign out and back in.", statusCode: 401);
            }
            catch (ListenException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
        });

        group.MapPost("/items/{id}/progress", (
            ClaimsPrincipal principal, ListenService listen, ListeningStatsService stats, ILogger<ListenService> logger,
            string id, ProgressRequest req, string? mediaSourceId, CancellationToken ct) =>
            Run(principal, async session =>
            {
                if (!ListenService.IsValidItemId(id))
                {
                    return Results.NotFound();
                }
                await listen.ReportProgressAsync(session, id, mediaSourceId, req, ct);

                // Counted only once Jellyfin has the report: the player re-sends anything that didn't get through,
                // so recording before could count the same minutes twice.
                try
                {
                    await stats.RecordAsync(UserId(principal), id, req, DateOnly.FromDateTime(DateTime.UtcNow), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Couldn't record listening time for {ItemId}", id);
                }
                return Results.NoContent();
            }));

        group.MapPost("/items/{id}/played", (ClaimsPrincipal principal, ListenService listen, string id, CancellationToken ct) =>
            Run(principal, async session =>
            {
                if (!ListenService.IsValidItemId(id))
                {
                    return Results.NotFound();
                }
                await listen.SetPlayedAsync(session, id, true, ct);
                return Results.NoContent();
            }));

        group.MapDelete("/items/{id}/played", (ClaimsPrincipal principal, ListenService listen, string id, CancellationToken ct) =>
            Run(principal, async session =>
            {
                if (!ListenService.IsValidItemId(id))
                {
                    return Results.NotFound();
                }
                await listen.SetPlayedAsync(session, id, false, ct);
                return Results.NoContent();
            }));

        return app;
    }

    private static async Task<IReadOnlyList<ListenItemDto>> WithRatingsAsync(
        PersonalBookService personal, int userId, IReadOnlyList<ListenItemDto> items, CancellationToken ct)
    {
        var ratings = await personal.RatingsAsync(userId, items.Select(i => i.Id), ct);
        return items.Select(i => ratings.TryGetValue(i.Id, out var stars) ? i with { MyRating = stars } : i).ToList();
    }

    // The person's own data never touches Jellyfin, so (unlike Run) this needs no Jellyfin session -- only a valid book id.
    private static async Task<IResult> Personal(ClaimsPrincipal principal, string? itemId, Func<int, Task<IResult>> action)
    {
        if (itemId is not null && !ListenService.IsValidItemId(itemId))
        {
            return Results.NotFound();
        }

        try
        {
            return await action(UserId(principal));
        }
        catch (ListenException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    private static IReadOnlyList<ListenItemDto> WithMetadata(IReadOnlyList<ListenItemDto> items, BookMetadataIndex known) =>
        items.Select(i => ListenService.WithMetadata(i, known)).ToList();

    private static ListenGrouping? Grouping(string? by) => by?.ToLowerInvariant() switch
    {
        "author" => ListenGrouping.Author,
        "series" => ListenGrouping.Series,
        _ => null,
    };

    private static int UserId(ClaimsPrincipal principal) => int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Shared envelope: needs the person's Jellyfin session; turns the expected failures into plain responses.
    private static async Task<IResult> Run(ClaimsPrincipal principal, Func<JellyfinSession, Task<IResult>> action)
    {
        var session = SessionClaims.JellyfinSessionOf(principal);
        if (session is null)
        {
            return Results.Problem(ReSignIn, statusCode: 401);
        }

        try
        {
            return await action(session);
        }
        catch (JellyfinUnauthorizedException)
        {
            return Results.Problem("Jellyfin no longer accepts your sign-in. Sign out and back in.", statusCode: 401);
        }
        catch (ListenException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("Couldn't reach Jellyfin.", statusCode: 502);
        }
    }
}
