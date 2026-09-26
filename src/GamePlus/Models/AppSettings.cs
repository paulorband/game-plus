namespace GamePlus.Models;

/// <summary>App-level preferences edited in the overlay's Settings category (Data\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>"dark" or "light".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Opacity of the overlay panel, 50-100 percent.</summary>
    public int PanelOpacity { get; set; } = 95;

    /// <summary>Launch straight to the tray instead of opening the window.</summary>
    public bool StartInTray { get; set; }

    /// <summary>A single click on the tray icon opens the app (otherwise a double click does).</summary>
    public bool TrayClickOpens { get; set; } = true;
}
