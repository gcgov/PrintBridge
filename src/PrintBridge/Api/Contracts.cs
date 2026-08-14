namespace PrintBridge.Api;

public sealed record StatusResponse(
    string App,
    string Version,
    int ApiVersion,
    int QueuesConfigured,
    string? DefaultQueue);

/// <summary>A queue as web apps see it: a name, never the Windows printer behind it.</summary>
public sealed record QueueDto(string Name, bool IsDefault);

public sealed record StartPrintResponse(string JobId);

public sealed record ApiErrorBody(string Code, string Message);

public sealed record ApiErrorResponse(ApiErrorBody Error);

public sealed record PrintJobResponse(
    string JobId,
    string Status,
    string Queue,
    int Copies,
    int PagesPrinted,
    ApiErrorBody? Error);
