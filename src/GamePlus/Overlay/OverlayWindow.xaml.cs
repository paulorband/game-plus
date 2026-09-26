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
            StatusText.Text = $"Erro ao carregar Settings.xml: {ex.Message}";
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
        ProfileCountText.Text = $"{_profileIndex + 1} de {_settings.GameProfiles.Count}";

        bool adaptive() => profile.LSFG3Mode1 == "ADAPTIVE";

        _rows = new List<SettingRow>
        {
            new ActionRow("Lossless Scaling", ToggleScaling, CurrentState, () =>
            {
                if (profile.FrameGeneration == "Off") return ["Geração desligada"];
                var pills = new List<string> { profile.FrameGeneration };
                pills.Add(adaptive() ? $"Dinâmico {profile.LSFG3Target} fps" : $"{profile.LSFG3Multiplier}x");
                pills.Add($"Flow {profile.LSFGFlowScale}%");
                return pills;
            })
            { Countdown = () => _countdown },

            new EnumSettingRow("Algoritmo", ["Off", "LSFG2", "LSFG3"],
                () => profile.FrameGeneration, v => profile.FrameGeneration = v, ["Desligado", "LSFG2", "LSFG3"])
                { Section = "Geração de quadros" },

            new EnumSettingRow("Modo", ["FIXED", "ADAPTIVE"],
                () => profile.LSFG3Mode1, v => profile.LSFG3Mode1 = v, ["Fixo", "Dinâmico"]),

            new IntSettingRow("Multiplicador", () => profile.LSFG3Multiplier, v => profile.LSFG3Multiplier = v, 2, 4, 1, "x")
                { Hint = "Quadros gerados para cada quadro real" },

            new IntSettingRow("FPS alvo", () => profile.LSFG3Target, v => profile.LSFG3Target = v, 20, 480, 10, " fps")
                { Hint = "Disponível apenas no modo Dinâmico", EnabledWhen = adaptive },

            new EnumSettingRow("Tamanho do LSFG", ["PERFORMANCE", "BALANCED"],
                () => profile.LSFGSize, v => profile.LSFGSize = v, ["Desempenho", "Equilibrado"]),

            new IntSettingRow("Flow scale", () => profile.LSFGFlowScale, v => profile.LSFGFlowScale = v, 25, 100, 5, "%", slider: true),

            // Valores extraídos de LosslessScaling.dll (enums reais); gravar um valor fora deles impede o LS de abrir.
            new EnumSettingRow("Tipo de escalonamento", ["Off", "LS1", "FSR", "NIS", "SGSR", "BicubicCAS", "Anime4K", "XBR", "SharpBilinear", "Integer", "Nearest"],
                () => profile.ScalingType, v => profile.ScalingType = v)
                { Section = "Escala de imagem" },

            new DoubleSettingRow("Fator de escala", () => profile.ScaleFactor, v => profile.ScaleFactor = v, 1.0, 3.0, 0.05),

            new IntSettingRow("Nitidez", () => profile.Sharpness, v => profile.Sharpness = v, 0, 10, 1, slider: true),

            new EnumSettingRow("Sincronização", ["OFF", "DEFAULT", "VSYNC1", "VSYNC2", "VSYNC3", "VSYNC4"],
                () => profile.SyncMode, v => profile.SyncMode = v)
                { Section = "Exibição" },

            new BoolSettingRow("Suporte G-Sync/FreeSync", () => profile.GsyncSupport, v => profile.GsyncSupport = v),
            new BoolSettingRow("Suporte HDR", () => profile.HdrSupport, v => profile.HdrSupport = v),
            new BoolSettingRow("Mostrar FPS", () => profile.DrawFps, v => profile.DrawFps = v),
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
            !_monitoring.Status.Afterburner ? "Abra o MSI Afterburner para ler este sensor"
            : !_monitoring.IsAvailable(key) ? "Indisponível neste hardware"
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
            HintWhen = () => cfg.Has(parentKey) ? null : $"Ligue \"{parentLabel}\" primeiro",
        };

        var gpuNames = gpus.Count > 0 ? gpus.ToArray() : ["—"];
        var rows = new List<SettingRow>
        {
            new MonitoringMasterRow("Exibição do monitoramento", cfg, () => _monitoring.Status, () => _monitoring.ActiveMetricCount, () => { Changed(); RefreshAll(); },
                () => _afterburnerInstalled || _monitoring.Status.Afterburner, () => _openingMonitoring, () => _ = OpenMonitoringAsync()),

            new EnumSettingRow("GPU monitorada", gpuNames, () => cfg.GpuName, v => { cfg.GpuName = v; Changed(); },
                gpuNames.Select(ShortGpuName).ToArray(), forceStepper: true)
            {
                Section = "GPU",
                EnabledWhen = () => gpus.Count > 1,
                HintWhen = () => gpus.Count > 1 ? null : gpus.Count == 1 ? "Só há uma GPU neste PC" : "Abra o MSI Afterburner para listar as GPUs",
            },
            Metric("Temperatura", MetricKeys.GpuTemp),
            Metric("Uso", MetricKeys.GpuUse),
            Metric("Frequência", MetricKeys.GpuClock),
            Metric("Uso de VRAM", MetricKeys.GpuVram),
            Metric("Consumo", MetricKeys.GpuPower),
            Metric("Tensão", MetricKeys.GpuVolt),

            Metric("Temperatura", MetricKeys.CpuTemp, "CPU"),
            Metric("Uso", MetricKeys.CpuUse),
            Metric("Frequência", MetricKeys.CpuClock),
            Metric("Consumo", MetricKeys.CpuPower),
            Metric("Tensão", MetricKeys.CpuVolt),

            Metric("Uso", MetricKeys.RamUse, "RAM"),
            Metric("Frequência", MetricKeys.RamClock),

            new BoolSettingRow("FPS atual", () => cfg.Has(MetricKeys.Fps), on => { cfg.Set(MetricKeys.Fps, on); Changed(); }) { Section = "FPS" },
            Graph("FPS com gráfico", MetricKeys.FpsGraph, MetricKeys.Fps, "FPS atual"),
            new BoolSettingRow("Média (AVG)", () => cfg.Has(MetricKeys.FpsAvg), on => { cfg.Set(MetricKeys.FpsAvg, on); Changed(); }),
            new BoolSettingRow("Frametime", () => cfg.Has(MetricKeys.FrameTime), on => { cfg.Set(MetricKeys.FrameTime, on); Changed(); }),
            Graph("Frametime com gráfico", MetricKeys.FrameTimeGraph, MetricKeys.FrameTime, "Frametime"),
            new IntSettingRow("Largura dos gráficos", () => cfg.GraphWidth, v => { cfg.GraphWidth = v; Changed(); }, 8, 48, 4, "", slider: true)
            {
                EnabledWhen = () => cfg.Has(MetricKeys.FpsGraph) || cfg.Has(MetricKeys.FrameTimeGraph),
                HintWhen = () => cfg.Has(MetricKeys.FpsGraph) || cfg.Has(MetricKeys.FrameTimeGraph) ? null : "Ligue um gráfico primeiro",
            },

            new PositionRow("Posição na tela", () => cfg.Position, v => { cfg.Position = v; Changed(); }) { Section = "Aparência" },
            new IntSettingRow("Tamanho do texto", () => cfg.TextScale, v => { cfg.TextScale = v; Changed(); }, 50, 150, 10, "%", slider: true),
        };

        // Preset stepper plus R/G/B sliders: the sliders reach any color, the stepper jumps to a named one.
        IEnumerable<SettingRow> ColorRows(string label, Func<string> get, Action<string> set, string? section)
        {
            var names = ColorPresets.All.Select(p => p.Name).ToArray();
            yield return new EnumSettingRow(label, names, () => ColorPresets.NameOf(get()) ?? ColorPresets.CustomName,
                name =>
                {
                    var preset = ColorPresets.All.FirstOrDefault(p => p.Name == name);
                    if (preset.Hex is not null) set(preset.Hex);
                }, forceStepper: true)
            {
                Section = section,
                SwatchHex = get,
                HintWhen = () => $"#{get()}",
            };
            yield return Channel("Vermelho", 0);
            yield return Channel("Verde", 1);
            yield return Channel("Azul", 2);

            IntSettingRow Channel(string name, int index) => new(name,
                () => index switch { 0 => ColorPresets.Split(get()).R, 1 => ColorPresets.Split(get()).G, _ => ColorPresets.Split(get()).B },
                v =>
                {
                    var (r, g, b) = ColorPresets.Split(get());
                    set(index switch { 0 => ColorPresets.Join(v, g, b), 1 => ColorPresets.Join(r, v, b), _ => ColorPresets.Join(r, g, v) });
                }, 0, 255, 15, "", slider: true);
        }

        // One group at a time keeps the screen short: pick the group, then edit its label and value colors.
        rows.Add(new EnumSettingRow("Grupo", ColorGroups.All.Select(g => g.Key).ToArray(), () => _colorGroup, v => _colorGroup = v,
            ColorGroups.All.Select(g => g.Name).ToArray(), forceStepper: true) { Section = "Cores" });
        rows.AddRange(ColorRows("Cor do rótulo", () => cfg.LabelColor(_colorGroup), v => { cfg.SetColors(_colorGroup, label: v); Changed(); }, null));
        rows.AddRange(ColorRows("Cor dos valores", () => cfg.ValueColor(_colorGroup), v => { cfg.SetColors(_colorGroup, value: v); Changed(); }, null));
        return _rows = rows;
    }

    private List<SettingRow> BuildSettingsRows()
    {
        var s = _appSettings.Current;
        return
        [
            new EnumSettingRow("Tema", [ThemeManager.Dark, ThemeManager.Light], () => ThemeManager.Normalize(s.Theme),
                v => { s.Theme = v; _appSettings.Save(); }, ["Escuro", "Claro"]) { Section = "Aparência" },
            new BoolSettingRow("Iniciar com o Windows", StartupRegistration.IsEnabled, StartupRegistration.SetEnabled) { Section = "Inicialização" },
            new EnumSettingRow("Ao iniciar", ["window", "tray"], () => s.StartInTray ? "tray" : "window",
                v => { s.StartInTray = v == "tray"; _appSettings.Save(); }, ["Abrir a janela", "Só na bandeja"]),
            new BoolSettingRow("Abrir com um clique na bandeja", () => s.TrayClickOpens, on => { s.TrayClickOpens = on; _appSettings.Save(); })
                { Hint = "Desligado: só o clique duplo abre" },
        ];
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
            StatusText.Text = "Monitoramento ligado.";
        }
        else
        {
            StatusText.Text = "Não foi possível abrir o MSI Afterburner.";
        }
        RefreshAll();
    }

    private void ChangeCategory(int delta)
    {
        var count = Enum.GetValues<Category>().Length;
        SetCategory((Category)(((int)_category + delta + count) % count));
    }

    private void SetCategory(Category category)
    {
        if (category == _category) return;
        _category = category;
        RebuildRows();
    }

    private void OnRailLsClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => SetCategory(Category.LosslessScaling);

    private void OnRailMonClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => SetCategory(Category.Monitoring);

    // Runs after App has applied the new theme; the rail items are styled from code, so they need a nudge.
    private void OnAppSettingsChanged() => UpdateChrome();

    private void OnRailSetClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => SetCategory(Category.Settings);

    // Everything outside the row list that depends on the active category.
    private void UpdateChrome()
    {
        var ls = _category == Category.LosslessScaling;
        StyleRailItem(RailLs, ls);
        StyleRailItem(RailMon, _category == Category.Monitoring);
        StyleRailItem(RailSet, _category == Category.Settings);
        RailDot.Visibility = _scaling ? Visibility.Visible : Visibility.Hidden;
        MonDot.Visibility = _monitoring.Config.Enabled ? Visibility.Visible : Visibility.Hidden;

        var lsOnly = ls ? Visibility.Visible : Visibility.Collapsed;
        ProfileChip.Visibility = lsOnly;
        ProfileHint.Visibility = lsOnly;
        RestartHint.Visibility = lsOnly;
        SaveHint.Visibility = lsOnly;
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
        MonDot.Visibility = _monitoring.Config.Enabled ? Visibility.Visible : Visibility.Hidden;
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
        HintAText.Text = row.IsMaster ? (_category == Category.LosslessScaling ? "Ligar / desligar LS" : "Ligar / desligar")
            : row.IsToggle ? "Ligar / desligar"
            : row.IsPosition ? "Próximo canto"
            : "Próximo valor";
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
                StatusText.Text = "Lossless Scaling reiniciado.";
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
        var hotkey = System.Windows.Forms.Keys.F12;
        var modifiers = Array.Empty<System.Windows.Forms.Keys>();
        try
        {
            (hotkey, modifiers) = LosslessScalingSettingsService.ParseHotkey(_settingsService.Load());
        }
        catch (Exception)
        {
            // Settings unreadable: fall back to LS's default F12.
        }

        if (_scaling)
        {
            // Turning off: the hotkey is global, so keep the overlay open and let the state poll show the result.
            Task.Run(() => HotkeyInputSender.SendHotkey(hotkey, modifiers));
            // LS hides its windows while the overlay has focus, so the poll can't confirm this; assume it worked.
            _scaling = false;
            _rows.FirstOrDefault()?.Refresh();
            RailDot.Visibility = Visibility.Hidden;
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
            StatusText.Text = "Não foi possível abrir o Lossless Scaling.";
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
                RailDot.Visibility = _scaling ? Visibility.Visible : Visibility.Hidden;
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
                StatusText.Text = "Configurações salvas e Lossless Scaling reiniciado.";
            }
            else
            {
                StatusText.Text = "Configurações salvas.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erro ao salvar: {ex.Message}";
        }
    }
}
