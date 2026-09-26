using System.Windows;
using System.Windows.Forms;
using GamePlus.Overlay;
using GamePlus.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace GamePlus;

public partial class App : Application
{
    private AppConfigService _configService = null!;
    private Models.AppConfig _config = null!;
    private LosslessScalingSettingsService _settingsService = null!;
    private LosslessScalingProcessService _processService = null!;
    private AppSettingsService _appSettings = null!;
    private GamepadService _gamepad = null!;
    private Services.Monitoring.MonitoringService _monitoring = null!;
    private NotifyIcon _trayIcon = null!;
    private OverlayWindow? _overlay;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Single instance: a second launch just asks the running one to show its window, like "Abrir configurações".
        // Per-session name, so another signed-in user can run their own copy.
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GamePlus.Show", out var firstInstance);
        if (!firstInstance)
        {
            Log.Info("Already running; asking the first instance to show itself");
            try { _showSignal.Set(); } catch (Exception ex) { Log.Error("Signal first instance", ex); }
            Shutdown();
            return;
        }
        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowOverlay);
        }) { IsBackground = true, Name = "SingleInstanceListener" };
        listener.Start();

        // Errors raised from tray-menu callbacks would otherwise vanish (or hide behind a fullscreen shell).
        System.Windows.Forms.Application.ThreadException += (_, args) => Log.Error("WinForms ThreadException", args.Exception);
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("UnhandledException", (Exception)args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Error("UnobservedTaskException", args.Exception);
        Log.Info($"Start {Environment.OSVersion} | exe={Environment.ProcessPath} | args={string.Join(' ', e.Args)}");

        _configService = new AppConfigService();
        _config = _configService.Load();
        _appSettings = new AppSettingsService();
        Loc.Instance.Select(_appSettings.Current.Language);
        ApplyAppearance();
        _appSettings.Changed += ApplyAppearance;
        _settingsService = new LosslessScalingSettingsService();
        _processService = new LosslessScalingProcessService();

        var syncResult = _settingsService.SyncWithLocalCopy();

        if (!_settingsService.SettingsFileExists)
        {
            MessageBox.Show(
                Loc.T("app.ls_settings_missing", LosslessScalingSettingsService.SettingsPath),
                "Game Plus",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _monitoring = new Services.Monitoring.MonitoringService(new MonitoringConfigService());
        _monitoring.Start();

        _gamepad = new GamepadService(_config);
        _gamepad.ToggleComboTriggered += OnToggleComboTriggered;
        _gamepad.OverlayComboTriggered += OnOverlayComboTriggered;
        _gamepad.Start();

        SetupTrayIcon();

        if (syncResult == LosslessScalingSettingsService.SyncResult.AppliedToLosslessScaling)
        {
            // LS only reads Settings.xml at startup, so a running instance must be relaunched to pick up the copy.
            if (_processService.IsRunning) _processService.RestartIfRunning(_config);
            _trayIcon.ShowBalloonTip(2000, "Lossless Scaling", Loc.T("tray.local_applied"), ToolTipIcon.None);
        }

        // The window is the default; "start in tray" is opt-in. --overlay always opens it.
        if (e.Args.Contains("--overlay") || !_appSettings.Current.StartInTray)
        {
            ShowOverlay();
        }
    }

    // Theme, panel opacity and language are app-wide, so they are applied here rather than by any one window.
    private void ApplyAppearance()
    {
        ThemeManager.Apply(_appSettings.Current.Theme, _appSettings.Current.PanelOpacity);
        Loc.Instance.Select(_appSettings.Current.Language);
    }

    private ToolStripMenuItem _openItem = null!, _startupItem = null!, _exitItem = null!;

    private void RetitleTrayMenu()
    {
        _openItem.Text = Loc.T("tray.open");
        _startupItem.Text = Loc.T("tray.start_with_windows");
        _exitItem.Text = Loc.T("tray.exit");
    }

    private void SetupTrayIcon()
    {
        var menu = new ContextMenuStrip();
        _openItem = new ToolStripMenuItem(null, null, (_, _) => ShowOverlay());
        menu.Items.Add(_openItem);
        menu.Items.Add(new ToolStripSeparator());

        _startupItem = new ToolStripMenuItem { CheckOnClick = true, Checked = StartupRegistration.IsEnabled() };
        _startupItem.Click += (_, _) => StartupRegistration.SetEnabled(_startupItem.Checked);
        menu.Items.Add(_startupItem);
        // The overlay's Settings category can flip this too, so re-read it whenever the menu opens.
        menu.Opening += (_, _) => _startupItem.Checked = StartupRegistration.IsEnabled();

        menu.Items.Add(new ToolStripSeparator());
        _exitItem = new ToolStripMenuItem(null, null, (_, _) => Shutdown());
        menu.Items.Add(_exitItem);
        RetitleTrayMenu();
        Loc.Instance.LanguageChanged += RetitleTrayMenu;

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Game Plus",
            ContextMenuStrip = menu,
        };
        // One click opens by default (a tray icon is a button, not a file); the setting restores the classic double click.
        _trayIcon.MouseClick += (_, args) => { if (args.Button == MouseButtons.Left && _appSettings.Current.TrayClickOpens) ShowOverlay(); };
        _trayIcon.DoubleClick += (_, _) => { if (!_appSettings.Current.TrayClickOpens) ShowOverlay(); };
    }

    // Uses the exe's embedded multi-size icon, picking the size the tray wants.
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using var full = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            if (full is not null) return new System.Drawing.Icon(full, SystemInformation.SmallIconSize);
        }
        catch (Exception)
        {
            // Fall through to the stock icon.
        }
        return System.Drawing.SystemIcons.Application;
    }

    private void OnToggleComboTriggered()
    {
        try
        {
            var settings = _settingsService.Load();
            var (key, modifiers) = LosslessScalingSettingsService.ParseHotkey(settings);
            HotkeyInputSender.SendHotkey(key, modifiers);
            _trayIcon.ShowBalloonTip(1000, "Lossless Scaling", Loc.T("tray.toggled"), ToolTipIcon.None);
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(2000, "Game Plus", Loc.T("tray.toggle_failed", ex.Message), ToolTipIcon.Error);
        }
    }

    private void OnOverlayComboTriggered() => Dispatcher.Invoke(ToggleOverlay);

    private void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            _ = _overlay.CloseAfterReleaseAsync();
        }
        else
        {
            ShowOverlay();
        }
    }

    private void ShowOverlay()
    {
        try
        {
            Log.Info("ShowOverlay");
            using var watchdog = Log.Watchdog("ShowOverlay", TimeSpan.FromSeconds(4));
            if (_overlay is null || !_overlay.IsLoaded)
            {
                var game = GameWindowFinder.Find();
                // LS drops its scaling windows once the game loses focus, so read the state before the overlay steals it.
                var scaling = _processService.IsScaling;
                _overlay = new OverlayWindow(_settingsService, _processService, _config, _gamepad, _monitoring, _appSettings)
                {
                    PreviousForegroundWindow = game,
                    InitialScaling = scaling,
                };
                _overlay.Closed += (_, _) => _overlay = null;
                Log.Info("Overlay created");
            }

            _overlay.Show();
            Log.Info("Overlay shown");
            _overlay.Activate();
        }
        catch (Exception ex)
        {
            Log.Error("ShowOverlay", ex);
            _trayIcon.ShowBalloonTip(4000, "Game Plus", Loc.T("tray.overlay_failed", ex.Message), ToolTipIcon.Error);
            _overlay = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitoring?.Dispose();
        _gamepad?.Dispose();
        if (_trayIcon is not null) _trayIcon.Visible = false;
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
