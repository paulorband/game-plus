using System.IO;

namespace GamePlus.Services;

/// <summary>Captures the monitor the player is actually looking at (the one showing the foreground window)
/// to a timestamped PNG, like a standalone screenshot tool.</summary>
public sealed class ScreenshotService
{
    private readonly AppSettingsService _appSettings;

    public ScreenshotService(AppSettingsService appSettings) => _appSettings = appSettings;

    /// <summary>Default save folder. Deliberately NOT next to the exe (unlike config.json/Data\): screenshots are
    /// meant to be found in the usual Windows spot, so other capture tools (Xbox Game Bar, Nvidia/AMD overlays)
    /// and photo apps pick them up the same way.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots", "GamePlus");

    /// <summary>The folder screenshots are written to: the user's choice, or <see cref="DefaultFolder"/>.</summary>
    public string ResolvedFolder() =>
        string.IsNullOrWhiteSpace(_appSettings.Current.ScreenshotFolder) ? DefaultFolder : _appSettings.Current.ScreenshotFolder!;

    /// <summary>Captures just the monitor showing the current foreground window (the game, in the common case)
    /// to a PNG in the configured folder. Falls back to the primary monitor if there is no foreground window.
    /// Returns the saved path.</summary>
    public string Capture()
    {
        var folder = ResolvedFolder();
        Directory.CreateDirectory(folder);

        var foreground = HotkeyInputSender.GetForegroundWindow();
        var screen = foreground != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(foreground) : null;
        var bounds = (screen ?? System.Windows.Forms.Screen.PrimaryScreen)?.Bounds
            ?? System.Windows.Forms.SystemInformation.VirtualScreen;

        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        }

        var path = Path.Combine(folder, $"GamePlus_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }
}
