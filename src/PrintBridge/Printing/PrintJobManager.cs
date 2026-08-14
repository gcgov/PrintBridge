using System.Collections.Concurrent;

namespace PrintBridge.Printing;

/// <summary>Prints one job; injected so the manager is testable without a printer.</summary>
public delegate Task PrintExecutor(PrintRequest request, IProgress<int> pageProgress, CancellationToken cancellationToken);

/// <summary>
/// In-memory print job registry. Submissions are always accepted and drained
/// first-in-first-out by a single loop: rendering a PDF at printer resolution is
/// memory-hungry, and serializing the work bounds that cost no matter how many tabs
/// hit the API at once. Terminal jobs are kept for <see cref="_retention"/> so the
/// browser can observe the outcome, then pruned.
/// </summary>
public sealed class PrintJobManager : IDisposable
{
    private readonly PrintExecutor _executor;
    private readonly TimeSpan _retention;
    private readonly ConcurrentDictionary<string, PrintJob> _jobs = new();
    private readonly ConcurrentQueue<(PrintJob Job, PrintRequest Request)> _pending = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Threading.Timer _pruneTimer;

    public PrintJobManager(PrintExecutor executor, TimeSpan? retention = null)
    {
        _executor = executor;
        _retention = retention ?? TimeSpan.FromMinutes(10);
        _pruneTimer = new System.Threading.Timer(_ => Prune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _ = Task.Run(DrainAsync);
    }

    /// <summary>Raised when a job reaches a terminal state (completed, failed or canceled).</summary>
    public event Action<PrintJob>? JobFinished;

    /// <summary>Jobs waiting for the drain loop, not counting the one being printed.</summary>
    public int QueuedCount => _pending.Count;

    /// <summary>Accepts a job for printing. Never rejects: the queue absorbs bursts.</summary>
    public PrintJob Enqueue(PrintRequest request)
    {
        var job = new PrintJob(request.Queue, request.Copies) { PdfBytes = request.PdfBytes };
        _jobs[job.Id] = job;
        _pending.Enqueue((job, request));
        _pendingSignal.Release();
        return job;
    }

    public PrintJob? Get(string jobId) => _jobs.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>Cancels a queued or printing job, or discards a finished one. Returns false when unknown.</summary>
    public bool CancelOrDiscard(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return false;
        }

        if (job.IsTerminal)
        {
            _jobs.TryRemove(jobId, out _);
            return true;
        }

        try
        {
            // A job still waiting in the queue stays there; the drain loop sees the
            // canceled token when it gets to it and finishes it without printing.
            job.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Job finished between the lookup and the cancel; nothing to do.
        }

        return true;
    }

    private async Task DrainAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await _pendingSignal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }

            if (_pending.TryDequeue(out var next))
            {
                await RunAsync(next.Job, next.Request).ConfigureAwait(false);
            }
        }
    }

    private async Task RunAsync(PrintJob job, PrintRequest request)
    {
        var progress = new Progress<int>(pages => job.PagesPrinted = pages);
        try
        {
            job.Cts.Token.ThrowIfCancellationRequested();
            job.Status = PrintJobStatus.Printing;
            await _executor(request, progress, job.Cts.Token).ConfigureAwait(false);
            job.Status = PrintJobStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            job.Status = PrintJobStatus.Canceled;
            job.ErrorCode = PrintErrorCodes.Canceled;
            job.ErrorMessage = "The print job was canceled.";
        }
        catch (PrintBridgeException ex)
        {
            job.Status = PrintJobStatus.Failed;
            job.ErrorCode = ex.Code;
            job.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            job.Status = PrintJobStatus.Failed;
            job.ErrorCode = PrintErrorCodes.PrintFailed;
            job.ErrorMessage = ex.Message;
        }
        finally
        {
            // The document can be tens of megabytes; a terminal job never needs it again.
            job.PdfBytes = null;
            job.FinishedUtc = DateTimeOffset.UtcNow;
            job.Cts.Dispose();
            JobFinished?.Invoke(job);
        }
    }

    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - _retention;
        foreach (var (id, job) in _jobs)
        {
            if (job.IsTerminal && job.FinishedUtc is { } finished && finished < cutoff)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }

    public void Dispose()
    {
        _pruneTimer.Dispose();
        _shutdown.Cancel();
        foreach (var job in _jobs.Values)
        {
            if (!job.IsTerminal)
            {
                try
                {
                    job.Cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
