using System.Windows;
using LosslessScaling.Companion.Models;
using LosslessScaling.Companion.Services;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace LosslessScaling.Companion.Overlay;

public partial class OverlayWindow : Window
{
    private readonly LosslessScalingSettingsService _settingsService;
    private readonly LosslessScalingProcessService _processService;
    private readonly AppConfig _config;
    private readonly GamepadService _gamepad;

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
        GamepadService gamepad)
    {
        Services.Log.Info("Overlay ctor: before InitializeComponent");
        InitializeComponent();
        Services.Log.Info("Overlay ctor: InitializeComponent done");

        _settingsService = settingsService;
        _processService = processService;
        _config = config;
        _gamepad = gamepad;

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
        Closed += (_, _) => _gamepad.ButtonPressed -= OnGamepadButtonPressed;
        _gamepad.ButtonPressed += OnGamepadButtonPressed;

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

        RowsList.ItemsSource = _rows;
        RowsList.SelectedIndex = 0;
        StatusText.Text = " ";
        RailDot.Visibility = _scaling ? Visibility.Visible : Visibility.Hidden;
    }

    private void RefreshAll()
    {
        foreach (var row in _rows) row.Refresh();
    }

    // Moves focus to the next enabled row in the given direction (stays put if none).
    private void MoveFocus(int direction)
    {
        var i = RowsList.SelectedIndex < 0 ? 0 : RowsList.SelectedIndex;
        for (var next = i + direction; next >= 0 && next < _rows.Count; next += direction)
        {
            if (!_rows[next].Available) continue;
            RowsList.SelectedIndex = next;
            RowsList.ScrollIntoView(_rows[next]);
            return;
        }
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RowsList.SelectedItem is not SettingRow row) return;
        HintAText.Text = row.IsMaster ? "Ligar / desligar LS" : row.IsToggle ? "Ligar / desligar" : "Próximo valor";
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
        Dispatcher.Invoke(() => Handle(button));
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
                ChangeProfile(-1);
                break;

            case GamepadButton.RightShoulder:
                ChangeProfile(1);
                break;

            case GamepadButton.Y:
                SaveSettings();
                break;

            case GamepadButton.X:
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
            StatusText.Text = "NÃ£o foi possÃ­vel abrir o Lossless Scaling.";
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
            tick++;
            await Task.Delay(500);
        }
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
