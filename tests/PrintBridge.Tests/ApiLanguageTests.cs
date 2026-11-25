using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PrintBridge.Hosting;
using PrintBridge.Printing;
using PrintBridge.Settings;
using Serilog;
using Xunit;

namespace PrintBridge.Tests;

/// <summary>
/// Error messages from the real Kestrel host follow Accept-Language; error codes never change.
/// </summary>
public sealed class ApiLanguageTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintBridgeApiTests-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();
    private int _port;
    private PrintJobManager _jobManager = null!;
    private WebHostRunner _runner = null!;

    private string BaseUrl => $"http://127.0.0.1:{_port}/api/v1";

    public async Task InitializeAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var settingsStore = new SettingsStore(_tempDir);
        settingsStore.Load();
        settingsStore.Save(new AppSettings { Port = _port });

        _jobManager = new PrintJobManager((_, _, _) => Task.CompletedTask);
        _runner = new WebHostRunner(settingsStore, _jobManager, new LoggerConfiguration().CreateLogger());
        await _runner.StartAsync();
        Assert.True(_runner.IsRunning, _runner.LastError?.ToString());
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _runner.DisposeAsync();
        _jobManager.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<(string Code, string Message, string? Language)> GetErrorAsync(string path, string? acceptLanguage)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        if (acceptLanguage is not null)
        {
            request.Headers.Add("Accept-Language", acceptLanguage);
        }

        var response = await _http.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = json.RootElement.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!,
            response.Content.Headers.ContentLanguage.FirstOrDefault());
    }

    [Fact]
    public async Task ErrorMessageIsEnglishWithoutAcceptLanguage()
    {
        var (code, message, _) = await GetErrorAsync("/print-jobs/unknown", acceptLanguage: null);

        Assert.Equal("notFound", code);
        Assert.Equal("Unknown print job.", message);
    }

    [Fact]
    public async Task ErrorMessageFollowsAcceptLanguage()
    {
        var (code, message, language) = await GetErrorAsync("/print-jobs/unknown", "it-IT,it;q=0.9,en;q=0.8");

        Assert.Equal("notFound", code);
        Assert.Equal("Stampa sconosciuta.", message);
        Assert.Equal("it", language);
    }

    [Fact]
    public async Task UnsupportedAcceptLanguageFallsBackToEnglish()
    {
        var (_, message, _) = await GetErrorAsync("/print-jobs/unknown", "de-DE");

        Assert.Equal("Unknown print job.", message);
    }
}
