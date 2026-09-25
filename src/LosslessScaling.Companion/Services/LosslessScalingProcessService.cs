using System.Diagnostics;
using System.IO;
using LosslessScaling.Companion.Models;
using Microsoft.Win32;

namespace LosslessScaling.Companion.Services;

public sealed class LosslessScalingProcessService
{
    private const string ProcessName = "LosslessScaling";

    private static readonly string[] CommonRelativePaths =
    [
        @"Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe",
    ];

    public string? FindExecutablePath(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.LosslessScalingExePath) && File.Exists(config.LosslessScalingExePath))
        {
            return config.LosslessScalingExePath;
        }

        // 1) Already running — ask the OS where it lives.
        var running = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (running is not null)
        {
            try
            {
                var path = running.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied reading MainModule of another process; fall through to other lookups.
            }
        }

        // 2) Common Steam library drive roots.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            foreach (var relative in CommonRelativePaths)
            {
                var candidate = Path.Combine(drive.RootDirectory.FullName, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            var programFilesX86Candidate = Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam", "steamapps", "common", "Lossless Scaling", "LosslessScaling.exe");
            if (File.Exists(programFilesX86Candidate))
            {
                return programFilesX86Candidate;
            }
        }

        // 3) Registry uninstall entries (covers non-Steam / Store installs).
        return FindViaUninstallRegistry();
    }

    private static string? FindViaUninstallRegistry()
    {
        string[] roots =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];

        foreach (var root in roots)
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key is null) continue;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                if (displayName is null || !displayName.Contains("Lossless Scaling", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var installLocation = subKey?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(installLocation))
                {
                    var candidate = Path.Combine(installLocation, "LosslessScaling.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    public bool IsRunning => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>True while LS is actively scaling: it then owns a visible top-level window of class "LosslessScaling"
    /// (its idle UI is a WPF HwndWrapper window, so the process alone doesn't tell).</summary>
    public bool IsScaling
    {
        get
        {
            var pids = Process.GetProcessesByName(ProcessName).Select(p => (uint)p.Id).ToHashSet();
            if (pids.Count == 0) return false;

            var found = false;
            var name = new System.Text.StringBuilder(64);
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (!pids.Contains(pid) || !IsWindowVisible(hwnd)) return true;
                name.Clear();
                GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() == "LosslessScaling") { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    public bool IsInstalled(AppConfig config) => FindExecutablePath(config) is not null;

    /// <summary>Starts LS minimized (no-op if it's already running). Returns false when it can't be found or started.</summary>
    public bool Launch(AppConfig config)
    {
        if (IsRunning) return true;
        var exePath = FindExecutablePath(config);
        if (exePath is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = "-StartMinimized",
                WorkingDirectory = Path.GetDirectoryName(exePath),
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Opens the Lossless Scaling page in the Steam client, or in the browser when Steam isn't installed.</summary>
    public static void OpenSteamStorePage()
    {
        foreach (var url in new[] { "steam://store/993090", "https://store.steampowered.com/app/993090/Lossless_Scaling/" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Steam protocol handler missing; try the web page next.
            }
        }
    }

    public void RestartIfRunning(AppConfig config)
    {
        var exePath = FindExecutablePath(config);
        if (exePath is null)
        {
            return;
        }

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process may have already exited; nothing to do.
            }
        }

        Process.Start(new ProcessStartInfo(exePath)
        {
            Arguments = "-StartMinimized",
            WorkingDirectory = Path.GetDirectoryName(exePath),
            UseShellExecute = true,
        });
    }
}
