using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GamePlus.Services.Monitoring;

/// <summary>Finds and starts MSI Afterburner and RTSS, the two programs the monitoring depends on.</summary>
public static class AfterburnerLauncher
{
    private static readonly string[] ProgramFilesRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    ];

    public static string? FindAfterburner() => FindExe(@"MSI Afterburner\MSIAfterburner.exe", @"SOFTWARE\MSI\Afterburner", "InstallPath");

    public static string? FindRtss() => FindExe(@"RivaTuner Statistics Server\RTSS.exe", @"SOFTWARE\Unwinder\RTSS", "InstallPath");

    /// <summary>Afterburner ships RTSS, so having Afterburner is what "installed" means for the monitoring.</summary>
    public static bool IsInstalled() => FindAfterburner() is not null;

    /// <summary>Starts whichever of Afterburner/RTSS isn't running yet. False if nothing could be started.</summary>
    public static bool StartMissing(bool afterburnerUp, bool rtssUp)
    {
        var started = false;
        if (!afterburnerUp) started |= Start(FindAfterburner());
        // Afterburner normally brings RTSS up itself; only start it directly when it is still missing later.
        if (afterburnerUp && !rtssUp) started |= Start(FindRtss());
        return started;
    }

    /// <summary>Starts RTSS on its own (used when Afterburner is up but RTSS didn't follow).</summary>
    public static bool StartRtss() => Start(FindRtss());

    private static bool Start(string? exe)
    {
        if (exe is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                WindowStyle = ProcessWindowStyle.Minimized,
            });
            Log.Info($"Started {exe}");
            return true;
        }
        catch (Exception ex)
        {
            // Includes the user cancelling a UAC prompt for programs that ask for elevation.
            Log.Error($"Start {exe}", ex);
            return false;
        }
    }

    private static string? FindExe(string relativePath, string registryKey, string valueName)
    {
        foreach (var root in ProgramFilesRoots)
        {
            var path = Path.Combine(root, relativePath);
            if (File.Exists(path)) return path;
        }

        try
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(registryKey);
                if (key?.GetValue(valueName) is string dir)
                {
                    var path = Path.Combine(dir, Path.GetFileName(relativePath));
                    if (File.Exists(path)) return path;
                }
            }
        }
        catch (Exception)
        {
            // Registry unreadable: the Program Files probe above already had its chance.
        }
        return null;
    }
}
