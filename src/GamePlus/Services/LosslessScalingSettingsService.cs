using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using GamePlus.Models;

namespace GamePlus.Services;

public sealed class LosslessScalingSettingsService
{
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lossless Scaling",
        "Settings.xml");

    private static readonly XmlSerializer Serializer = new(typeof(LosslessScalingSettings));

    public bool SettingsFileExists => File.Exists(SettingsPath);

    public LosslessScalingSettings Load()
    {
        using var stream = File.OpenRead(SettingsPath);
        var settings = (LosslessScalingSettings?)Serializer.Deserialize(stream)
            ?? throw new InvalidDataException("Settings.xml do Lossless Scaling está vazio ou inválido.");
        return settings;
    }

    /// <summary>Writes the settings back atomically (write to temp file, then replace) to avoid
    /// corrupting the file if the app is interrupted mid-write.</summary>
    public void Save(LosslessScalingSettings settings)
    {
        var tempPath = SettingsPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            Serializer.Serialize(stream, settings);
        }
        File.Copy(tempPath, SettingsPath, overwrite: true);
        File.Delete(tempPath);
        TryCopyToLocal();
    }

    /// <summary>Portable copy of the LS settings, kept next to the exe so the folder can be moved to another PC.</summary>
    public static string LocalCopyPath { get; } = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
        "Data",
        "LosslessScaling.Settings.xml");

    public enum SyncResult { Unchanged, LocalCopyUpdated, AppliedToLosslessScaling, Failed }

    /// <summary>
    /// Reconciles the local copy with the real Settings.xml; the most recently modified file wins.
    /// Copying the app folder preserves the timestamp, so a copy made on another PC is newer than a
    /// fresh LS install and gets applied; changes made inside LS itself are newer than the copy and are kept.
    /// </summary>
    public SyncResult SyncWithLocalCopy()
    {
        try
        {
            var hasLs = File.Exists(SettingsPath);
            var hasLocal = File.Exists(LocalCopyPath);
            if (!hasLs && !hasLocal) return SyncResult.Unchanged;

            if (hasLs && hasLocal && File.ReadAllBytes(SettingsPath).AsSpan().SequenceEqual(File.ReadAllBytes(LocalCopyPath)))
            {
                return SyncResult.Unchanged;
            }

            var localIsNewer = hasLocal && (!hasLs || File.GetLastWriteTimeUtc(LocalCopyPath) > File.GetLastWriteTimeUtc(SettingsPath));
            if (localIsNewer)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.Copy(LocalCopyPath, SettingsPath, overwrite: true);
                return SyncResult.AppliedToLosslessScaling;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(LocalCopyPath)!);
            File.Copy(SettingsPath, LocalCopyPath, overwrite: true);
            return SyncResult.LocalCopyUpdated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SyncResult.Failed;
        }
    }

    private static void TryCopyToLocal()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LocalCopyPath)!);
            File.Copy(SettingsPath, LocalCopyPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only app folder: the real settings were saved; only the portable copy is skipped.
        }
    }

    /// <summary>Parses the hotkey configured inside Lossless Scaling into a virtual key
    /// and its modifiers, falling back to F12/none if parsing fails.</summary>
    public static (Keys Key, Keys[] Modifiers) ParseHotkey(LosslessScalingSettings settings)
    {
        var key = Keys.F12;
        if (!string.IsNullOrWhiteSpace(settings.Hotkey) &&
            Enum.TryParse<Keys>(settings.Hotkey, ignoreCase: true, out var parsedKey))
        {
            key = parsedKey;
        }

        var modifiers = new List<Keys>();
        if (!string.IsNullOrWhiteSpace(settings.HotkeyModifierKeys) &&
            !settings.HotkeyModifierKeys.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var token in settings.HotkeyModifierKeys.Split(new[] { ',', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<Keys>(token, ignoreCase: true, out var mod))
                {
                    modifiers.Add(mod);
                }
            }
        }

        return (key, modifiers.ToArray());
    }
}
