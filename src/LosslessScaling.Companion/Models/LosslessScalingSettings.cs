using System.Xml.Serialization;

namespace LosslessScaling.Companion.Models;

[XmlRoot("Settings", Namespace = "")]
public sealed class LosslessScalingSettings
{
    public bool WindowMaximized { get; set; }
    public int WindowHeight { get; set; }
    public int WindowWidth { get; set; }
    public int ProfileListWidth { get; set; }

    // The user's configured toggle hotkey inside Lossless Scaling itself.
    // The companion app sends this exact key so it always matches what LS expects.
    public string Hotkey { get; set; } = "F12";
    public string HotkeyModifierKeys { get; set; } = "None";

    public int GpuPreference { get; set; }
    public int GpuPreferenceChangeCount { get; set; }
    public int SetupPhase { get; set; }
    public bool StartAsAdmin { get; set; }
    public bool StartAtWindowsStartup { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; }
    public string Language { get; set; } = "System";
    public string Theme { get; set; } = "System";

    public bool FrameGenerationCollapsed { get; set; }
    public bool CursorOptionsCollapsed { get; set; }
    public bool RenderingOptionsCollapsed { get; set; }
    public bool CaptureOptionsCollapsed { get; set; }
    public bool GpuDisplayOptionsCollapsed { get; set; }
    public bool CropInputOptionsCollapsed { get; set; }
    public bool BehaviorCollapsed { get; set; }
    public bool LegacyOptionsCollapsed { get; set; }

    [XmlArray("GameProfiles")]
    [XmlArrayItem("Profile")]
    public List<LosslessScalingProfile> GameProfiles { get; set; } = new();
}

public sealed class LosslessScalingProfile
{
    public string Title { get; set; } = "Padrão";

    public bool AutoScale { get; set; }
    public int AutoScaleDelay { get; set; }

    public string ScalingMode { get; set; } = "Auto";
    public string ScalingFitMode { get; set; } = "AspectRatio";
    public double ScaleFactor { get; set; } = 1.5;
    public bool ResizeBeforeScaling { get; set; }
    public bool WindowedMode { get; set; }
    public string ScalingType { get; set; } = "Off";
    public string FSRType { get; set; } = "ORIGINAL";
    public string LS1Type { get; set; } = "BALANCED";

    public string LSFG2Mode { get; set; } = "X2";
    public string LSFG3Mode1 { get; set; } = "FIXED";
    public int LSFG3Multiplier { get; set; } = 2;
    public int LSFG3Target { get; set; } = 144;
    public int LSFGFlowScale { get; set; } = 75;
    public string LSFGSize { get; set; } = "BALANCED";

    public string Anime4kType { get; set; } = "S";
    public int Sharpness { get; set; }
    public int LS1Sharpness { get; set; }
    public bool VRS { get; set; }

    // "Off", "LSFG2", "LSFG3" — the master frame-generation mode switch.
    public string FrameGeneration { get; set; } = "Off";

    public bool ClipCursor { get; set; }
    public bool AdjustCursorSpeed { get; set; }
    public bool HideCursor { get; set; }
    public bool ScaleCursor { get; set; }

    public string SyncMode { get; set; } = "DEFAULT";
    public int MaxFrameLatency { get; set; } = 3;
    public bool GsyncSupport { get; set; }
    public bool HdrSupport { get; set; }
    public bool DrawFps { get; set; }
    public string CaptureApi { get; set; } = "DXGI";
    public int QueueTarget { get; set; } = 1;
    public int PreferredGpuId { get; set; }
    public int OutputDisplayId { get; set; }

    public bool CropInput { get; set; }
    public int CropInputLeft { get; set; }
    public int CropInputTop { get; set; }
    public int CropInputRight { get; set; }
    public int CropInputBottom { get; set; }
    public bool MultiDisplayMode { get; set; }
}
