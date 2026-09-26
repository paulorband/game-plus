using System.Windows;
using GamePlus.Models;
using GamePlus.Services;
using GamePlus.Services.Monitoring;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GamePlus.Overlay;

public partial class OverlayWindow : Window
{
    private readonly LosslessScalingSettingsService _settingsService;
    private readonly LosslessScalingProcessService _processService;
    private readonly AppConfig _config;
    private readonly GamepadService _gamepad;
    private readonly MonitoringService _monitoring;
    private readonly AppSettingsService _appSettings;

    private enum Category { LosslessScaling, Monitoring, Settings }
    private Category _category;
    private string _gpuKey = "";
    private bool _openingMonitoring;
    private string _colorGroup = ColorGroups.Gpu;
    private bool _afterburnerInstalled = true;

    private LosslessScalingSettings _settings = null!;
    private int _profileIndex;
    private List<SettingRow> _rows = new();
    private bool _scaling;
    private bool _running;
    private bool _installed = true;
    private bool _opening;
    private int _countdown;
    private bool _cancelCountdown;

    private MasterState CurrentState() =>
        !_installed ? MasterState.NotInstalled
        : _opening ? MasterState.Opening
        : _countdown > 0 ? MasterState.Starting
        : _scaling ? MasterState.Scaling
        : _running ? MasterState.Idle
        : MasterState.Closed;

    public OverlayWindow(
        LosslessScalingSettingsService settingsService,
        LosslessScalingProcessService processService,
        AppConfig config,
        GamepadService gamepad,
        MonitoringService monitoring,
        AppSettingsService appSettings)
    {
        Services.Log.Info("Overlay ctor: before InitializeComponent");
        InitializeComponent();
        Services.Log.Info("Overlay ctor: InitializeComponent done");

        _settingsService = settingsService;
        _processService = processService;
        _config = config;
        _gamepad = gamepad;
        _monitoring = monitoring;
        _appSettings = appSettings;
        _appSettings.Changed += OnAppSettingsChanged;
        ApplyScale();
        BuildFeatures();
        _category = _features.First(f => IsVisible(f.Id)).Id;

        Loaded += (_, _) =>
        {
            Services.Log.Info("Overlay loaded");
            _scaling = InitialScaling || _processService.IsScaling;
            _running = _processService.IsRunning;
            _installed = _running || _processService.IsInstalled(_config);
            LoadFromDisk();
            PollScalingState();
        };
        ContentRendered += (_, _) => { Services.Log.Info("Overlay rendered"); GrabFocus(); };
        Deactivated += (_, _) => { if (!_closing && DateTime.UtcNow - _openedAt < TimeSpan.FromSeconds(2) && _grabAttempts++ < 3) GrabFocus(); };
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            _appSettings.Changed -= OnAppSettingsChanged;
            _gamepad.ButtonPressed -= OnGamepadButtonPressed;
            _gamepad.ButtonRepeated -= OnGamepadButtonRepeated;
            _gamepad.ButtonReleased -= OnGamepadButtonReleased;
        };
        _gamepad.ButtonPressed += OnGamepadButtonPressed;
        _gamepad.ButtonRepeated += OnGamepadButtonRepeated;
        _gamepad.ButtonReleased += OnGamepadButtonReleased;

        KeyDown += OnKeyDown;
    }

    private void LoadFromDisk()
    {
        try
        {
            _settings = _settingsService.Load();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.T("ls.load_error", ex.Message);
            _settings = new LosslessScalingSettings();
        }

        if (_settings.GameProfiles.Count == 0)
        {
            _settings.GameProfiles.Add(new LosslessScalingProfile());
        }

        _profileIndex = 0;
        RebuildRows();
    }

    private void RebuildRows()
    {
        _rows = _category switch
        {
            Category.Monitoring => BuildMonitoringRows(),
            Category.Settings => BuildSettingsRows(),
            _ => BuildLosslessRows(),
        };
        RowsList.ItemsSource = _rows;
        RowsList.SelectedIndex = 0;
        StatusText.Text = " ";
        UpdateChrome();
    }

    private List<SettingRow> BuildLosslessRows()
    {
        var profile = _settings.GameProfiles[_profileIndex];
        ProfileNameText.Text = profile.Title;
        ProfileCountText.Text = Loc.T("profile.count", _profileIndex + 1, _settings.GameProfiles.Count);

        bool adaptive() => profile.LSFG3Mode1 == "ADAPTIVE";

        _rows = new List<SettingRow>
        {
            new ActionRow("Lossless Scaling", ToggleScaling, CurrentState, () =>
            {
                if (profile.FrameGeneration == "Off") return [Loc.T("ls.pill.generation_off")];
                var pills = new List<string> { profile.FrameGeneration };
                pills.Add(adaptive() ? Loc.T("ls.pill.adaptive", profile.LSFG3Target) : $"{profile.LSFG3Multiplier}x");
                pills.Add($"Flow {profile.LSFGFlowScale}%");
                return pills;
            })
            { Countdown = () => _countdown },

            new EnumSettingRow(Loc.T("ls.algorithm"), ["Off", "LSFG2", "LSFG3"],
                () => profile.FrameGeneration, v => profile.FrameGeneration = v, [Loc.T("ls.algorithm.off"), "LSFG2", "LSFG3"])
                { Section = Loc.T("ls.section.framegen") },

            new EnumSettingRow(Loc.T("ls.mode"), ["FIXED", "ADAPTIVE"],
                () => profile.LSFG3Mode1, v => profile.LSFG3Mode1 = v, [Loc.T("ls.mode.fixed"), Loc.T("ls.mode.adaptive")]),

            new IntSettingRow(Loc.T("ls.multiplier"), () => profile.LSFG3Multiplier, v => profile.LSFG3Multiplier = v, 2, 4, 1, "x")
                { Hint = Loc.T("ls.multiplier.hint") },

            new IntSettingRow(Loc.T("ls.target_fps"), () => profile.LSFG3Target, v => profile.LSFG3Target = v, 20, 480, 10, " fps")
                { Hint = Loc.T("ls.target_fps.hint"), EnabledWhen = adaptive },

            new EnumSettingRow(Loc.T("ls.lsfg_size"), ["PERFORMANCE", "BALANCED"],
                () => profile.LSFGSize, v => profile.LSFGSize = v, [Loc.T("ls.lsfg_size.performance"), Loc.T("ls.lsfg_size.balanced")]),

            new IntSettingRow(Loc.T("ls.flow_scale"), () => profile.LSFGFlowScale, v => profile.LSFGFlowScale = v, 25, 100, 5, "%", slider: true),

            // Valores extraídos de LosslessScaling.dll (enums reais); gravar um valor fora deles impede o LS de abrir.
            new EnumSettingRow(Loc.T("ls.scaling_type"), ["Off", "LS1", "FSR", "NIS", "SGSR", "BicubicCAS", "Anime4K", "XBR", "SharpBilinear", "Integer", "Nearest"],
                () => profile.ScalingType, v => profile.ScalingType = v)
                { Section = Loc.T("ls.section.scaling") },

            new DoubleSettingRow(Loc.T("ls.scale_factor"), () => profile.ScaleFactor, v => profile.ScaleFactor = v, 1.0, 3.0, 0.05),

            new IntSettingRow(Loc.T("ls.sharpness"), () => profile.Sharpness, v => profile.Sharpness = v, 0, 10, 1, slider: true),

            new EnumSettingRow(Loc.T("ls.sync"), ["OFF", "DEFAULT", "VSYNC1", "VSYNC2", "VSYNC3", "VSYNC4"],
                () => profile.SyncMode, v => profile.SyncMode = v)
                { Section = Loc.T("ls.section.display") },

            new BoolSettingRow(Loc.T("ls.gsync"), () => profile.GsyncSupport, v => profile.GsyncSupport = v),
            new BoolSettingRow(Loc.T("ls.hdr"), () => profile.HdrSupport, v => profile.HdrSupport = v),
            new BoolSettingRow(Loc.T("ls.show_fps"), () => profile.DrawFps, v => profile.DrawFps = v),
        };

        return _rows;
    }

    private List<SettingRow> BuildMonitoringRows()
    {
        var cfg = _monitoring.Config;
        var gpus = _monitoring.Status.Gpus;
        _gpuKey = string.Join('|', gpus);
        _afterburnerInstalled = AfterburnerLauncher.IsInstalled();

        void Changed() => _monitoring.Save();

        string? Why(string key) =>
            !_monitoring.Status.Afterburner ? Loc.T("mon.why.open_ab")
            : !_monitoring.IsAvailable(key) ? Loc.T("mon.why.hardware")
            : null;

        BoolSettingRow Metric(string label, string key, string? section = null) => new(label,
            () => cfg.Has(key) && _monitoring.IsAvailable(key), on => { cfg.Set(key, on); Changed(); })
        {
            Section = section,
            EnabledWhen = () => _monitoring.IsAvailable(key),
            HintWhen = () => Why(key),
        };

        // Graphs only make sense while the value they draw is on.
        BoolSettingRow Graph(string label, string key, string parentKey, string parentLabel) => new(label,
            () => cfg.Has(key), on => { cfg.Set(key, on); Changed(); })
        {
            EnabledWhen = () => cfg.Has(parentKey),
            HintWhen = () => cfg.Has(parentKey) ? null : Loc.T("mon.turn_on_first", parentLabel),
        };

        var gpuNames = gpus.Count > 0 ? gpus.ToArray() : ["—"];
        var rows = new List<SettingRow>
        {
            new MonitoringMasterRow(Loc.T("mon.master"), cfg, () => _monitoring.Status, () => _monitoring.ActiveMetricCount, () => { Changed(); RefreshAll(); },
                () => _afterburnerInstalled || _monitoring.Status.Afterburner, () => _openingMonitoring, () => _ = OpenMonitoringAsync()),

            new EnumSettingRow(Loc.T("mon.gpu"), gpuNames, () => cfg.GpuName, v => { cfg.GpuName = v; Changed(); },
                gpuNames.Select(ShortGpuName).ToArray(), forceStepper: true)
            {
                Section = Loc.T("mon.section.gpu"),
                EnabledWhen = () => gpus.Count > 1,
                HintWhen = () => gpus.Count > 1 ? null : Loc.T(gpus.Count == 1 ? "mon.gpu.single" : "mon.gpu.open_ab"),
            },
            Metric(Loc.T("mon.temperature"), MetricKeys.GpuTemp),
            Metric(Loc.T("mon.usage"), MetricKeys.GpuUse),
            Metric(Loc.T("mon.frequency"), MetricKeys.GpuClock),
            Metric(Loc.T("mon.vram"), MetricKeys.GpuVram),
            Metric(Loc.T("mon.power"), MetricKeys.GpuPower),
            Metric(Loc.T("mon.voltage"), MetricKeys.GpuVolt),

            Metric(Loc.T("mon.temperature"), MetricKeys.CpuTemp, Loc.T("mon.section.cpu")),
            Metric(Loc.T("mon.usage"), MetricKeys.CpuUse),
            Metric(Loc.T("mon.frequency"), MetricKeys.CpuClock),
            Metric(Loc.T("mon.power"), MetricKeys.CpuPower),
            Metric(Loc.T("mon.voltage"), MetricKeys.CpuVolt),

            Metric(Loc.T("mon.usage"), MetricKeys.RamUse, Loc.T("mon.section.ram")),
            Metric(Loc.T("mon.frequency"), MetricKeys.RamClock),

            new BoolSettingRow(Loc.T("mon.fps"), () => cfg.Has(MetricKeys.Fps), on => { cfg.Set(MetricKeys.Fps, on); Changed(); }) { Section = Loc.T("mon.section.fps") },
            Graph(Loc.T("mon.fps_graph"), MetricKeys.FpsGraph, MetricKeys.Fps, Loc.T("mon.fps")),
            new BoolSettingRow(Loc.T("mon.average"), () => cfg.Has(MetricKeys.FpsAvg), on => { cfg.Set(MetricKeys.FpsAvg, on); Changed(); }),
            new BoolSettingRow(Loc.T("mon.frametime"), () => cfg.Has(MetricKeys.FrameTime), on => { cfg.Set(MetricKeys.FrameTime, on); Changed(); }),
            Graph(Loc.T("mon.frametime_graph"), MetricKeys.FrameTimeGraph, MetricKeys.FrameTime, Loc.T("mon.frametime")),
            new IntSettingRow(Loc.T("mon.graph_width"), () => cfg.GraphWidth, v => { cfg.GraphWidth = v; Changed(); }, 8, 48, 4, "", slider: true)
            {
                EnabledWhen = () => cfg.Has(MetricKeys.FpsGraph) || cfg.Has(MetricKeys.FrameTimeGraph),
                HintWhen = () => cfg.Has(MetricKeys.FpsGraph) || cfg.Has(MetricKeys.FrameTimeGraph) ? null : Loc.T("mon.graph_first"),
            },

            new PositionRow(Loc.T("mon.position"), () => cfg.Position, v => { cfg.Position = v; Changed(); }) { Section = Loc.T("section.appearance") },
            new IntSettingRow(Loc.T("mon.text_size"), () => cfg.TextScale, v => { cfg.TextScale = v; Changed(); }, 50, 150, 10, "%", slider: true),
        };

        // Preset stepper plus R/G/B sliders: the sliders reach any color, the stepper jumps to a named one.
        IEnumerable<SettingRow> ColorRows(string label, Func<string> get, Action<string> set, string? section)
        {
            var keys = ColorPresets.All.Select(p => p.Key).ToArray();
            var names = keys.Select(k => Loc.T($"preset.{k}")).ToArray();
            yield return new EnumSettingRow(label, keys, () => ColorPresets.NameOf(get()) ?? Loc.T("preset.custom"),
                key =>
                {
                    var preset = ColorPresets.All.FirstOrDefault(p => p.Key == key);
                    if (preset.Hex is not null) set(preset.Hex);
                }, names, forceStepper: true)
            {
                Section = section,
                SwatchHex = get,
                HintWhen = () => $"#{get()}",
            };
            yield return Channel(Loc.T("mon.red"), 0);
            yield return Channel(Loc.T("mon.green"), 1);
            yield return Channel(Loc.T("mon.blue"), 2);

            IntSettingRow Channel(string name, int index) => new(name,
                () => index switch { 0 => ColorPresets.Split(get()).R, 1 => ColorPresets.Split(get()).G, _ => ColorPresets.Split(get()).B },
                v =>
                {
                    var (r, g, b) = ColorPresets.Split(get());
                    set(index switch { 0 => ColorPresets.Join(v, g, b), 1 => ColorPresets.Join(r, v, b), _ => ColorPresets.Join(r, g, v) });
                }, 0, 255, 15, "", slider: true);
        }

        // One group at a time keeps the screen short: pick the group, then edit its label and value colors.
        rows.Add(new EnumSettingRow(Loc.T("mon.color_group"), ColorGroups.All.Select(g => g.Key).ToArray(), () => _colorGroup, v => _colorGroup = v,
            ColorGroups.All.Select(g => g.Name).ToArray(), forceStepper: true) { Section = Loc.T("mon.section.colors") });
        rows.AddRange(ColorRows(Loc.T("mon.color.label"), () => cfg.LabelColor(_colorGroup), v => { cfg.SetColors(_colorGroup, label: v); Changed(); }, null));
        rows.AddRange(ColorRows(Loc.T("mon.color.value"), () => cfg.ValueColor(_colorGroup), v => { cfg.SetColors(_colorGroup, value: v); Changed(); }, null));
        return _rows = rows;
    }

    private List<SettingRow> BuildSettingsRows()
    {
        var s = _appSettings.Current;
        var languages = Loc.Instance.Available;
        return
        [
            new EnumSettingRow(Loc.T("set.theme"), [ThemeManager.Dark, ThemeManager.Light], () => ThemeManager.Normalize(s.Theme),
                v => { s.Theme = v; _appSettings.Save(); }, [Loc.T("set.theme.dark"), Loc.T("set.theme.light")]) { Section = Loc.T("section.appearance") },
            new IntSettingRow(Loc.T("set.opacity"), () => s.PanelOpacity, v => { s.PanelOpacity = v; _appSettings.Save(); }, 50, 100, 5, "%", slider: true)
                { Hint = Loc.T("set.opacity.hint") },
            new IntSettingRow(Loc.T("set.scale"), () => s.UiScale, v => { s.UiScale = v; _appSettings.Save(); }, 75, 150, 5, "%", slider: true)
                { Hint = Loc.T("set.scale.hint") },
            new EnumSettingRow(Loc.T("set.language"), languages.Select(l => l.Code).ToArray(), () => Loc.Instance.Code,
                v => { s.Language = v; _appSettings.Save(); }, languages.Select(l => l.Name).ToArray(), forceStepper: true),

            .. FeatureVisibilityRows(),

            new BoolSettingRow(Loc.T("set.start_with_windows"), StartupRegistration.IsEnabled, StartupRegistration.SetEnabled) { Section = Loc.T("set.section.startup") },
            new EnumSettingRow(Loc.T("set.start_mode"), ["window", "tray"], () => s.StartInTray ? "tray" : "window",
                v => { s.StartInTray = v == "tray"; _appSettings.Save(); }, [Loc.T("set.start_mode.window"), Loc.T("set.start_mode.tray")]),
            new BoolSettingRow(Loc.T("set.tray_click"), () => s.TrayClickOpens, on => { s.TrayClickOpens = on; _appSettings.Save(); })
                { Hint = Loc.T("set.tray_click.hint") },
        ];
    }

    // One toggle per hideable feature, generated from the registry (so new features show up here on their own).
    private IEnumerable<SettingRow> FeatureVisibilityRows()
    {
        var hideable = _features.Where(f => f.Hideable).ToList();
        var first = true;
        foreach (var feature in hideable)
        {
            var f = feature;
            var lastOne = () => IsVisible(f.Id) && hideable.Count(h => IsVisible(h.Id)) <= 1;
            yield return new BoolSettingRow(Loc.T(f.NameKey), () => IsVisible(f.Id), on => SetFeatureVisible(f, on))
            {
                Section = first ? Loc.T("set.section.sidebar") : null,
                Hint = first ? Loc.T("set.sidebar.hint") : null,
                EnabledWhen = () => !lastOne(),
                HintWhen = () => lastOne() ? Loc.T("set.sidebar.last") : null,
            };
            first = false;
        }
    }

    // "NVIDIA GeForce RTX 5060 Ti" -> "RTX 5060 Ti": the stepper only has room for a short name.
    private static string ShortGpuName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"NVIDIA |GeForce |AMD |\(TM\)|\(R\)", "").Trim();

    // Afterburner (and RTSS) are closed: start them with visible feedback, then switch the display on.
    private async Task OpenMonitoringAsync()
    {
        if (_openingMonitoring) return;
        _openingMonitoring = true;
        _rows.FirstOrDefault()?.Refresh();

        var started = await Task.Run(() => AfterburnerLauncher.StartMissing(_monitoring.Status.Afterburner, _monitoring.Status.Rtss));
        var ready = false;
        var rtssTried = _monitoring.Status.Afterburner;
        if (started)
        {
            // Afterburner brings RTSS up itself; give it time, and start RTSS directly if it never follows.
            for (var i = 0; i < 80 && !_closing; i++)
            {
                if (_monitoring.Status.Ready) { ready = true; break; }
                if (i == 30 && _monitoring.Status.Afterburner && !_monitoring.Status.Rtss && !rtssTried)
                {
                    rtssTried = true;
                    await Task.Run(AfterburnerLauncher.StartRtss);
                }
                await Task.Delay(500);
            }
            // Sensors settle a moment after the shared memory appears.
            if (ready) await Task.Delay(1000);
        }
        if (_closing) return;

        _openingMonitoring = false;
        if (ready)
        {
            _monitoring.Config.Enabled = true;
            _monitoring.Save();
            StatusText.Text = Loc.T("mon.enabled");
        }
        else
        {
            StatusText.Text = Loc.T("mon.open_failed");
        }
        RefreshAll();
    }

    private void ChangeCategory(int delta)
    {
        // Hidden features are skipped, so LB/RB only walks what the rail shows.
        var shown = _features.Where(f => IsVisible(f.Id)).Select(f => f.Id).ToList();
        var at = Math.Max(shown.IndexOf(_category), 0);
        SetCategory(shown[((at + delta) % shown.Count + shown.Count) % shown.Count]);
    }

    private void SetCategory(Category category)
    {
        if (category == _category || !IsVisible(category)) return;
        _category = category;
        RebuildRows();
    }

    // Runs after App has applied the new theme; the rail items are styled from code, so they need a nudge.
    private void OnAppSettingsChanged()
    {
        UpdateChrome();
        if (_appSettings.Current.UiScale != _appliedScale) ApplyScale();
        // Every row label was built with the old language: rebuild them, keeping the row the user is on.
        if (Loc.Instance.Code != _appliedLanguage) RebuildRowsKeepingSelection();
    }

    private string _appliedLanguage = Loc.Instance.Code;

    private void RebuildRowsKeepingSelection()
    {
        _appliedLanguage = Loc.Instance.Code;
        var selected = Math.Max(RowsList.SelectedIndex, 0);
        var status = StatusText.Text;
        RebuildRows();
        RowsList.SelectedIndex = Math.Min(selected, _rows.Count - 1);
        StatusText.Text = status;
    }

    private const double BaseWidth = 920, BaseHeight = 580;
    private int _appliedScale;

    // Scales the whole panel and keeps it centered. Never larger than the work area (a 720p handheld at 150% would clip it).
    private void ApplyScale()
    {
        _appliedScale = _appSettings.Current.UiScale;
        var area = SystemParameters.WorkArea;
        var fit = Math.Min((area.Width - 24) / BaseWidth, (area.Height - 24) / BaseHeight);
        var scale = Math.Max(0.5, Math.Min(_appliedScale / 100.0, fit));
        RootScale.ScaleX = RootScale.ScaleY = scale;
        Left = area.Left + (area.Width - BaseWidth * scale) / 2;
        Top = area.Top + (area.Height - BaseHeight * scale) / 2;
    }

    // Everything outside the row list that depends on the active category.
    private void UpdateChrome()
    {
        var ls = _category == Category.LosslessScaling;
        foreach (var feature in _features)
        {
            var (item, _) = _rail[feature.Id];
            item.Visibility = IsVisible(feature.Id) ? Visibility.Visible : Visibility.Collapsed;
            StyleRailItem(item, feature.Id == _category);
        }
        UpdateDots();

        var lsOnly = ls ? Visibility.Visible : Visibility.Collapsed;
        ProfileChip.Visibility = lsOnly;
        ProfileHint.Visibility = lsOnly;
        RestartHint.Visibility = lsOnly;
        SaveHint.Visibility = lsOnly;
    }

    // ---- Feature registry -------------------------------------------------------------------------------------------
    // Everything the sidebar and the "Sidebar items" settings are generated from. A new feature = a Category value,
    // one entry here and a row builder in RebuildRows; the rail, the show/hide setting and the "keep one visible" rule follow.

    /// <param name="Active">Whether the feature is doing something right now (the dot next to its name).</param>
    /// <param name="Deactivate">Turns the feature off; runs when it is hidden and again when it is shown, so it always starts off.</param>
    private sealed record Feature(Category Id, string NameKey, string Icon, Func<bool>? Active, Action? Deactivate, bool Hideable = true);

    private List<Feature> _features = null!;
    private readonly Dictionary<Category, (System.Windows.Controls.Border Item, System.Windows.Shapes.Ellipse? Dot)> _rail = new();

    private void BuildFeatures()
    {
        _features =
        [
            new(Category.LosslessScaling, "cat.lossless", "M11,2 L4,11 L9,11 L8,18 L16,8 L11,8 Z", () => _scaling, DeactivateScaling),
            new(Category.Monitoring, "cat.monitoring", "M3,14 A7,7 0 1 1 17,14 M10,14 L13,9", () => _monitoring.Config.Enabled, DeactivateMonitoring),
            new(Category.Settings, "cat.settings", "M3,5 H17 M3,10 H17 M3,15 H17 M7,3 V7 M13,8 V12 M6,13 V17", null, null, Hideable: false),
        ];

        // Never leave the rail with nothing to show (a hand-edited settings.json could hide everything).
        var hidden = _appSettings.Current.HiddenFeatures;
        if (_features.Where(f => f.Hideable).All(f => hidden.Contains(f.Id.ToString()))) hidden.Clear();

        foreach (var feature in _features)
        {
            var dock = new System.Windows.Controls.DockPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var icon = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(feature.Icon), StrokeThickness = 1.7, Width = 20, Height = 20,
                Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0),
                StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            };
            System.Windows.Controls.DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
            dock.Children.Add(icon);

            System.Windows.Shapes.Ellipse? dot = null;
            if (feature.Active is not null)
            {
                dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Visibility = Visibility.Hidden };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
                System.Windows.Controls.DockPanel.SetDock(dot, System.Windows.Controls.Dock.Right);
                dock.Children.Add(dot);
            }

            var label = new System.Windows.Controls.TextBlock { FontSize = 15, FontWeight = FontWeights.Medium, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            label.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding($"[{feature.NameKey}]") { Source = Loc.Instance });
            dock.Children.Add(label);

            var item = new System.Windows.Controls.Border
            {
                Height = 48, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 4),
                Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent, Child = dock,
            };
            var id = feature.Id;
            item.MouseLeftButtonDown += (_, _) => SetCategory(id);
            RailItems.Children.Add(item);
            _rail[feature.Id] = (item, dot);
        }
    }

    private bool IsVisible(Category id) =>
        _features.FirstOrDefault(f => f.Id == id) is { } f && (!f.Hideable || !_appSettings.Current.HiddenFeatures.Contains(id.ToString()));

    private void UpdateDots()
    {
        foreach (var feature in _features)
            if (_rail[feature.Id].Dot is { } dot && feature.Active is not null)
                dot.Visibility = feature.Active() ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetFeatureVisible(Feature feature, bool visible)
    {
        var hidden = _appSettings.Current.HiddenFeatures;
        var id = feature.Id.ToString();
        if (!visible && _features.Count(f => f.Hideable && IsVisible(f.Id)) <= 1) return;   // at least one stays
        if (visible) hidden.Remove(id); else if (!hidden.Contains(id)) hidden.Add(id);
        feature.Deactivate?.Invoke();   // hiding switches it off; showing it again must not resurrect an old "on"
        _appSettings.Save();
    }

    private void DeactivateMonitoring()
    {
        if (!_monitoring.Config.Enabled) return;
        _monitoring.Config.Enabled = false;
        _monitoring.Save();
    }

    private void DeactivateScaling()
    {
        if (_countdown > 0) _cancelCountdown = true;
        if (!_scaling) return;
        var (hotkey, modifiers) = ReadHotkey();
        // The hotkey is global, so it works while the overlay has focus (same as turning it off from the master card).
        Task.Run(() => HotkeyInputSender.SendHotkey(hotkey, modifiers));
        _scaling = false;
    }

    private (System.Windows.Forms.Keys Hotkey, System.Windows.Forms.Keys[] Modifiers) ReadHotkey()
    {
        try
        {
            return LosslessScalingSettingsService.ParseHotkey(_settingsService.Load());
        }
        catch (Exception)
        {
            // Settings unreadable: fall back to LS's default F12.
            return (System.Windows.Forms.Keys.F12, Array.Empty<System.Windows.Forms.Keys>());
        }
    }

    private void StyleRailItem(System.Windows.Controls.Border item, bool active)
    {
        item.Background = active ? (System.Windows.Media.Brush)FindResource("Surface3Brush") : System.Windows.Media.Brushes.Transparent;
        foreach (var child in ((System.Windows.Controls.DockPanel)item.Child).Children)
        {
            if (child is System.Windows.Controls.TextBlock text)
                text.Foreground = (System.Windows.Media.Brush)FindResource(active ? "TextBrush" : "Text2Brush");
            else if (child is System.Windows.Shapes.Path icon)
                icon.Stroke = (System.Windows.Media.Brush)FindResource(active ? "AccentBrush" : "Text2Brush");
        }
    }

    private void RefreshAll()
    {
        foreach (var row in _rows) row.Refresh();
        UpdateDots();
    }

    // Moves focus to the next enabled row in the given direction, wrapping past either end (stays put if none).
    private void MoveFocus(int direction)
    {
        var count = _rows.Count;
        var i = RowsList.SelectedIndex < 0 ? 0 : RowsList.SelectedIndex;
        for (var step = 1; step <= count; step++)
        {
            var next = ((i + direction * step) % count + count) % count;
            if (!_rows[next].Available) continue;
            RowsList.SelectedIndex = next;
            RowsList.ScrollIntoView(_rows[next]);
            return;
        }
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RowsList.SelectedItem is not SettingRow row) return;
        HintAText.Text = Loc.T(row.IsMaster ? (_category == Category.LosslessScaling ? "hint.toggle_ls" : "hint.toggle")
            : row.IsToggle ? "hint.toggle"
            : row.IsPosition ? "hint.corner"
            : "hint.next_value");
    }

    private void OnSegmentClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not OptionItem option) return;
        option.Select();
        RefreshAll();
    }

    private void OnSliderMouse(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var track = (FrameworkElement)sender;
        if (track.DataContext is not SettingRow row || !row.Available) return;
        RowsList.SelectedItem = row;
        row.SetFraction(e.GetPosition(track).X / Math.Max(1, track.ActualWidth));
        RefreshAll();
    }

    private bool _closing;
    private readonly DateTime _openedAt = DateTime.UtcNow;
    private int _grabAttempts;

    // While open, the overlay must own the foreground so games that ignore input when unfocused stop reacting to the pad.
    private void GrabFocus()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Activate();
        HotkeyInputSender.ForceForeground(hwnd);
        LogState(hwnd);
    }

    private void LogState(IntPtr hwnd)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var wa = SystemParameters.WorkArea;
        Services.Log.Info(
            $"State: visible={IsVisible} active={IsActive} topmost={Topmost} state={WindowState} pos={Left:0},{Top:0} size={ActualWidth:0}x{ActualHeight:0} opacity={Opacity} | " +
            $"self={HotkeyInputSender.Describe(hwnd)} | foreground={HotkeyInputSender.Describe(HotkeyInputSender.GetForegroundWindow())} | " +
            $"game={HotkeyInputSender.Describe(PreviousForegroundWindow)} | dpi={dpi.PixelsPerDip:0.##} workArea(DIP)={wa.Width:0}x{wa.Height:0} " +
            $"primary(px)={SystemParameters.PrimaryScreenWidth * dpi.PixelsPerDip:0}x{SystemParameters.PrimaryScreenHeight * dpi.PixelsPerDip:0} " +
            $"renderTier={System.Windows.Media.RenderCapability.Tier >> 16}");
    }

    private void OnDragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase)        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => Handle(GamepadButton.Y);

    private void OnRestartClick(object sender, RoutedEventArgs e) => Handle(GamepadButton.X);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnDecrementClick(object sender, RoutedEventArgs e) => ChangeRow(sender, -1);

    private void OnIncrementClick(object sender, RoutedEventArgs e) => ChangeRow(sender, 1);

    private void ChangeRow(object sender, int delta)
    {
        if (((FrameworkElement)sender).DataContext is not SettingRow row) return;
        RowsList.SelectedItem = row;
        if (!row.Available) return;
        if (delta < 0) row.Decrement(); else row.Increment();
        RefreshAll();
    }

    private void OnGamepadButtonPressed(GamepadButton button)
    {
        if (_closePending) return;
        Dispatcher.Invoke(() =>
        {
            if (button is GamepadButton.DPadLeft or GamepadButton.DPadRight) HorizontalPress(button);
            else Handle(button);
        });
    }

    // Held vertical direction keeps walking the rows; held horizontal keeps previewing values (applied on release).
    private void OnGamepadButtonRepeated(GamepadButton button)
    {
        if (_closePending) return;
        Dispatcher.Invoke(() =>
        {
            if (button is GamepadButton.DPadUp or GamepadButton.DPadDown) Handle(button);
            else if (_holdRow is not null && _holdRow == RowsList.SelectedItem)
            {
                _holdRow.Preview(button == GamepadButton.DPadLeft ? -1 : 1);
                RefreshAll();
            }
        });
    }

    private void OnGamepadButtonReleased(GamepadButton button)
    {
        if (button != _holdButton) return;
        Dispatcher.Invoke(CommitHold);
    }

    private SettingRow? _holdRow;
    private GamepadButton _holdButton;

    private void HorizontalPress(GamepadButton button)
    {
        CommitHold();
        if (_rows.Count == 0) return;
        var row = RowsList.SelectedItem as SettingRow ?? _rows[0];
        if (!row.Available) return;
        var delta = button == GamepadButton.DPadLeft ? -1 : 1;
        if (row.HoldToPreview)
        {
            _holdRow = row;
            _holdButton = button;
            row.Preview(delta);
        }
        else if (delta < 0) row.Decrement();
        else row.Increment();
        RefreshAll();
    }

    private void CommitHold()
    {
        var row = _holdRow;
        _holdRow = null;
        _holdButton = GamepadButton.None;
        if (row is null) return;
        row.Commit();
        RefreshAll();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Handle(GamepadButton.DPadUp); break;
            case Key.Down: Handle(GamepadButton.DPadDown); break;
            case Key.Left: Handle(GamepadButton.DPadLeft); break;
            case Key.Right: Handle(GamepadButton.DPadRight); break;
            case Key.Enter: Handle(GamepadButton.A); break;
            case Key.Escape: Handle(GamepadButton.B); break;
            case Key.S: Handle(GamepadButton.Y); break;
            case Key.PageUp: Handle(GamepadButton.LeftShoulder); break;
            case Key.PageDown: Handle(GamepadButton.RightShoulder); break;
            case Key.Q: Handle(GamepadButton.LeftTrigger); break;
            case Key.E: Handle(GamepadButton.RightTrigger); break;
        }
    }

    private void Handle(GamepadButton button)
    {
        if (_rows.Count == 0) return;
        var index = RowsList.SelectedIndex < 0 ? 0 : RowsList.SelectedIndex;

        switch (button)
        {
            case GamepadButton.DPadUp:
                MoveFocus(-1);
                break;

            case GamepadButton.DPadDown:
                MoveFocus(1);
                break;

            case GamepadButton.DPadLeft:
                if (_rows[index].Available) _rows[index].Decrement();
                RefreshAll();
                break;

            case GamepadButton.DPadRight:
            case GamepadButton.A:
                if (_rows[index].Available) _rows[index].Increment();
                RefreshAll();
                break;

            case GamepadButton.LeftShoulder:
                ChangeCategory(-1);
                break;

            case GamepadButton.RightShoulder:
                ChangeCategory(1);
                break;

            case GamepadButton.LeftTrigger:
                if (_category == Category.LosslessScaling) ChangeProfile(-1);
                break;

            case GamepadButton.RightTrigger:
                if (_category == Category.LosslessScaling) ChangeProfile(1);
                break;

            case GamepadButton.Y:
                if (_category == Category.LosslessScaling) SaveSettings();
                break;

            case GamepadButton.X:
                if (_category != Category.LosslessScaling) break;
                _processService.RestartIfRunning(_config);
                StatusText.Text = Loc.T("ls.restarted");
                break;

            case GamepadButton.B:
                _ = CloseAfterReleaseAsync();
                break;
        }
    }

    /// <summary>Foreground window (the game) captured before the overlay took focus.</summary>
    public IntPtr PreviousForegroundWindow { get; set; }

    /// <summary>Whether LS was scaling right before the overlay took focus.</summary>
    public bool InitialScaling { get; set; }

    private void ToggleScaling()
    {
        if (_opening) return;

        if (_countdown > 0)
        {
            _cancelCountdown = true;
            return;
        }

        if (!_installed)
        {
            LosslessScalingProcessService.OpenSteamStorePage();
            return;
        }

        var target = PreviousForegroundWindow;
        var (hotkey, modifiers) = ReadHotkey();

        if (_scaling)
        {
            // Turning off: the hotkey is global, so keep the overlay open and let the state poll show the result.
            Task.Run(() => HotkeyInputSender.SendHotkey(hotkey, modifiers));
            // LS hides its windows while the overlay has focus, so the poll can't confirm this; assume it worked.
            _scaling = false;
            _rows.FirstOrDefault()?.Refresh();
            UpdateDots();
            return;
        }

        if (!_running)
        {
            _ = OpenThenToggleAsync(target, hotkey, modifiers);
            return;
        }

        _ = CountdownThenToggleAsync(target, hotkey, modifiers);
    }

    // Gives the user 3 s of visible warning before frame generation starts (A cancels).
    private async Task CountdownThenToggleAsync(IntPtr target, System.Windows.Forms.Keys hotkey, System.Windows.Forms.Keys[] modifiers)
    {
        _cancelCountdown = false;
        for (var n = 3; n >= 1; n--)
        {
            _countdown = n;
            _rows.FirstOrDefault()?.Refresh();
            await Task.Delay(1000);
            if (_closing) return;
            if (_cancelCountdown)
            {
                _countdown = 0;
                _rows.FirstOrDefault()?.Refresh();
                return;
            }
        }
        _countdown = 0;
        CloseAndSendHotkey(target, hotkey, modifiers);
    }

    // LS is closed: start it, show "opening" feedback, wait until it's up, then follow the normal turn-on flow.
    private async Task OpenThenToggleAsync(IntPtr target, System.Windows.Forms.Keys hotkey, System.Windows.Forms.Keys[] modifiers)
    {
        _opening = true;
        _rows.FirstOrDefault()?.Refresh();

        var started = await Task.Run(() => _processService.Launch(_config));
        var up = false;
        if (started)
        {
            // Wait for the process, then give it a moment to register its hotkey.
            for (var i = 0; i < 40 && !_closing; i++)
            {
                if (await Task.Run(() => _processService.IsRunning)) { up = true; break; }
                await Task.Delay(500);
            }
            if (up) await Task.Delay(2000);
        }
        if (_closing) return;

        _opening = false;
        _running = up;
        _rows.FirstOrDefault()?.Refresh();

        if (!up)
        {
            StatusText.Text = Loc.T("ls.open_failed");
            return;
        }

        await CountdownThenToggleAsync(target, hotkey, modifiers);
    }

    /// <summary>Closes once the face buttons are let go: the game regains focus right away, and a button still
    /// held would otherwise reach it as a fresh press.</summary>
    public async Task CloseAfterReleaseAsync()
    {
        if (_closePending) return;
        _closePending = true;
        // We're usually called from inside a gamepad poll, before HeldButtons has been updated with this very press.
        await Task.Delay(60);
        const GamepadButton faceButtons = GamepadButton.A | GamepadButton.B | GamepadButton.X | GamepadButton.Y | GamepadButton.Start | GamepadButton.Back;
        for (var i = 0; i < 60 && (_gamepad.HeldButtons & faceButtons) != 0; i++) await Task.Delay(25);
        if (!_closing) Close();
    }

    private bool _closePending;

    private async void CloseAndSendHotkey(IntPtr target, System.Windows.Forms.Keys hotkey, System.Windows.Forms.Keys[] modifiers)
    {
        await CloseAfterReleaseAsync();

        // LS scales the foreground window, so hand focus back to the game before sending the key.
        Task.Run(async () =>
        {
            if (target != IntPtr.Zero) HotkeyInputSender.SetForegroundWindow(target);
            await Task.Delay(400);
            HotkeyInputSender.SendHotkey(hotkey, modifiers);
        });
    }
    // Polls (off the UI thread) whether LS is installed/running/scaling and refreshes the master card on changes.
    private async void PollScalingState()
    {
        var tick = 0;
        while (!_closing)
        {
            var (windows, running, installed) = await Task.Run(() =>
            {
                var r = _processService.IsRunning;
                // Install lookup scans drives and the registry, so only re-check it now and then while missing.
                var inst = r || (_installed ? _processService.IsInstalled(_config) : tick % 8 == 0 ? _processService.IsInstalled(_config) : false);
                return (r && _processService.IsScaling, r, inst);
            });
            if (_closing) return;
            // While the overlay has focus LS hides its scaling windows even though scaling continues, so their
            // absence proves nothing: only a visible window turns the state on, and LS exiting turns it off.
            var scaling = running && (windows || _scaling);
            if (scaling != _scaling || running != _running || installed != _installed)
            {
                _scaling = scaling;
                _running = running;
                _installed = installed;
                _rows.FirstOrDefault()?.Refresh();
                UpdateDots();
            }
            if (_category == Category.Monitoring) RefreshMonitoringRows();
            tick++;
            await Task.Delay(500);
        }
    }
    // Sensor availability and the game name change under us (Afterburner opened, game started): keep the rows current.
    private void RefreshMonitoringRows()
    {
        if (string.Join('|', _monitoring.Status.Gpus) != _gpuKey)
        {
            // The GPU list arrived or changed: rebuild the rows but keep the selection where it was.
            var selected = RowsList.SelectedIndex;
            _rows = BuildMonitoringRows();
            RowsList.ItemsSource = _rows;
            RowsList.SelectedIndex = Math.Min(Math.Max(selected, 0), _rows.Count - 1);
        }
        RefreshAll();
    }

    private void ChangeProfile(int delta)
    {
        if (_settings.GameProfiles.Count <= 1) return;
        _profileIndex = (_profileIndex + delta + _settings.GameProfiles.Count) % _settings.GameProfiles.Count;
        RebuildRows();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsService.Save(_settings);
            if (_config.AutoRestartOnSave && _processService.IsRunning)
            {
                _processService.RestartIfRunning(_config);
                StatusText.Text = Loc.T("ls.saved_restarted");
            }
            else
            {
                StatusText.Text = Loc.T("ls.saved");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.T("ls.save_error", ex.Message);
        }
    }
}
