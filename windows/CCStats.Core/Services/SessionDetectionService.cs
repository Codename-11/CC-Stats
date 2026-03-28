using System.Diagnostics;

namespace CCStats.Core.Services;

/// <summary>
/// Detects whether Claude Code (or similar AI coding tools) are actively running.
/// When active, suggests a faster poll interval for better trend resolution.
/// </summary>
public static class SessionDetectionService
{
    private static readonly string[] ClaudeProcessNames =
    {
        "claude",           // Claude Code CLI
        "claude-code",      // Claude Code alternative
    };

    /// <summary>
    /// Checks if any Claude-related processes are currently running.
    /// Looks for exact process names first, then checks Node.js processes
    /// for Claude-specific command-line arguments.
    /// </summary>
    public static bool IsClaudeActive()
    {
        try
        {
            foreach (var name in ClaudeProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                var found = processes.Length > 0;
                foreach (var p in processes) p.Dispose();
                if (found) return true;
            }

            // Check node processes for Claude Code specifically
            var nodeProcesses = Process.GetProcessesByName("node");
            try
            {
                foreach (var p in nodeProcesses)
                {
                    try
                    {
                        // Check if the node process main module path contains "claude"
                        var mainModule = p.MainModule?.FileName;
                        if (mainModule is not null &&
                            mainModule.Contains("claude", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Access denied for some processes — skip
                    }
                }
            }
            finally
            {
                foreach (var p in nodeProcesses) p.Dispose();
            }
        }
        catch
        {
            // Process enumeration can fail, treat as inactive
        }
        return false;
    }

    /// <summary>
    /// Returns the recommended poll interval in seconds based on whether Claude is active.
    /// </summary>
    public static int GetAdaptiveInterval(int configuredInterval)
    {
        if (!IsClaudeActive()) return configuredInterval;

        // When Claude is active, poll at most every 15 seconds
        // but never faster than what user configured if they set < 15
        return Math.Min(configuredInterval, 15);
    }
}
