using System.Windows;
using System.Windows.Forms;
using LosslessScaling.Companion.Overlay;
using LosslessScaling.Companion.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace LosslessScaling.Companion;

public partial class App : Application
{
    private AppConfigService _configService = null!;
    private Models.AppConfig _config = null!;
    private LosslessScalingSettingsService _settingsService = null!;
    private LosslessScalingProcessService _processService = null!;
    private GamepadService _gamepad = null!;
    private NotifyIcon _trayIcon = null!;
    private OverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
        _settingsService = new LosslessScalingSettingsService();
        _processService = new LosslessScalingProcessService();

        var syncResult = _settingsService.SyncWithLocalCopy();

        if (!_settingsService.SettingsFileExists)
        {
            MessageBox.Show(
                $"Não encontrei o Settings.xml do Lossless Scaling em:\n{LosslessScalingSettingsService.SettingsPath}\n\n" +
                "Abra o Lossless Scaling pelo menos uma vez para gerar o arquivo de configurações, depois reinicie este app.",
                "Lossless Scaling Companion",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _gamepad = new GamepadService(_config);
        _gamepad.ToggleComboTriggered += OnToggleComboTriggered;
        _gamepad.OverlayComboTriggered += OnOverlayComboTriggered;
        _gamepad.Start();

        SetupTrayIcon();

        if (syncResult == LosslessScalingSettingsService.SyncResult.AppliedToLosslessScaling)
        {
            // LS only reads Settings.xml at startup, so a running instance must be relaunched to pick up the copy.
            if (_processService.IsRunning) _processService.RestartIfRunning(_config);
            _trayIcon.ShowBalloonTip(2000, "Lossless Scaling", "Configurações da cópia local aplicadas.", ToolTipIcon.None);
        }

        if (e.Args.Contains("--overlay"))
        {
            ShowOverlay();
        }
    }

    private void SetupTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir configurações", null, (_, _) => ShowOverlay());
        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true, Checked = StartupRegistration.IsEnabled() };
        startupItem.Click += (_, _) => StartupRegistration.SetEnabled(startupItem.Checked);
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Shutdown());

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Lossless Scaling Companion",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowOverlay();
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
            _trayIcon.ShowBalloonTip(1000, "Lossless Scaling", "Alternado", ToolTipIcon.None);
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(2000, "Lossless Scaling Companion", $"Falha ao alternar: {ex.Message}", ToolTipIcon.Error);
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
                _overlay = new OverlayWindow(_settingsService, _processService, _config, _gamepad)
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
            _trayIcon.ShowBalloonTip(4000, "Lossless Scaling Companion", $"Não foi possível abrir o overlay: {ex.Message}", ToolTipIcon.Error);
            _overlay = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _gamepad.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnExit(e);
    }
}
