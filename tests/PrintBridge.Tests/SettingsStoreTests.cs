using PrintBridge.Settings;
using Xunit;

namespace PrintBridge.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintBridgeTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadWithoutFileReturnsDefaultsAndFlagsFirstRun()
    {
        var store = new SettingsStore(_tempDir);
        var settings = store.Load();

        Assert.True(store.IsFirstRun);
        Assert.Equal(AppSettings.DefaultPort, settings.Port);
        Assert.Empty(settings.AllowedOrigins);
        Assert.Empty(settings.Queues);
        Assert.Null(settings.DefaultQueue);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new SettingsStore(_tempDir);
        store.Load();

        var settings = new AppSettings
        {
            Port = 9111,
            AllowedOrigins = ["https://apps.example.gov"],
            Queues =
            [
                new PrintQueueDefinition { Name = "labels", PrinterName = "ZDesigner GK420d" },
                new PrintQueueDefinition { Name = "front-desk", PrinterName = "HP LaserJet M404" },
            ],
            DefaultQueue = "labels",
            RunAtLogin = false,
        };
        store.Save(settings);

        var reloaded = new SettingsStore(_tempDir).Load();
        Assert.Equal(9111, reloaded.Port);
        Assert.Equal(new[] { "https://apps.example.gov" }, reloaded.AllowedOrigins);
        Assert.Equal(2, reloaded.Queues.Count);
        Assert.Equal("labels", reloaded.Queues[0].Name);
        Assert.Equal("ZDesigner GK420d", reloaded.Queues[0].PrinterName);
        Assert.Equal("front-desk", reloaded.Queues[1].Name);
        Assert.Equal("labels", reloaded.DefaultQueue);
        Assert.False(reloaded.RunAtLogin);
    }

    [Fact]
    public void SavedFileUsesCamelCaseNames()
    {
        var store = new SettingsStore(_tempDir);
        store.Load();
        store.Save(new AppSettings
        {
            Queues = [new PrintQueueDefinition { Name = "labels", PrinterName = "ZDesigner GK420d" }],
            DefaultQueue = "labels",
        });

        var json = File.ReadAllText(store.SettingsPath);
        Assert.Contains("\"defaultQueue\"", json);
        Assert.Contains("\"printerName\"", json);
    }

    [Fact]
    public void SaveRaisesChanged()
    {
        var store = new SettingsStore(_tempDir);
        store.Load();

        AppSettings? observed = null;
        store.Changed += s => observed = s;
        store.Save(new AppSettings { Port = 9112 });

        Assert.NotNull(observed);
        Assert.Equal(9112, observed!.Port);
        Assert.False(store.IsFirstRun);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "{not json!!");

        var settings = new SettingsStore(_tempDir).Load();
        Assert.Equal(AppSettings.DefaultPort, settings.Port);
        Assert.Empty(settings.Queues);
    }

    [Fact]
    public void CloneDoesNotShareQueueInstances()
    {
        var settings = new AppSettings
        {
            Queues = [new PrintQueueDefinition { Name = "labels", PrinterName = "ZDesigner GK420d" }],
        };

        var clone = settings.Clone();
        clone.Queues[0].PrinterName = "Something else";

        Assert.Equal("ZDesigner GK420d", settings.Queues[0].PrinterName);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
