namespace GamePlus.Models;

/// <summary>Monitoring preferences, stored in Data\monitoring.json next to the exe.</summary>
public sealed class MonitoringConfig
{
    /// <summary>Whether the on-screen display is shown in games.</summary>
    public bool Enabled { get; set; }

    /// <summary>Device name of the monitored GPU (empty until the first detection picks the busiest one).</summary>
    public string GpuName { get; set; } = "";

    /// <summary>Screen position on a 3x3 grid, 0 = top left ... 8 = bottom right.</summary>
    public int Position { get; set; }

    /// <summary>Text size in percent (50-150).</summary>
    public int TextScale { get; set; } = 100;

    /// <summary>Width of the FPS/frametime graphs in text characters (8-48).</summary>
    public int GraphWidth { get; set; } = 32;

    /// <summary>Label and value colors per information group (keys in <see cref="ColorGroups"/>); missing groups use the defaults.</summary>
    public Dictionary<string, GroupColors> Colors { get; set; } = new();

    public string LabelColor(string group) => Colors.TryGetValue(group, out var g) ? g.Label : ColorPresets.DefaultLabel;
    public string ValueColor(string group) => Colors.TryGetValue(group, out var g) ? g.Value : ColorPresets.DefaultValue;

    public void SetColors(string group, string? label = null, string? value = null) =>
        Colors[group] = new GroupColors(label ?? LabelColor(group), value ?? ValueColor(group));

    /// <summary>Metric keys (see <see cref="MetricKeys"/>) that are switched on.</summary>
    public List<string> Metrics { get; set; } = [MetricKeys.GpuTemp, MetricKeys.GpuUse, MetricKeys.CpuTemp, MetricKeys.CpuUse, MetricKeys.Fps, MetricKeys.FrameTime];

    public bool Has(string key) => Metrics.Contains(key);

    public void Set(string key, bool on)
    {
        Metrics.Remove(key);
        if (on) Metrics.Add(key);
    }
}

public static class MetricKeys
{
    public const string GpuTemp = "gpuTemp";
    public const string GpuUse = "gpuUse";
    public const string GpuClock = "gpuClk";
    public const string GpuVram = "gpuVram";
    public const string GpuPower = "gpuPwr";
    public const string GpuVolt = "gpuVolt";
    public const string CpuTemp = "cpuTemp";
    public const string CpuUse = "cpuUse";
    public const string CpuClock = "cpuClk";
    public const string CpuPower = "cpuPwr";
    public const string CpuVolt = "cpuVolt";
    public const string RamUse = "ramUse";
    public const string RamClock = "ramClk";
    public const string Fps = "fps";
    public const string FpsGraph = "fpsGraph";
    public const string FpsAvg = "fpsAvg";
    public const string FrameTime = "ft";
    public const string FrameTimeGraph = "ftGraph";
}

/// <summary>Colors ("RRGGBB") of one group's label and values.</summary>
public sealed record GroupColors(string Label, string Value);

public static class ColorGroups
{
    public const string Gpu = "gpu";
    public const string Cpu = "cpu";
    public const string Ram = "ram";
    public const string Fps = "fps";
    public const string FrameTime = "ft";

    public static readonly (string Key, string Name)[] All =
    [
        (Gpu, "GPU"), (Cpu, "CPU"), (Ram, "RAM"), (Fps, "FPS"), (FrameTime, "Frametime"),
    ];
}
