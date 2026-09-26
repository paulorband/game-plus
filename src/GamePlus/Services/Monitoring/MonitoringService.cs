using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GamePlus.Models;

namespace GamePlus.Services.Monitoring;

/// <summary>What the overlay needs to know about the monitoring pipeline right now.</summary>
public sealed record MonitoringStatus(
    bool Afterburner,
    bool Rtss,
    IReadOnlyList<string> Gpus,
    IReadOnlySet<string> Available,
    string? Game)
{
    public static MonitoringStatus Empty { get; } = new(false, false, [], new HashSet<string>(), null);
    public bool Ready => Afterburner && Rtss;
}

/// <summary>
/// Reads sensors from Afterburner, the game's FPS from RTSS, and draws the result as an RTSS on-screen display.
/// Runs on its own loop so the display keeps working while the overlay is closed.
/// </summary>
public sealed partial class MonitoringService : IDisposable
{
    private static readonly string[] IgnoredProcesses = ["GamePlus.exe", "LosslessScaling.exe"];

    private readonly MonitoringConfigService _store;
    private readonly MahmReader _mahm = new();
    private readonly RtssClient _rtss = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Queue<double> _fpsHistory = new();
    private int _trackedPid;
    private bool _osdVisible;

    public MonitoringConfig Config { get; }

    /// <summary>Latest status; replaced as a whole, so it is safe to read from any thread.</summary>
    public MonitoringStatus Status { get; private set; } = MonitoringStatus.Empty;

    public MonitoringService(MonitoringConfigService store)
    {
        _store = store;
        Config = store.Load();
    }

    public void Start() => _ = Task.Run(LoopAsync);

    /// <summary>Persists the config after a change made in the overlay.</summary>
    public void Save() => _store.Save(Config);

    /// <summary>How many switched-on metrics can actually be shown.</summary>
    public int ActiveMetricCount => Config.Metrics.Count(k => IsAvailable(k));

    public bool IsAvailable(string key) =>
        key is MetricKeys.Fps or MetricKeys.FpsAvg or MetricKeys.FrameTime or MetricKeys.FpsGraph or MetricKeys.FrameTimeGraph
        || Status.Available.Contains(key);

    private async Task LoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try { Tick(); }
                catch (Exception ex) { Log.Error("Monitoring tick", ex); }
                await Task.Delay(Config.Enabled ? 500 : 1500, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            Log.Error("Monitoring loop", ex);
        }
    }

    private void Tick()
    {
        var snapshot = _mahm.TryRead();
        var rtss = _rtss.Connect();

        var gpus = (IReadOnlyList<string>)(snapshot?.Gpus ?? []);
        if (snapshot is not null) PickDefaultGpu(snapshot);
        var gpuNumber = GpuNumber(gpus);

        var game = rtss ? _rtss.ReadGame(ForegroundProcessId(), IgnoredProcesses) : null;
        TrackFps(game);

        Status = new MonitoringStatus(snapshot is not null, rtss, gpus, AvailableKeys(snapshot, gpuNumber), game?.ProcessName);

        if (Config.Enabled && snapshot is not null && rtss)
        {
            var (text, objects) = BuildOsd(snapshot, gpuNumber, game);
            _osdVisible = _rtss.WriteOsd(text, objects);
        }
        else if (_osdVisible)
        {
            _rtss.ReleaseOsd();
            _osdVisible = false;
        }
    }

    // First run: monitor the GPU with the most memory in use (the discrete one on desktops), then keep that choice.
    private void PickDefaultGpu(MahmSnapshot snapshot)
    {
        if (snapshot.Gpus.Count == 0 || snapshot.Gpus.Contains(Config.GpuName)) return;

        var best = 0;
        var bestUsed = -1f;
        for (var i = 0; i < snapshot.Gpus.Count; i++)
        {
            var used = snapshot.Values.GetValueOrDefault($"GPU{i + 1} memory usage", 0);
            if (used > bestUsed) { best = i; bestUsed = used; }
        }
        Config.GpuName = snapshot.Gpus[best];
        Save();
    }

    private int GpuNumber(IReadOnlyList<string> gpus)
    {
        var index = -1;
        for (var i = 0; i < gpus.Count; i++) if (gpus[i] == Config.GpuName) index = i;
        return Math.Max(index, 0) + 1;
    }

    // Afterburner numbers the sensors ("GPU2 usage") only when the PC has several GPUs; with a single one (e.g. the ROG Ally)
    // they are plain "GPU usage".
    private static string Gs(MahmSnapshot s, int gpu, string suffix)
    {
        var numbered = $"GPU{gpu} {suffix}";
        return s.Gpus.Count <= 1 && !s.Values.ContainsKey(numbered) ? $"GPU {suffix}" : numbered;
    }

    private HashSet<string> AvailableKeys(MahmSnapshot? s, int gpu)
    {
        var keys = new HashSet<string>();
        if (s is null) return keys;

        void Add(string key, string sensor) { if (s.Values.ContainsKey(sensor)) keys.Add(key); }
        Add(MetricKeys.GpuTemp, Gs(s, gpu, "temperature"));
        Add(MetricKeys.GpuUse, Gs(s, gpu, "usage"));
        Add(MetricKeys.GpuClock, Gs(s, gpu, "core clock"));
        Add(MetricKeys.GpuVram, Gs(s, gpu, "memory usage"));
        Add(MetricKeys.GpuPower, Gs(s, gpu, "power"));
        Add(MetricKeys.GpuVolt, Gs(s, gpu, "voltage"));
        Add(MetricKeys.CpuTemp, "CPU temperature");
        Add(MetricKeys.CpuUse, "CPU usage");
        Add(MetricKeys.CpuClock, "CPU clock");
        Add(MetricKeys.CpuPower, "CPU power");
        Add(MetricKeys.CpuVolt, "CPU voltage");
        Add(MetricKeys.RamUse, "RAM usage");
        if (RamInfoProvider.ConfiguredSpeed is not null) keys.Add(MetricKeys.RamClock);
        return keys;
    }

    // Keeps a rolling one-minute FPS average that restarts when another game takes over.
    private void TrackFps(GameFrameInfo? game)
    {
        if (game is null || game.Value.ProcessId != _trackedPid)
        {
            _fpsHistory.Clear();
            _trackedPid = game?.ProcessId ?? 0;
        }
        if (game is null) return;
        _fpsHistory.Enqueue(game.Value.Fps);
        while (_fpsHistory.Count > 120) _fpsHistory.Dequeue();
    }

    private (string Text, byte[]? Objects) BuildOsd(MahmSnapshot s, int gpu, GameFrameInfo? game)
    {
        var c = Config;
        var sb = new StringBuilder();

        // Sticky position on the 3x3 grid (layer 0), then the size in percent (S=0 would hide the text).
        sb.Append($"<P{c.Position}><L0><S={c.TextScale}>");

        static string Tag(string hex) => $"<C={hex}>";
        double V(string sensor) => s.Values.GetValueOrDefault(sensor, float.NaN);
        void Line(string group, string label, IEnumerable<string?> parts)
        {
            var shown = parts.Where(p => p is not null).ToList();
            if (shown.Count == 0) return;
            sb.Append(Tag(c.LabelColor(group))).Append(label).Append(Tag(c.ValueColor(group))).Append(' ').Append(string.Join("  ", shown)).Append('\n');
        }
        string? Fmt(string key, string sensor, string format) =>
            c.Has(key) && s.Values.TryGetValue(sensor, out var v) ? string.Format(System.Globalization.CultureInfo.CurrentCulture, format, v) : null;
        string? Vram(string sensor) =>
            c.Has(MetricKeys.GpuVram) && s.Values.TryGetValue(sensor, out var mb)
                ? $"{mb:0} MB" : null;

        Line(ColorGroups.Gpu, "GPU", [
            Fmt(MetricKeys.GpuTemp, Gs(s, gpu, "temperature"), "{0:0}°C"),
            Fmt(MetricKeys.GpuUse, Gs(s, gpu, "usage"), "{0:0}%"),
            Fmt(MetricKeys.GpuClock, Gs(s, gpu, "core clock"), "{0:0} MHz"),
            Vram(Gs(s, gpu, "memory usage")),
            Fmt(MetricKeys.GpuPower, Gs(s, gpu, "power"), "{0:0} W"),
            Fmt(MetricKeys.GpuVolt, Gs(s, gpu, "voltage"), "{0:0.000} V"),
        ]);
        Line(ColorGroups.Cpu, "CPU", [
            Fmt(MetricKeys.CpuTemp, "CPU temperature", "{0:0}°C"),
            Fmt(MetricKeys.CpuUse, "CPU usage", "{0:0}%"),
            Fmt(MetricKeys.CpuClock, "CPU clock", "{0:0} MHz"),
            Fmt(MetricKeys.CpuPower, "CPU power", "{0:0} W"),
            Fmt(MetricKeys.CpuVolt, "CPU voltage", "{0:0.000} V"),
        ]);
        Line(ColorGroups.Ram, "RAM", [
            c.Has(MetricKeys.RamUse) && s.Values.TryGetValue("RAM usage", out var ram) ? $"{ram:0} MB" : null,
            c.Has(MetricKeys.RamClock) && RamInfoProvider.ConfiguredSpeed is { } speed ? $"{speed} MT/s" : null,
        ]);

        // FPS and frametime come from RTSS tags (always live); graphs are RTSS objects that fill themselves.
        var objects = new List<byte[]>();
        var offset = 0;
        string Graph(uint flags, float max)
        {
            var obj = GraphObject(flags, max, c.GraphWidth);
            var tag = $"<OBJ={offset:X8}>";
            objects.Add(obj);
            offset += obj.Length;
            return tag;
        }

        var avg = _fpsHistory.Count > 0 ? _fpsHistory.Average() : 0;
        var supportsGraphs = _rtss.SupportsGraphs;
        if (c.Has(MetricKeys.Fps) || c.Has(MetricKeys.FpsAvg))
        {
            sb.Append(Tag(c.LabelColor(ColorGroups.Fps))).Append("FPS").Append(Tag(c.ValueColor(ColorGroups.Fps))).Append(' ');
            if (c.Has(MetricKeys.Fps))
            {
                if (c.Has(MetricKeys.FpsGraph) && supportsGraphs) sb.Append(Graph(0x2 /* framerate */, (float)Math.Max(60, Math.Ceiling(avg * 1.5 / 10) * 10))).Append(' ');
                sb.Append("<FR>");
            }
            if (c.Has(MetricKeys.FpsAvg)) sb.Append(c.Has(MetricKeys.Fps) ? "  " : "").Append($"AVG {avg:0}");
            sb.Append('\n');
        }
        if (c.Has(MetricKeys.FrameTime))
        {
            sb.Append(Tag(c.LabelColor(ColorGroups.FrameTime))).Append("FT ").Append(Tag(c.ValueColor(ColorGroups.FrameTime)));
            if (c.Has(MetricKeys.FrameTimeGraph) && supportsGraphs)
                sb.Append(Graph(0x4 /* frametime */, avg > 0 ? (float)Math.Clamp(3_000_000 / avg, 16667, 100000) : 50000)).Append(' ');
            sb.Append("<FT> ms\n");
        }

        // RTSS ends its layout state per slot; restore defaults so other clients aren't affected.
        sb.Append("<S><C>");
        return (sb.ToString(), objects.Count > 0 ? objects.SelectMany(o => o).ToArray() : null);
    }

    // RTSS_EMBEDDED_OBJECT_GRAPH without samples: RTSS fills framerate/frametime graphs on its own.
    private static byte[] GraphObject(uint flags, float max, int width)
    {
        var buffer = new byte[36];
        using var w = new BinaryWriter(new MemoryStream(buffer));
        w.Write(0x47523030u); // 'GR00'
        w.Write(36u);         // size
        w.Write(-width);       // width (negative = in characters)
        w.Write(-2);          // height (in lines)
        w.Write(1);           // margin
        w.Write(flags);
        w.Write(0f);          // min
        w.Write(max);
        w.Write(0u);          // no data samples
        return buffer;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static int ForegroundProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return (int)pid;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _rtss.Dispose();
        _mahm.Dispose();
    }
}
