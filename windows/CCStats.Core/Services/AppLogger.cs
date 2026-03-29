using System.Diagnostics;

namespace CCStats.Core.Services;

/// <summary>
/// Simple logger that writes to both Debug output and stderr.
/// Debug output is only visible in a debugger; stderr is visible in the terminal.
/// </summary>
public static class AppLogger
{
    private static bool _consoleEnabled = true;

    /// <summary>Set to false to suppress console output (e.g., in production/installed mode).</summary>
    public static bool ConsoleEnabled
    {
        get => _consoleEnabled;
        set => _consoleEnabled = value;
    }

    public static void Log(string tag, string message)
    {
        var line = $"[{tag}] {message}";
        Debug.WriteLine(line);
        if (_consoleEnabled)
        {
            try { Console.Error.WriteLine(line); }
            catch { /* stderr might not be available */ }
        }
    }

    public static void Error(string tag, string message, Exception? ex = null)
    {
        var line = ex is not null
            ? $"[{tag}] ERROR: {message} — {ex.GetType().Name}: {ex.Message}"
            : $"[{tag}] ERROR: {message}";
        Debug.WriteLine(line);
        if (_consoleEnabled)
        {
            try { Console.Error.WriteLine(line); }
            catch { }
        }
    }
}
