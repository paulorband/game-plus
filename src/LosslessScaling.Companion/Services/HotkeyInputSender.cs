using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LosslessScaling.Companion.Services;

/// <summary>
/// Sends the Lossless Scaling toggle hotkey as a real global keystroke via SendInput,
/// exactly as if the user pressed it on a keyboard. This is the same mechanism Lossless
/// Scaling's own hotkey listener expects, so it works regardless of which window has focus.
/// </summary>
public static class HotkeyInputSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // Matches the native INPUT union layout on x64: a 4-byte type tag, then 4 bytes of
    // padding to the union's 8-byte alignment, then the largest member (MOUSEINPUT/KEYBDINPUT).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Diagnostic description of a window: process, class and screen rectangle.</summary>
    public static string Describe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "none";
        try
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            var cls = new System.Text.StringBuilder(128);
            GetClassName(hWnd, cls, cls.Capacity);
            GetWindowRect(hWnd, out var r);
            return $"{name} [{cls}] {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}";
        }
        catch (Exception ex)
        {
            return $"? ({ex.GetType().Name})";
        }
    }

    /// <summary>Takes foreground focus even when Windows' foreground lock would refuse a plain SetForegroundWindow,
    /// by briefly attaching to the current foreground window's input thread.</summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        var fg = GetForegroundWindow();
        if (fg == hWnd) return;

        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint current = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != current && AttachThreadInput(current, fgThread, true);
        try
        {
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached) AttachThreadInput(current, fgThread, false);
        }
    }

    /// <summary>
    /// Sends the given key (optionally with modifiers) as a key-down followed by key-up,
    /// modifiers pressed first and released last.
    /// </summary>
    public static void SendHotkey(Keys key, IEnumerable<Keys> modifiers)
    {
        var mods = modifiers.ToArray();
        var sequence = new List<Input>(mods.Length * 2 + 2);

        foreach (var m in mods) sequence.Add(BuildInput(m, up: false));
        sequence.Add(BuildInput(key, up: false));
        sequence.Add(BuildInput(key, up: true));
        for (int i = mods.Length - 1; i >= 0; i--) sequence.Add(BuildInput(mods[i], up: true));

        var array = sequence.ToArray();
        SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
    }

    private static Input BuildInput(Keys key, bool up) => new()
    {
        Type = InputKeyboard,
        Ki = new KeyboardInput
        {
            VirtualKey = (ushort)key,
            ScanCode = 0,
            Flags = up ? KeyEventFKeyUp : 0,
            Time = 0,
            ExtraInfo = IntPtr.Zero,
        }
    };
}
