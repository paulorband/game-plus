namespace GamePlus.Models;

public sealed class AppConfig
{
    /// <summary>Poll interval for reading the controller state, in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 16;

    /// <summary>How long a combo must stay held before it fires, in milliseconds.</summary>
    public int ComboHoldMs { get; set; } = 350;

    /// <summary>Cooldown after a combo fires before it can fire again, in milliseconds.</summary>
    public int ComboCooldownMs { get; set; } = 600;

    /// <summary>Buttons that must be held together to toggle scaling/frame generation.
    /// Deliberately avoids A/B/X/Y/LB/RB/D-Pad, which the settings overlay uses for navigation.</summary>
    public string[] ToggleCombo { get; set; } = ["Back", "LeftThumb"];

    /// <summary>Buttons that must be held together to open/close the settings overlay.</summary>
    public string[] OverlayCombo { get; set; } = ["Back", "Start"];

    /// <summary>
    /// Optional explicit path to LosslessScaling.exe. Leave empty to auto-detect
    /// (Steam library, common install folders, or registry uninstall entries).
    /// </summary>
    public string? LosslessScalingExePath { get; set; }

    /// <summary>Automatically relaunch Lossless Scaling after saving settings so changes take effect.</summary>
    public bool AutoRestartOnSave { get; set; } = true;
}
