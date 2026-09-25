using System.Windows.Threading;
using LosslessScaling.Companion.Models;

namespace LosslessScaling.Companion.Services;

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

    private DateTime? _toggleHeldSince;
    private DateTime? _overlayHeldSince;
    private DateTime _toggleCooldownUntil = DateTime.MinValue;
    private DateTime _overlayCooldownUntil = DateTime.MinValue;

    public bool AnyControllerConnected { get; private set; }

    /// <summary>Buttons held as of the last poll.</summary>
    public GamepadButton HeldButtons => _previousButtons;

    /// <summary>Fires once when a tracked button transitions from up to down. Only used while the overlay is open.</summary>
    public event Action<GamepadButton>? ButtonPressed;

    /// <summary>Fires once the toggle combo has been held for the configured duration.</summary>
    public event Action? ToggleComboTriggered;

    /// <summary>Fires once the overlay combo has been held for the configured duration.</summary>
    public event Action? OverlayComboTriggered;

    public GamepadService(AppConfig config)
    {
        _config = config;
        _toggleCombo = ParseCombo(config.ToggleCombo);
        _overlayCombo = ParseCombo(config.OverlayCombo);

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
        if (pressedNow != GamepadButton.None)
        {
            foreach (GamepadButton flag in Enum.GetValues<GamepadButton>())
            {
                if (flag != GamepadButton.None && pressedNow.HasFlag(flag))
                {
                    ButtonPressed?.Invoke(flag);
                }
            }
        }

        var now = DateTime.UtcNow;
        EvaluateCombo(buttons, _toggleCombo, ref _toggleHeldSince, ref _toggleCooldownUntil, now, ToggleComboTriggered);
        EvaluateCombo(buttons, _overlayCombo, ref _overlayHeldSince, ref _overlayCooldownUntil, now, OverlayComboTriggered);

        _previousButtons = buttons;
    }

    private void EvaluateCombo(
        GamepadButton current,
        GamepadButton combo,
        ref DateTime? heldSince,
        ref DateTime cooldownUntil,
        DateTime now,
        Action? trigger)
    {
        if (combo == GamepadButton.None)
        {
            return;
        }

        bool isHeld = (current & combo) == combo;
        if (!isHeld)
        {
            heldSince = null;
            return;
        }

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
            trigger?.Invoke();
        }
    }

    private static GamepadButton ReadMergedButtons(out bool anyConnected)
    {
        GamepadButton merged = GamepadButton.None;
        anyConnected = false;

        for (int i = 0; i < 4; i++)
        {
            if (XInputNative.XInputGetState(i, out var state) == 0) // ERROR_SUCCESS
            {
                anyConnected = true;
                merged |= state.Gamepad.Buttons;
            }
        }

        return merged;
    }

    public void Dispose() => _timer.Stop();
}
