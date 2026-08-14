using PrintBridge.Printing;
using Xunit;

namespace PrintBridge.Tests;

public class PrintJobManagerTests
{
    private static PrintRequest Request(string queue = "labels", int copies = 1) =>
        new(Queue: queue, PrinterName: "Test Printer", Copies: copies, PdfBytes: [1, 2, 3]);

    private static async Task WaitForTerminal(PrintJob job)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!job.IsTerminal)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "job did not reach a terminal state in time");
            await Task.Delay(10);
        }
    }

    private static async Task WaitFor(Func<bool> condition, string because)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, because);
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task CompletedJobCarriesQueueAndCopies()
    {
        using var manager = new PrintJobManager((_, progress, _) =>
        {
            progress.Report(2);
            return Task.CompletedTask;
        });

        var job = manager.Enqueue(Request(queue: "labels", copies: 3));
        Assert.Equal("labels", job.Queue);
        Assert.Equal(3, job.Copies);

        await WaitForTerminal(job);
        Assert.Equal(PrintJobStatus.Completed, job.Status);
        Assert.Null(job.ErrorCode);
    }

    [Fact]
    public async Task JobsAreDrainedInSubmissionOrder()
    {
        var started = new List<string>();
        var release = new TaskCompletionSource();
        using var manager = new PrintJobManager(async (request, _, _) =>
        {
            lock (started)
            {
                started.Add(request.Queue);
            }

            await release.Task;
        });

        var first = manager.Enqueue(Request("one"));
        var second = manager.Enqueue(Request("two"));
        var third = manager.Enqueue(Request("three"));

        // Only the first job may be in flight while the executor is blocked.
        await WaitFor(() => first.Status == PrintJobStatus.Printing, "the first job never started");
        lock (started)
        {
            Assert.Equal(new[] { "one" }, started);
        }

        Assert.Equal(PrintJobStatus.Queued, second.Status);
        Assert.Equal(PrintJobStatus.Queued, third.Status);

        release.SetResult();
        await WaitForTerminal(third);

        lock (started)
        {
            Assert.Equal(new[] { "one", "two", "three" }, started);
        }
    }

    [Fact]
    public async Task CancelMarksPrintingJobCanceled()
    {
        using var manager = new PrintJobManager(async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct));

        var job = manager.Enqueue(Request());
        await WaitFor(() => job.Status == PrintJobStatus.Printing, "the job never started printing");

        Assert.True(manager.CancelOrDiscard(job.Id));
        await WaitForTerminal(job);

        Assert.Equal(PrintJobStatus.Canceled, job.Status);
        Assert.Equal(PrintErrorCodes.Canceled, job.ErrorCode);
    }

    [Fact]
    public async Task CancelingAQueuedJobSkipsPrintingIt()
    {
        var printed = new List<string>();
        var release = new TaskCompletionSource();
        using var manager = new PrintJobManager(async (request, _, _) =>
        {
            lock (printed)
            {
                printed.Add(request.Queue);
            }

            await release.Task;
        });

        var blocking = manager.Enqueue(Request("blocking"));
        await WaitFor(() => blocking.Status == PrintJobStatus.Printing, "the first job never started");

        var queued = manager.Enqueue(Request("queued"));
        Assert.True(manager.CancelOrDiscard(queued.Id));

        release.SetResult();
        await WaitForTerminal(queued);

        Assert.Equal(PrintJobStatus.Canceled, queued.Status);
        Assert.Equal(PrintErrorCodes.Canceled, queued.ErrorCode);
        lock (printed)
        {
            Assert.Equal(new[] { "blocking" }, printed);
        }
    }

    [Fact]
    public async Task PrintBridgeExceptionMapsToItsCode()
    {
        using var manager = new PrintJobManager((_, _, _) =>
            Task.FromException(new PrintBridgeException(PrintErrorCodes.PrinterUnavailable, "Printer is offline")));

        var job = manager.Enqueue(Request());
        await WaitForTerminal(job);

        Assert.Equal(PrintJobStatus.Failed, job.Status);
        Assert.Equal(PrintErrorCodes.PrinterUnavailable, job.ErrorCode);
        Assert.Equal("Printer is offline", job.ErrorMessage);
    }

    [Fact]
    public async Task UnexpectedExceptionMapsToPrintFailed()
    {
        using var manager = new PrintJobManager((_, _, _) =>
            Task.FromException(new InvalidOperationException("boom")));

        var job = manager.Enqueue(Request());
        await WaitForTerminal(job);

        Assert.Equal(PrintJobStatus.Failed, job.Status);
        Assert.Equal(PrintErrorCodes.PrintFailed, job.ErrorCode);
        Assert.Equal("boom", job.ErrorMessage);
    }

    [Fact]
    public async Task ProgressUpdatesPagesPrinted()
    {
        var release = new TaskCompletionSource();
        using var manager = new PrintJobManager(async (_, progress, _) =>
        {
            progress.Report(4);
            await release.Task;
        });

        var job = manager.Enqueue(Request());

        // Progress<T> marshals via the thread pool, so poll briefly.
        await WaitFor(() => job.PagesPrinted == 4, "progress was not observed in time");

        release.SetResult();
        await WaitForTerminal(job);
        Assert.Equal(PrintJobStatus.Completed, job.Status);
    }

    [Fact]
    public async Task TerminalJobReleasesTheDocument()
    {
        var release = new TaskCompletionSource();
        using var manager = new PrintJobManager(async (_, _, _) => await release.Task);

        var job = manager.Enqueue(Request());
        await WaitFor(() => job.Status == PrintJobStatus.Printing, "the job never started printing");
        Assert.NotNull(job.PdfBytes);

        release.SetResult();
        await WaitForTerminal(job);
        Assert.Null(job.PdfBytes);
    }

    [Fact]
    public async Task DiscardRemovesFinishedJob()
    {
        using var manager = new PrintJobManager((_, _, _) => Task.CompletedTask);

        var job = manager.Enqueue(Request());
        await WaitForTerminal(job);

        Assert.NotNull(manager.Get(job.Id));
        Assert.True(manager.CancelOrDiscard(job.Id));
        Assert.Null(manager.Get(job.Id));
    }

    [Fact]
    public void UnknownJobReturnsFalseAndNull()
    {
        using var manager = new PrintJobManager((_, _, _) => Task.CompletedTask);
        Assert.Null(manager.Get("nope"));
        Assert.False(manager.CancelOrDiscard("nope"));
    }
}
