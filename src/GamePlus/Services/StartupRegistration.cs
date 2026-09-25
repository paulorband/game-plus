using Microsoft.Win32;

namespace GamePlus.Services;

/// <summary>Registers/unregisters this app under HKCU Run so it starts silently with Windows.</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GamePlus";
    private const string LegacyValueName = "LosslessScaling.Companion";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        // Older builds registered under the previous app name; drop that entry so it can't launch a stale exe.
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                key.SetValue(ValueName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
