using AudiobookRequest.Api.Services;

namespace AudiobookRequest.Api.Endpoints;

public record SetImportFolderRequest(string? Path);

/// <summary>Admin-only: the tool for tidying loose audiobooks into the library, one book at a time.</summary>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/import").RequireAuthorization("AdminOnly");

        group.MapGet("/", (ImportService import, CancellationToken ct) => Run(async () => Results.Ok(await import.GetFolderAsync(ct))));

        group.MapPut("/folder", (SetImportFolderRequest req, ImportService import, CancellationToken ct) =>
            Run(async () => Results.Ok(await import.SetFolderAsync(req.Path, ct))));

        group.MapGet("/scan", (ImportService import, CancellationToken ct) => Run(async () => Results.Ok(await import.ScanAsync(ct))));

        group.MapPost("/candidates/{id}/identify", (string id, ImportIdentifyRequest req, ImportService import, CancellationToken ct) =>
            Run(async () => Results.Ok(await import.IdentifyAsync(id, req, ct))));

        group.MapGet("/preview", (string title, string author, string? series, ImportService import, CancellationToken ct) =>
            Run(async () => Results.Ok(await import.PreviewAsync(title, author, series, ct))));

        group.MapPost("/candidates/{id}/import", (string id, ImportRequest req, ImportService import, ImportJobService jobs, CancellationToken ct) =>
            Run(async () =>
            {
                // Check the book is still there (and the folder is usable) before starting, so a bad request fails here, not in the background.
                var scan = await import.ScanAsync(ct);
                var candidate = scan.Candidates.FirstOrDefault(c => c.Id == id);
                if (candidate is null)
                {
                    return Results.Problem("That book isn't in the folder any more. Scan again.", statusCode: 404);
                }

                var job = jobs.Enqueue(id, candidate.Name, req);
                return job is null
                    ? Results.Problem("That book is already in the queue.", statusCode: 409)
                    : Results.Accepted("/api/admin/import/jobs", job);
            }));

        // The queue: waiting, converting and recently finished imports, in the order they were queued.
        group.MapGet("/jobs", (ImportJobService jobs) => Results.Ok(jobs.Jobs));

        group.MapDelete("/jobs/{jobId}", (string jobId, ImportJobService jobs) =>
            jobs.Cancel(jobId) ? Results.NoContent() : Results.Problem("That import isn't waiting or running any more.", statusCode: 409));

        group.MapDelete("/jobs", (ImportJobService jobs) => Results.Ok(new { cleared = jobs.ClearFinished() }));

        return app;
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ImportException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }
        catch (DirectoryNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: 400);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem($"This server isn't allowed to read that folder: {ex.Message}", statusCode: 403);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem($"Couldn't reach the book catalog: {ex.Message}", statusCode: 502);
        }
    }
}
