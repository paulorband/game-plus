using System.Windows.Threading;
using GamePlus.Models;

namespace GamePlus.Services;

/// <summary>
/// Polls up to 4 XInput controllers, merges them into one logical pad (any connected
/// controller can drive the app), and raises edge-triggered button and hold-combo events.
/// </summary>
public sealed class GamepadService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly AppConfig _config;

    private GamepadButton _previousButtons;
    private readonly GamepadButton _toggleCombo;
    private readonly GamepadButton _overlayCombo;
    private readonly GamepadButton _screenshotCombo;

    private DateTime? _toggleHeldSince;
    private DateTime? _overlayHeldSince;
    private DateTime? _screenshotHeldSince;
    private DateTime? _toggleLastSeenHeld;
    private DateTime? _overlayLastSeenHeld;
    private DateTime? _screenshotLastSeenHeld;
    private DateTime _toggleCooldownUntil = DateTime.MinValue;
    private DateTime _overlayCooldownUntil = DateTime.MinValue;
    private DateTime _screenshotCooldownUntil = DateTime.MinValue;

    public bool AnyControllerConnected { get; private set; }

    /// <summary>Buttons held as of the last poll.</summary>
    public GamepadButton HeldButtons => _previousButtons;

    /// <summary>Fires once when a tracked button transitions from up to down. Only used while the overlay is open.</summary>
    public event Action<GamepadButton>? ButtonPressed;

    /// <summary>Fires once when a button goes from down to up.</summary>
    public event Action<GamepadButton>? ButtonReleased;

    /// <summary>Fires repeatedly while a direction (D-Pad or left stick) stays held, after an initial delay.</summary>
    public event Action<GamepadButton>? ButtonRepeated;

    /// <summary>Fires once the toggle combo has been held for the configured duration.</summary>
    public event Action? ToggleComboTriggered;

    /// <summary>Fires once the overlay combo has been held for the configured duration.</summary>
    public event Action? OverlayComboTriggered;

    /// <summary>Fires once the screenshot combo has been held for the configured duration.</summary>
    public event Action? ScreenshotComboTriggered;

    public GamepadService(AppConfig config)
    {
        _config = config;
        _toggleCombo = ParseCombo(config.ToggleCombo);
        _overlayCombo = ParseCombo(config.OverlayCombo);
        _screenshotCombo = ParseCombo(config.ScreenshotCombo);

        _timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(8, config.PollIntervalMs))
        };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private static GamepadButton ParseCombo(IEnumerable<string> names)
    {
        GamepadButton result = GamepadButton.None;
        foreach (var name in names)
        {
            if (Enum.TryParse<GamepadButton>(name, ignoreCase: true, out var button))
            {
                result |= button;
            }
        }
        return result;
    }

    private void Poll()
    {
        var buttons = ReadMergedButtons(out var connected);
        AnyControllerConnected = connected;

        var pressedNow = buttons & ~_previousButtons;
        foreach (var flag in Flags(pressedNow)) ButtonPressed?.Invoke(flag);

        var now = DateTime.UtcNow;
        RepeatDirections(buttons, pressedNow, now);
        foreach (var flag in Flags(_previousButtons & ~buttons)) ButtonReleased?.Invoke(flag);

        EvaluateCombo(buttons, _toggleCombo, ref _toggleHeldSince, ref _toggleLastSeenHeld, ref _toggleCooldownUntil, now, "toggle", ToggleComboTriggered);
        EvaluateCombo(buttons, _overlayCombo, ref _overlayHeldSince, ref _overlayLastSeenHeld, ref _overlayCooldownUntil, now, "overlay", OverlayComboTriggered);
        EvaluateCombo(buttons, _screenshotCombo, ref _screenshotHeldSince, ref _screenshotLastSeenHeld, ref _screenshotCooldownUntil, now, "screenshot", ScreenshotComboTriggered);

        _previousButtons = buttons;
    }

    private static IEnumerable<GamepadButton> Flags(GamepadButton buttons)
    {
        if (buttons == GamepadButton.None) yield break;
        foreach (var flag in Enum.GetValues<GamepadButton>())
            if (flag != GamepadButton.None && buttons.HasFlag(flag)) yield return flag;
    }

    // Like keyboard auto-repeat: a first delay, then a steady rhythm (horizontal is slower, it changes values).
    private const int RepeatDelayMs = 400, VerticalRepeatMs = 110, HorizontalRepeatMs = 140;
    private static readonly GamepadButton[] Directions = [GamepadButton.DPadUp, GamepadButton.DPadDown, GamepadButton.DPadLeft, GamepadButton.DPadRight];
    private readonly Dictionary<GamepadButton, DateTime> _nextRepeat = new();

    private void RepeatDirections(GamepadButton buttons, GamepadButton pressedNow, DateTime now)
    {
        foreach (var direction in Directions)
        {
            if (!buttons.HasFlag(direction)) continue;
            if (pressedNow.HasFlag(direction))
            {
                _nextRepeat[direction] = now.AddMilliseconds(RepeatDelayMs);
            }
            else if (_nextRepeat.TryGetValue(direction, out var due) && now >= due)
            {
                var vertical = direction is GamepadButton.DPadUp or GamepadButton.DPadDown;
                _nextRepeat[direction] = now.AddMilliseconds(vertical ? VerticalRepeatMs : HorizontalRepeatMs);
                ButtonRepeated?.Invoke(direction);
            }
        }
    }

    // A real controller occasionally drops a single poll's reading (Bluetooth packet loss, USB polling jitter),
    // which would otherwise reset the hold timer and make a combo that IS being held correctly never reach
    // ComboHoldMs. Tolerating a short gap since the combo was last seen fully held smooths that out without
    // weakening the "must hold it together" requirement in any way a person would notice.
    private const int ComboGraceMs = 90;

    private void EvaluateCombo(
        GamepadButton current,
        GamepadButton combo,
        ref DateTime? heldSince,
        ref DateTime? lastSeenHeld,
        ref DateTime cooldownUntil,
        DateTime now,
        string name,
        Action? trigger)
    {
        if (combo == GamepadButton.None)
        {
            return;
        }

        bool isHeld = (current & combo) == combo;
        if (isHeld)
        {
            lastSeenHeld = now;
        }
        else if (lastSeenHeld is null || (now - lastSeenHeld.Value).TotalMilliseconds > ComboGraceMs)
        {
            if (heldSince is not null && heldSince != DateTime.MaxValue) Log.Info($"Gamepad combo '{name}' released before triggering");
            heldSince = null;
            lastSeenHeld = null;
            return;
        }
        // else: not held on this exact poll, but within grace of the last poll that saw it held — keep counting.

        if (heldSince is null) Log.Info($"Gamepad combo '{name}' held, waiting {_config.ComboHoldMs}ms");
        heldSince ??= now;

        if (now < cooldownUntil)
        {
            return;
        }

        if ((now - heldSince.Value).TotalMilliseconds >= _config.ComboHoldMs)
        {
            cooldownUntil = now.AddMilliseconds(_config.ComboCooldownMs);
            // Latch until the combo is released, so holding the buttons fires only once.
            heldSince = DateTime.MaxValue;
            Log.Info($"Gamepad combo '{name}' triggered");
            trigger?.Invoke();
        }
    }

    private const byte TriggerThreshold = 100;

    // The left stick doubles as a D-Pad: pressed past StickPress, released below StickRelease (hysteresis avoids flicker).
    private const int StickPress = 16000, StickRelease = 9000;
    private int _stickX, _stickY;

    private static int Latch(int current, int value) =>
        current == 0 ? (value > StickPress ? 1 : value < -StickPress ? -1 : 0)
        : (Math.Sign(value) != current || Math.Abs(value) < StickRelease ? 0 : current);

    private GamepadButton StickAsDPad(int x, int y)
    {
        // One axis at a time (the dominant one), so a diagonal push doesn't fire two directions.
        if (Math.Abs(x) >= Math.Abs(y)) y = 0; else x = 0;
        _stickX = Latch(_stickX, x);
        _stickY = Latch(_stickY, y);
        var result = GamepadButton.None;
        if (_stickX < 0) result |= GamepadButton.DPadLeft;
        if (_stickX > 0) result |= GamepadButton.DPadRight;
        if (_stickY > 0) result |= GamepadButton.DPadUp;
        if (_stickY < 0) result |= GamepadButton.DPadDown;
        return result;
    }

    private GamepadButton ReadMergedButtons(out bool anyConnected)
    {
        GamepadButton merged = GamepadButton.None;
        anyConnected = false;
        int stickX = 0, stickY = 0;

        for (int i = 0; i < 4; i++)
        {
            if (XInputNative.XInputGetState(i, out var state) == 0) // ERROR_SUCCESS
            {
                anyConnected = true;
                merged |= state.Gamepad.Buttons;
                if (state.Gamepad.LeftTrigger > TriggerThreshold) merged |= GamepadButton.LeftTrigger;
                if (state.Gamepad.RightTrigger > TriggerThreshold) merged |= GamepadButton.RightTrigger;
                if (Math.Abs((int)state.Gamepad.ThumbLX) > Math.Abs(stickX)) stickX = state.Gamepad.ThumbLX;
                if (Math.Abs((int)state.Gamepad.ThumbLY) > Math.Abs(stickY)) stickY = state.Gamepad.ThumbLY;
            }
        }

        return merged | StickAsDPad(stickX, stickY);
    }

    public void Dispose() => _timer.Stop();
}
