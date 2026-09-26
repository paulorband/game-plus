using System.IO;
using System.Text.Json;
using GamePlus.Models;

namespace GamePlus.Services;

/// <summary>Reads/writes Data\monitoring.json next to the exe, so the folder stays portable.</summary>
public sealed class MonitoringConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "Data", "monitoring.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MonitoringConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var config = JsonSerializer.Deserialize<MonitoringConfig>(File.ReadAllText(ConfigPath), JsonOptions);
                if (config is not null)
                {
                    config.Position = Math.Clamp(config.Position, 0, 8);
                    config.TextScale = Math.Clamp(config.TextScale, 50, 150);
                    config.GraphWidth = Math.Clamp(config.GraphWidth, 8, 48);
                    config.Colors = config.Colors
                        .Where(p => ColorGroups.All.Any(g => g.Key == p.Key) && p.Value is not null)
                        .ToDictionary(p => p.Key, p => new GroupColors(
                            ColorPresets.Normalize(p.Value.Label, ColorPresets.DefaultLabel),
                            ColorPresets.Normalize(p.Value.Value, ColorPresets.DefaultValue)));
                    return config;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable: fall back to defaults.
        }
        return new MonitoringConfig();
    }

    public void Save(MonitoringConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only folder: keep running with the in-memory values.
            Log.Error("Save monitoring.json", ex);
        }
    }
}
