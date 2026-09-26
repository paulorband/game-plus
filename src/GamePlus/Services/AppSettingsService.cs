using System.IO;
using System.Text.Json;
using GamePlus.Models;

namespace GamePlus.Services;

/// <summary>Owns Data\settings.json next to the exe (portable, like monitoring.json) and tells listeners when it changes.</summary>
public sealed class AppSettingsService
{
    private static readonly string ConfigPath = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "Data", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; }

    /// <summary>Raised after every save, on the calling thread.</summary>
    public event Action? Changed;

    public AppSettingsService()
    {
        Current = Load();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), JsonOptions);
                if (settings is not null) return settings;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only folder: keep running with the in-memory values.
            Log.Error("Save settings.json", ex);
        }
        Changed?.Invoke();
    }
}
