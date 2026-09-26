namespace GamePlus.Models;

/// <summary>App-level preferences edited in the overlay's Settings category (Data\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>Launch straight to the tray instead of opening the window.</summary>
    public bool StartInTray { get; set; }

    /// <summary>A single click on the tray icon opens the app (otherwise a double click does).</summary>
    public bool TrayClickOpens { get; set; } = true;
}
