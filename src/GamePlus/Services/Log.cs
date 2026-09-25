using System.IO;

namespace GamePlus.Services;

/// <summary>Minimal file log next to the exe (Data\app.log), used to diagnose problems on other machines.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string Path_ { get; } = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
        "Data",
        "app.log");

    private static string _last = "";

    public static void Info(string message)
    {
        _last = message;
        Write("INFO ", message);
    }

    /// <summary>Logs a warning (with the last breadcrumb) if the returned handle isn't disposed within the timeout,
    /// which tells a hung UI thread apart from a crash.</summary>
    public static IDisposable Watchdog(string what, TimeSpan timeout)
    {
        return new System.Threading.Timer(_ => Write("WARN ", $"{what} still running after {timeout.TotalSeconds:0}s (UI thread blocked?); last step: {_last}"),
            null, timeout, Timeout.InfiniteTimeSpan);
    }

    public static void Error(string context, Exception ex) => Write("ERROR", $"{context}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
                var info = new FileInfo(Path_);
                if (info.Exists && info.Length > 512 * 1024) info.Delete();
                File.AppendAllText(Path_, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }
}
