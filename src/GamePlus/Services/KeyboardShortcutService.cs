using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GamePlus.Services;

/// <summary>System-wide keyboard shortcuts (RegisterHotKey), one per slot id: the keyboard counterpart of the gamepad combos.</summary>
public sealed class KeyboardShortcutService : NativeWindow, IDisposable
{
    /// <summary>Opens/closes the overlay.</summary>
    public const int SlotOverlay = 1;
    /// <summary>Takes a screenshot.</summary>
    public const int SlotScreenshot = 2;

    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnly = new(-3);

    private sealed class Slot
    {
        public string? Registered;
        public bool Failed;
        public Action? Trigger;
    }

    private readonly Dictionary<int, Slot> _slots = new();

    public KeyboardShortcutService()
    {
        CreateHandle(new CreateParams { Parent = MessageOnly });
    }

    /// <summary>Registers (or updates) the shortcut for a slot; null/empty turns it off. The id stays stable across calls.</summary>
    public void Apply(int id, string? shortcut, Action trigger)
    {
        if (!_slots.TryGetValue(id, out var slot)) _slots[id] = slot = new Slot();
        slot.Trigger = trigger;

        shortcut = shortcut?.Trim();
        if (shortcut == slot.Registered && !slot.Failed) return;

        if (slot.Registered is not null) UnregisterHotKey(Handle, id);
        slot.Registered = null;
        slot.Failed = false;
        if (string.IsNullOrEmpty(shortcut)) return;

        if (TryParse(shortcut, out var modifiers, out var key) && RegisterHotKey(Handle, id, modifiers | ModNoRepeat, key))
        {
            slot.Registered = shortcut;
            Log.Info($"Keyboard shortcut registered: {shortcut}");
        }
        else
        {
            slot.Failed = true;
            Log.Info($"Keyboard shortcut unavailable: {shortcut}");
        }
    }

    /// <summary>True when the last shortcut asked for a slot could not be registered (another program owns it, or it is malformed).</summary>
    public bool Failed(int id) => _slots.TryGetValue(id, out var slot) && slot.Failed;

    // A bare key would steal it from every other program, so at least one modifier is required.
    private static bool TryParse(string shortcut, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": modifiers |= ModWin; break;
                default: return false;
            }
        }

        var last = parts[^1];
        if (int.TryParse(last, out _) || !Enum.TryParse<Keys>(last, true, out var parsed)
            || parsed is Keys.None or Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return false;
        key = (uint)parsed;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && _slots.TryGetValue((int)m.WParam, out var slot)) slot.Trigger?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var (id, slot) in _slots)
        {
            if (slot.Registered is not null) UnregisterHotKey(Handle, id);
        }
        _slots.Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
