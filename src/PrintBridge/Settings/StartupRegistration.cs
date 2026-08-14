using Microsoft.Win32;

namespace PrintBridge.Settings;

/// <summary>
/// Registers/unregisters the app in the current user's Run key so it starts at login.
/// Per-user only — never requires elevation.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PrintBridge";

    public static void Apply(bool runAtLogin)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (runAtLogin)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            key.SetValue(ValueName, $"\"{exePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
