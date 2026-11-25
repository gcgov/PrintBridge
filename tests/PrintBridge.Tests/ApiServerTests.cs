using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PrintBridge.Hosting;
using PrintBridge.Printing;
using PrintBridge.Settings;
using Serilog;
using Xunit;

namespace PrintBridge.Tests;

/// <summary>
/// End-to-end tests of the real Kestrel host (<see cref="WebHostRunner"/>) on a free
/// loopback port, with a fake print executor instead of a physical printer.
/// </summary>
public sealed class ApiServerTests : IAsyncLifetime
{
    private const string AllowedOrigin = "https://apps.example.gov";
    private const string OtherOrigin = "https://evil.example.com";
    private const string QueueName = "front-desk";

    private static readonly byte[] FakePdf = Encoding.ASCII.GetBytes("%PDF-1.4 fake");

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintBridgeApiTests-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();
    private readonly ConcurrentQueue<PrintRequest> _printed = new();
    private TaskCompletionSource _printerGate = OpenGate();
    private int _port;
    private PrintJobManager _jobManager = null!;
    private WebHostRunner _runner = null!;

    private string BaseUrl => $"http://127.0.0.1:{_port}/api/v1";

    public async Task InitializeAsync()
    {
        _port = GetFreePort();
        var settingsStore = new SettingsStore(_tempDir);
        settingsStore.Load();
        settingsStore.Save(new AppSettings
        {
            Port = _port,
            AllowedOrigins = [AllowedOrigin],
            Queues = [new PrintQueueDefinition { Name = QueueName, PrinterName = "Test Printer" }],
            DefaultQueue = QueueName,
        });

        _jobManager = new PrintJobManager((request, _, cancellationToken) =>
        {
            _printed.Enqueue(request);
            // A closed gate keeps the job in the "printing" state until a test opens it.
            return _printerGate.Task.WaitAsync(cancellationToken);
        });
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

    private static TaskCompletionSource OpenGate()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return gate;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? origin = null, string? language = null)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    private HttpRequestMessage PrintPost(string? origin = null, string contentType = "application/pdf")
    {
        var request = Request(HttpMethod.Post, "/print-jobs", origin);
        request.Content = new ByteArrayContent(FakePdf);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return request;
    }

    private static async Task<(string Code, string Message)> ReadErrorAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = json.RootElement.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);
    }

    private async Task<string> StartPrintAsync()
    {
        var response = await _http.SendAsync(PrintPost());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("jobId").GetString()!;
    }

    private async Task WaitUntilCompletedAsync(string jobId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "print job did not complete in time");
            using var json = JsonDocument.Parse(await _http.GetStringAsync($"{BaseUrl}/print-jobs/{jobId}"));
            var status = json.RootElement.GetProperty("status").GetString();
            if (status is "completed" or "failed" or "canceled")
            {
                Assert.Equal("completed", status);
                return;
            }

            await Task.Delay(20);
        }
    }

    private async Task WaitUntilPrintedCountIsAsync(int count)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (_printed.Count < count)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "print job did not reach the printer in time");
            await Task.Delay(10);
        }
    }

    // ---- Origin check on POST/DELETE (no custom header needed) ----

    [Fact]
    public async Task PostWithoutOriginIsAccepted()
    {
        // Same-machine tools (curl, desktop apps) send no Origin header.
        var response = await _http.SendAsync(PrintPost());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostFromAllowedOriginIsAccepted()
    {
        var response = await _http.SendAsync(PrintPost(origin: AllowedOrigin));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task PostFromAllowedOriginWithACustomHeaderIsAccepted()
    {
        // The CORS policy allows any request header, so existing clients keep working.
        var request = PrintPost(origin: AllowedOrigin);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData(OtherOrigin)]
    [InlineData("null")]
    [InlineData("https://apps.example.gov.evil.example.com")]
    public async Task PostFromUnauthorizedOriginIsRejected(string origin)
    {
        var response = await _http.SendAsync(PrintPost(origin: origin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var (code, message) = await ReadErrorAsync(response);
        Assert.Equal("originNotAllowed", code);
        Assert.Contains(origin, message);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task SimplePostFromUnauthorizedOriginPrintsNothing(string contentType)
    {
        // A "simple" request (no preflight) is what CORS alone cannot stop.
        var response = await _http.SendAsync(PrintPost(origin: OtherOrigin, contentType: contentType));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Jobs print in submission order, so once a later job completes, an earlier one
        // would already be in the executor. Only the later job may reach it.
        var jobId = await StartPrintAsync();
        await WaitUntilCompletedAsync(jobId);
        Assert.Single(_printed);
    }

    [Fact]
    public async Task DeleteFromUnauthorizedOriginIsRejected()
    {
        var response = await _http.SendAsync(Request(HttpMethod.Delete, "/print-jobs/anything", origin: OtherOrigin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("originNotAllowed", (await ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task DeleteFromUnauthorizedOriginDoesNotCancelTheJob()
    {
        _printerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobId = await StartPrintAsync();
        await WaitUntilPrintedCountIsAsync(1);

        var response = await _http.SendAsync(Request(HttpMethod.Delete, $"/print-jobs/{jobId}", origin: OtherOrigin));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Had the DELETE reached the job, its token would be canceled and the job would end "canceled".
        _printerGate.SetResult();
        await WaitUntilCompletedAsync(jobId);
    }

    [Fact]
    public async Task DeleteWithoutOriginIsAccepted()
    {
        var response = await _http.SendAsync(Request(HttpMethod.Delete, "/print-jobs/anything"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFromAllowedOriginIsAccepted()
    {
        var response = await _http.SendAsync(Request(HttpMethod.Delete, "/print-jobs/anything", origin: AllowedOrigin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetFromUnauthorizedOriginIsAnsweredWithoutCorsHeader()
    {
        // Reads stay as they were: the browser hides the response from the page.
        var response = await _http.SendAsync(Request(HttpMethod.Get, "/status", origin: OtherOrigin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OriginNotAllowedMessageFollowsAcceptLanguage()
    {
        var request = PrintPost(origin: OtherOrigin);
        request.Headers.Add("Accept-Language", "it-IT");

        var response = await _http.SendAsync(request);

        var (code, message) = await ReadErrorAsync(response);
        Assert.Equal("originNotAllowed", code);
        Assert.Equal($"Il sito {OtherOrigin} non è autorizzato a usare PrintBridge. Aggiungilo nelle impostazioni di PrintBridge.", message);
    }

    // ---- Host validation (DNS rebinding) ----

    [Theory]
    [InlineData("evil.com")]
    [InlineData("evil.com:1234")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1:1")]
    public async Task InvalidHostIsRejected(string host)
    {
        var request = Request(HttpMethod.Get, "/status");
        request.Headers.Host = host;

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("invalidHost", (await ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task PostWithInvalidHostPrintsNothing()
    {
        var request = PrintPost();
        request.Headers.Host = $"evil.com:{_port}";

        var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var jobId = await StartPrintAsync();
        await WaitUntilCompletedAsync(jobId);
        Assert.Single(_printed);
    }

    [Fact]
    public async Task LocalhostHostWithPortIsAccepted()
    {
        var request = Request(HttpMethod.Get, "/status");
        request.Headers.Host = $"localhost:{_port}";

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- CORS preflight ----

    [Fact]
    public async Task PreflightFromAllowedOriginIsAnswered()
    {
        var request = Request(HttpMethod.Options, "/print-jobs", origin: AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await _http.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode, $"preflight returned {(int)response.StatusCode}");
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("content-type", allowedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreflightFromUnauthorizedOriginIsNotAllowed()
    {
        var request = Request(HttpMethod.Options, "/print-jobs", origin: OtherOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await _http.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
