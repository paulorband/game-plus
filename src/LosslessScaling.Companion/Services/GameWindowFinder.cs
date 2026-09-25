using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LosslessScaling.Companion.Services;

/// <summary>Picks the window that should get focus back (and be scaled): the foreground window, unless that's the shell
/// (a folder, the desktop, the taskbar), this app or Lossless Scaling itself, in which case the topmost real window.</summary>
public static class GameWindowFinder
{
    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "LosslessScaling", "LosslessScaling.Companion", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost",
    };

    public static IntPtr Find()
    {
        var foreground = HotkeyInputSender.GetForegroundWindow();
        if (foreground != IntPtr.Zero && !IsIgnored(foreground)) return foreground;

        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsCandidate(hwnd)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found != IntPtr.Zero ? found : foreground;
    }

    private static bool IsCandidate(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsIgnored(hwnd)) return false;
        if ((GetWindowLong(hwnd, -20) & 0x80) != 0) return false; // WS_EX_TOOLWINDOW
        if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        return r.Right - r.Left >= 400 && r.Bottom - r.Top >= 300;
    }

    private static bool IsIgnored(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return true;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return IgnoredProcesses.Contains(process.ProcessName);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
}
