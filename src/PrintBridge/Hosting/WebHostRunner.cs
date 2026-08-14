using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintBridge.Api;
using PrintBridge.Printing;
using PrintBridge.Settings;
using Serilog;

namespace PrintBridge.Hosting;

/// <summary>
/// Hosts Kestrel inside the WinForms process, bound to 127.0.0.1 only. Rebuilds the
/// host when settings (port / allowed origins) change.
/// </summary>
public sealed class WebHostRunner : IAsyncDisposable
{
    private const string CorsPolicyName = "browser";

    private readonly SettingsStore _settingsStore;
    private readonly PrintJobManager _jobManager;
    private readonly Serilog.ILogger _logger;
    private WebApplication? _app;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    public WebHostRunner(SettingsStore settingsStore, PrintJobManager jobManager, Serilog.ILogger logger)
    {
        _settingsStore = settingsStore;
        _jobManager = jobManager;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Raised on start/stop/failure so the tray and settings window can show listener state.</summary>
    public event Action? StatusChanged;

    public async Task StartAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);

            var settings = _settingsStore.Current;
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    ContentRootPath = AppContext.BaseDirectory,
                });
                builder.Logging.ClearProviders();
                builder.Logging.AddSerilog(_logger);
                builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
                    .WithOrigins(settings.AllowedOrigins.ToArray())
                    .WithMethods("GET", "POST", "DELETE")
                    .AllowAnyHeader()));
                builder.WebHost.ConfigureKestrel(kestrel =>
                {
                    kestrel.Listen(IPAddress.Loopback, settings.Port);
                    // Documents are posted as raw PDF bodies; the API turns the
                    // resulting 413 into a documentTooLarge error.
                    kestrel.Limits.MaxRequestBodySize = ApiEndpoints.MaxBodyBytes;
                });

                var app = builder.Build();
                app.UseCors(CorsPolicyName);
                ApiEndpoints.Map(app, _settingsStore, _jobManager);

                await app.StartAsync().ConfigureAwait(false);
                _app = app;
                IsRunning = true;
                LastError = null;
                _logger.Information("PrintBridge API listening on http://127.0.0.1:{Port}", settings.Port);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                LastError = ex is IOException
                    ? $"Could not listen on port {settings.Port} — it may be in use by another program."
                    : ex.Message;
                _logger.Error(ex, "Failed to start the PrintBridge API on port {Port}", settings.Port);
            }
        }
        finally
        {
            _lifecycleLock.Release();
            StatusChanged?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
            StatusChanged?.Invoke();
        }
    }

    private async Task StopCoreAsync()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error stopping the PrintBridge API");
        }
        finally
        {
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
            IsRunning = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
    }
}
