using PrintBridge.Printing;

namespace PrintBridge.Settings;

/// <summary>
/// A named print queue: web apps ask for <see cref="Name"/> and never learn the
/// Windows printer behind it, so printers can be swapped without touching the web app.
/// </summary>
public sealed class PrintQueueDefinition
{
    /// <summary>Queue name used by API callers (see <see cref="AppSettings.TryNormalizeQueueName"/>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Windows printer name this queue prints to.</summary>
    public string PrinterName { get; set; } = string.Empty;

    public PrintQueueDefinition Clone() => new() { Name = Name, PrinterName = PrinterName };
}

/// <summary>
/// User-editable application settings, persisted as JSON in %AppData%\PrintBridge\settings.json.
/// </summary>
public sealed class AppSettings
{
    public const int DefaultPort = 7227;

    /// <summary>TCP port the local HTTP listener binds on 127.0.0.1.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// Exact web origins (scheme://host[:port]) allowed to call the API via CORS.
    /// Empty means no browser origin is allowed (same-machine tools still work).
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = new();

    /// <summary>Administrator-configured queues. Callers reference these by name.</summary>
    public List<PrintQueueDefinition> Queues { get; set; } = new();

    /// <summary>Name of the queue used when a request omits <c>queue</c>.</summary>
    public string? DefaultQueue { get; set; }

    public bool RunAtLogin { get; set; } = true;

    public AppSettings Clone() => new()
    {
        Port = Port,
        AllowedOrigins = new List<string>(AllowedOrigins),
        Queues = Queues.Select(q => q.Clone()).ToList(),
        DefaultQueue = DefaultQueue,
        RunAtLogin = RunAtLogin,
    };

    /// <summary>Machine-readable reasons a queue could not be resolved, as the API reports them.</summary>
    public const string NoQueueConfiguredCode = PrintErrorCodes.NoQueueConfigured;

    public const string UnknownQueueCode = PrintErrorCodes.UnknownQueue;

    /// <summary>
    /// Resolves the queue for a request: an empty/missing name picks the default queue.
    /// Pure and side-effect free so the routing rules can be unit tested without a printer.
    /// </summary>
    public bool TryResolveQueue(string? requested, out PrintQueueDefinition queue, out string errorCode)
    {
        queue = null!;
        errorCode = string.Empty;

        var namedByCaller = !string.IsNullOrWhiteSpace(requested);
        var name = namedByCaller ? requested!.Trim() : DefaultQueue?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errorCode = NoQueueConfiguredCode;
            return false;
        }

        var match = Queues.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            // The caller naming a queue that does not exist is a caller mistake; a
            // default that points at nothing is an incomplete configuration.
            errorCode = namedByCaller ? UnknownQueueCode : NoQueueConfiguredCode;
            return false;
        }

        queue = match;
        return true;
    }

    /// <summary>
    /// Validates and canonicalizes a queue name: trimmed, 1–64 characters of
    /// letters, digits, dot, underscore or hyphen, starting with a letter or digit.
    /// </summary>
    public static bool TryNormalizeQueueName(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var name = input.Trim();
        if (name.Length > 64)
        {
            return false;
        }

        if (!char.IsAsciiLetterOrDigit(name[0]))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                return false;
            }
        }

        normalized = name;
        return true;
    }

    /// <summary>
    /// Validates and canonicalizes a web origin: http/https, no path/query/fragment,
    /// no credentials; lowercased scheme + host; default ports dropped.
    /// </summary>
    public static bool TryNormalizeOrigin(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        input = input.Trim().TrimEnd('/');
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        // GetLeftPart(Authority) lowercases scheme/host and omits default ports.
        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
