using System.IO;
using System.Text.Json;
using LosslessScaling.Companion.Models;

namespace LosslessScaling.Companion.Services;

public sealed class AppConfigService
{
    // Portable: config lives next to the exe.
    private static readonly string ConfigDirectory =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");

    // Where earlier versions kept it; copied over once so existing settings survive.
    private static readonly string LegacyConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LosslessScaling.Companion", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
            {
                File.Copy(LegacyConfigPath, ConfigPath);
            }

            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config is not null)
                {
                    return config;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fall through to defaults if the file is missing or corrupt.
        }

        var defaultConfig = new AppConfig();
        try
        {
            Save(defaultConfig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only folder: run with defaults in memory.
        }
        return defaultConfig;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
