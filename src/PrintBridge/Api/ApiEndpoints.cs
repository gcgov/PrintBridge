using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PrintBridge.Printing;
using PrintBridge.Settings;

namespace PrintBridge.Api;

/// <summary>
/// Maps the localhost HTTP surface. Responses are camelCase JSON; the document to
/// print is sent as a raw PDF body rather than JSON so nothing has to be base64-encoded.
/// </summary>
public static class ApiEndpoints
{
    /// <summary>Largest PDF accepted, mirrored into the Kestrel request body limit.</summary>
    public const long MaxBodyBytes = 100L * 1024 * 1024;

    public const int MinCopies = 1;

    public const int MaxCopies = 99;

    public static void Map(WebApplication app, SettingsStore settingsStore, PrintJobManager jobManager)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var api = app.MapGroup("/api/v1");

        api.MapGet("/status", () =>
        {
            var settings = settingsStore.Current;
            return Results.Ok(new StatusResponse(
                App: "PrintBridge",
                Version: version,
                ApiVersion: 1,
                QueuesConfigured: settings.Queues.Count,
                DefaultQueue: settings.DefaultQueue));
        });

        api.MapGet("/queues", () =>
        {
            var settings = settingsStore.Current;
            return Results.Ok(settings.Queues.Select(q => new QueueDto(
                q.Name,
                string.Equals(q.Name, settings.DefaultQueue, StringComparison.OrdinalIgnoreCase))));
        });

        api.MapPost("/print-jobs", async (HttpRequest httpRequest, string? queue, int? copies, CancellationToken cancellationToken) =>
        {
            var requestedCopies = copies ?? 1;
            if (requestedCopies is < MinCopies or > MaxCopies)
            {
                return Error(StatusCodes.Status422UnprocessableEntity, PrintErrorCodes.InvalidCopies,
                    $"copies must be between {MinCopies} and {MaxCopies}.");
            }

            var settings = settingsStore.Current;
            if (!settings.TryResolveQueue(queue, out var resolvedQueue, out var errorCode))
            {
                return Error(StatusCodes.Status422UnprocessableEntity, errorCode, errorCode switch
                {
                    PrintErrorCodes.UnknownQueue => $"There is no print queue named '{queue}'.",
                    _ => "No print queue is configured. Open PrintBridge settings and add one.",
                });
            }

            if (httpRequest.ContentLength > MaxBodyBytes)
            {
                return TooLarge();
            }

            byte[] pdfBytes;
            try
            {
                using var buffer = new MemoryStream();
                await httpRequest.Body.CopyToAsync(buffer, cancellationToken);
                pdfBytes = buffer.ToArray();
            }
            catch (BadHttpRequestException)
            {
                // Kestrel aborts the body once it passes MaxRequestBodySize.
                return TooLarge();
            }

            if (!PdfSniffer.LooksLikePdf(pdfBytes))
            {
                return Error(StatusCodes.Status422UnprocessableEntity, PrintErrorCodes.InvalidDocument,
                    "The request body is not a PDF document.");
            }

            var job = jobManager.Enqueue(new PrintRequest(
                Queue: resolvedQueue.Name,
                PrinterName: resolvedQueue.PrinterName,
                Copies: requestedCopies,
                PdfBytes: pdfBytes));

            return Results.Accepted($"/api/v1/print-jobs/{job.Id}", new StartPrintResponse(job.Id));
        });

        api.MapGet("/print-jobs/{jobId}", (string jobId) =>
        {
            var job = jobManager.Get(jobId);
            if (job is null)
            {
                return Error(StatusCodes.Status404NotFound, "notFound", "Unknown print job.");
            }

            return Results.Ok(ToResponse(job));
        });

        api.MapDelete("/print-jobs/{jobId}", (string jobId) =>
        {
            jobManager.CancelOrDiscard(jobId);
            return Results.NoContent();
        });
    }

    private static PrintJobResponse ToResponse(PrintJob job) => new(
        JobId: job.Id,
        Status: job.Status switch
        {
            PrintJobStatus.Queued => "queued",
            PrintJobStatus.Printing => "printing",
            PrintJobStatus.Completed => "completed",
            PrintJobStatus.Failed => "failed",
            PrintJobStatus.Canceled => "canceled",
            _ => "unknown",
        },
        Queue: job.Queue,
        Copies: job.Copies,
        PagesPrinted: job.PagesPrinted,
        Error: job.ErrorCode is null ? null : new ApiErrorBody(job.ErrorCode, job.ErrorMessage ?? string.Empty));

    private static IResult TooLarge() =>
        Error(StatusCodes.Status413PayloadTooLarge, PrintErrorCodes.DocumentTooLarge,
            $"The document is larger than the {MaxBodyBytes / (1024 * 1024)} MB limit.");

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new ApiErrorResponse(new ApiErrorBody(code, message)), statusCode: statusCode);
}
