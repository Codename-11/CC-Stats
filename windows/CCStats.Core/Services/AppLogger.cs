using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace CCStats.Core.Services;

/// <summary>
/// Simple logger that writes to Debug output, stderr, and an in-memory ring buffer.
/// The ring buffer enables a debug log viewer in the settings UI.
/// </summary>
public static class AppLogger
{
    private static bool _consoleEnabled = true;
    private static readonly ConcurrentQueue<LogEntry> _buffer = new();
    private const int MaxBufferSize = 500;

    /// <summary>Set to false to suppress console output (e.g., in production/installed mode).</summary>
    public static bool ConsoleEnabled
    {
        get => _consoleEnabled;
        set => _consoleEnabled = value;
    }

    /// <summary>Set to SynchronizationContext.Current from the UI thread during app init.</summary>
    public static SynchronizationContext? UiContext { get; set; }

    /// <summary>Fired on UI thread when a new log entry is added.</summary>
    public static event EventHandler<LogEntry>? LogAdded;

    public static void Log(string tag, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, tag, message, false);
        Append(entry);
        var line = $"[{tag}] {message}";
        // Only write to stderr directly — Debug.WriteLine + ConsoleTraceListener causes duplicates
        if (_consoleEnabled)
        {
            try { Console.Error.WriteLine(line); }
            catch { /* stderr might not be available */ }
        }
        else
        {
            Debug.WriteLine(line);
        }
    }

    public static void Error(string tag, string message, Exception? ex = null)
    {
        var fullMessage = ex is not null
            ? $"{message} -- {ex.GetType().Name}: {ex.Message}"
            : message;
        var entry = new LogEntry(DateTimeOffset.Now, tag, fullMessage, true);
        Append(entry);
        var line = ex is not null
            ? $"[{tag}] ERROR: {message} -- {ex.GetType().Name}: {ex.Message}"
            : $"[{tag}] ERROR: {message}";
        if (_consoleEnabled)
        {
            try { Console.Error.WriteLine(line); }
            catch { }
        }
        else
        {
            Debug.WriteLine(line);
        }
    }

    /// <summary>Returns a snapshot of the current log buffer (oldest first).</summary>
    public static IReadOnlyList<LogEntry> GetEntries() => _buffer.ToArray();

    /// <summary>Clears the in-memory log buffer.</summary>
    public static void Clear() => _buffer.Clear();

    private static void Append(LogEntry entry)
    {
        _buffer.Enqueue(entry);
        // Trim to max size
        while (_buffer.Count > MaxBufferSize)
            _buffer.TryDequeue(out _);
        var handler = LogAdded;
        if (handler is not null)
        {
            if (UiContext is not null)
                UiContext.Post(_ => handler(null, entry), null);
            else
                handler(null, entry);
        }
    }
}

public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    string Tag,
    string Message,
    bool IsError)
{
    public string FormatLine() =>
        $"{Timestamp:HH:mm:ss} [{Tag}]{(IsError ? " ERROR" : "")} {Message}";
}
