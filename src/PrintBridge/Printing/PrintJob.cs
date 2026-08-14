namespace PrintBridge.Printing;

public enum PrintJobStatus
{
    Queued,
    Printing,
    Completed,
    Failed,
    Canceled,
}

/// <summary>Machine-readable error codes surfaced to API clients.</summary>
public static class PrintErrorCodes
{
    public const string NoQueueConfigured = "noQueueConfigured";
    public const string UnknownQueue = "unknownQueue";
    public const string InvalidDocument = "invalidDocument";
    public const string InvalidCopies = "invalidCopies";
    public const string DocumentTooLarge = "documentTooLarge";
    public const string PrinterUnavailable = "printerUnavailable";
    public const string Canceled = "canceled";
    public const string PrintFailed = "printFailed";
}

/// <summary>Thrown by the print pipeline with a machine-readable code from <see cref="PrintErrorCodes"/>.</summary>
public sealed class PrintBridgeException : Exception
{
    public PrintBridgeException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// Resolved parameters for one print: the queue has already been matched against the
/// configuration, so the printing layer never sees a caller-supplied printer name.
/// </summary>
public sealed record PrintRequest(
    string Queue,
    string PrinterName,
    int Copies,
    byte[] PdfBytes);

public sealed class PrintJob
{
    public PrintJob(string queue, int copies)
    {
        Queue = queue;
        Copies = copies;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Name of the queue the job was submitted to (never the Windows printer name).</summary>
    public string Queue { get; }

    public int Copies { get; }

    public PrintJobStatus Status { get; internal set; } = PrintJobStatus.Queued;

    /// <summary>Pages handed to the spooler so far; multiplied out over copies.</summary>
    public int PagesPrinted { get; internal set; }

    public string? ErrorCode { get; internal set; }

    public string? ErrorMessage { get; internal set; }

    /// <summary>The submitted PDF. Released (set to null) once the job reaches a terminal state.</summary>
    public byte[]? PdfBytes { get; internal set; }

    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedUtc { get; internal set; }

    internal CancellationTokenSource Cts { get; } = new();

    public bool IsTerminal => Status is PrintJobStatus.Completed or PrintJobStatus.Failed or PrintJobStatus.Canceled;
}
