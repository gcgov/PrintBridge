using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintBridge.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Writes are atomic
/// (temp file + replace) so a crash mid-save never corrupts settings.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _directory;
    private readonly Lock _sync = new();

    public SettingsStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintBridge");
    }

    public string SettingsPath => Path.Combine(_directory, "settings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>Raised after <see cref="Save"/> persists a new snapshot.</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>True when no settings file existed at load time (first run).</summary>
    public bool IsFirstRun { get; private set; }

    public AppSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(SettingsPath))
            {
                IsFirstRun = true;
                Current = new AppSettings();
                return Current;
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Unreadable settings should not brick the app; fall back to defaults.
                Current = new AppSettings();
            }

            return Current;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(SettingsPath))
            {
                File.Replace(tempPath, SettingsPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, SettingsPath);
            }

            Current = settings;
            IsFirstRun = false;
        }

        Changed?.Invoke(settings);
    }
}
