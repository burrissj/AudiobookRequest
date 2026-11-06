using Microsoft.Extensions.DependencyInjection;

namespace AudiobookRequest.Api.Services;

/// <param name="Status">"Queued", "Running", "Succeeded", "Failed" or "Cancelled".</param>
public record ImportJobState(
    string JobId,
    string CandidateId,
    string Name,
    string Status,
    string Stage,
    string? Error,
    ImportResultDto? Result,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// A queue of imports, worked through in the background so converting a long book doesn't tie up (or time out) a web
/// request, and so the admin can line up a whole folder and carry on identifying books while earlier ones convert.
/// One book converts at a time, in the order queued: each is a heavy ffmpeg job and two at once mostly slow each other
/// down (and fight over the disk). State is kept in memory: a restart forgets the queue and finished jobs, and a book
/// that was queued but not yet converted is simply offered again by the next scan.
/// </summary>
public class ImportJobService(IServiceScopeFactory scopes, ILogger<ImportJobService> logger)
{
    /// <summary>How many finished jobs are remembered (so their result or error can still be read).</summary>
    private const int KeepFinished = 50;

    private sealed class Entry(ImportJobState state, ImportRequest request)
    {
        public ImportJobState State { get; set; } = state;
        public ImportRequest Request { get; } = request;
        public CancellationTokenSource? Cancel { get; set; }
    }

    private readonly object gate = new();
    private readonly List<Entry> entries = [];
    private bool working;

    /// <summary>Every job in the order it was queued, finished ones included.</summary>
    public IReadOnlyList<ImportJobState> Jobs
    {
        get
        {
            lock (gate)
            {
                return entries.Select(e => e.State).ToList();
            }
        }
    }

    /// <summary>The job converting right now, if any.</summary>
    public ImportJobState? Current
    {
        get
        {
            lock (gate)
            {
                return entries.Select(e => e.State).FirstOrDefault(s => s.Status == "Running");
            }
        }
    }

    /// <returns>The queued job, or null when that book is already queued or being converted.</returns>
    public ImportJobState? Enqueue(string candidateId, string name, ImportRequest request)
    {
        ImportJobState job;
        bool startWorker = false;
        lock (gate)
        {
            if (entries.Any(e => e.State.CandidateId == candidateId && e.State.Status is "Queued" or "Running"))
            {
                return null;
            }

            job = new ImportJobState(Guid.NewGuid().ToString("N"), candidateId, name, "Queued", "Waiting in line", null, null, DateTimeOffset.UtcNow, null, null);
            entries.Add(new Entry(job, request));
            if (!working)
            {
                working = startWorker = true;
            }
        }

        if (startWorker)
        {
            _ = Task.Run(WorkAsync);
        }
        return job;
    }

    /// <summary>Takes a waiting job off the queue, or stops the one converting.</summary>
    /// <returns>false when there is no such job, or it has already finished.</returns>
    public bool Cancel(string jobId)
    {
        lock (gate)
        {
            var entry = entries.FirstOrDefault(e => e.State.JobId == jobId);
            switch (entry?.State.Status)
            {
                case "Queued":
                    entry.State = entry.State with
                    {
                        Status = "Cancelled",
                        Stage = "Cancelled",
                        Error = "Taken off the queue. Nothing was added to the library.",
                        FinishedAt = DateTimeOffset.UtcNow,
                    };
                    Trim();
                    return true;
                case "Running":
                    entry.Cancel?.Cancel();
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Forgets the finished jobs (done, failed or cancelled); waiting and running ones stay.</summary>
    /// <returns>How many were forgotten.</returns>
    public int ClearFinished()
    {
        lock (gate)
        {
            return entries.RemoveAll(e => e.State.Status is not ("Queued" or "Running"));
        }
    }

    private async Task WorkAsync()
    {
        while (true)
        {
            Entry entry;
            CancellationTokenSource source;
            lock (gate)
            {
                var next = entries.FirstOrDefault(e => e.State.Status == "Queued");
                if (next is null)
                {
                    working = false;
                    return;
                }

                entry = next;
                source = entry.Cancel = new CancellationTokenSource();
                entry.State = entry.State with { Status = "Running", Stage = "Starting", StartedAt = DateTimeOffset.UtcNow };
            }

            await RunAsync(entry, source);
        }
    }

    private async Task RunAsync(Entry entry, CancellationTokenSource source)
    {
        var job = entry.State;
        try
        {
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ImportService>();
            var result = await service.ImportAsync(job.CandidateId, entry.Request, stage => Update(entry, s => s with { Stage = stage }), source.Token);
            Update(entry, s => s with { Status = "Succeeded", Stage = "Done", Result = result, FinishedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex) when (ex is OperationCanceledException || source.IsCancellationRequested)
        {
            // Stopped by the admin: whatever error the interrupted work threw on its way out is not a real failure.
            Update(entry, s => s with { Status = "Cancelled", Stage = "Cancelled", Error = "Cancelled. Nothing was added to the library.", FinishedAt = DateTimeOffset.UtcNow });
        }
        catch (ImportException ex)
        {
            Update(entry, s => s with { Status = "Failed", Stage = "Failed", Error = ex.Message, FinishedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Importing '{Name}' failed", job.Name);
            Update(entry, s => s with { Status = "Failed", Stage = "Failed", Error = ex.Message, FinishedAt = DateTimeOffset.UtcNow });
        }
        finally
        {
            lock (gate)
            {
                entry.Cancel = null;
                Trim();
            }
            source.Dispose();
        }
    }

    private void Update(Entry entry, Func<ImportJobState, ImportJobState> change)
    {
        lock (gate)
        {
            entry.State = change(entry.State);
        }
    }

    // Caller holds the gate. Keeps the most recent finished jobs and forgets the older ones.
    private void Trim()
    {
        var finished = entries.Where(e => e.State.Status is not ("Queued" or "Running")).ToList();
        foreach (var old in finished.Take(Math.Max(0, finished.Count - KeepFinished)))
        {
            entries.Remove(old);
        }
    }
}
