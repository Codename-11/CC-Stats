namespace CCStats.Core.Models;

/// <summary>
/// Tracks where usage data came from, enabling transparent display in the UI.
/// </summary>
public enum UsageSource
{
    /// <summary>No data available.</summary>
    None,

    /// <summary>From Claude Code's local statusline cache (fast, may be slightly stale).</summary>
    LocalCache,

    /// <summary>Live from the Anthropic API (freshest).</summary>
    Api,

    /// <summary>Previous successful fetch held in memory (stale fallback).</summary>
    Cached,

    /// <summary>Only tier/subscription info available from stored credentials.</summary>
    CredentialsOnly,
}

public static class UsageSourceExtensions
{
    public static string Label(this UsageSource source) => source switch
    {
        UsageSource.LocalCache => "Local",
        UsageSource.Api => "API",
        UsageSource.Cached => "Cached",
        UsageSource.CredentialsOnly => "Tier",
        _ => "",
    };

    public static string Description(this UsageSource source) => source switch
    {
        UsageSource.LocalCache => "From Claude Code's local cache",
        UsageSource.Api => "Live from Anthropic API",
        UsageSource.Cached => "Previous data (API unreachable)",
        UsageSource.CredentialsOnly => "Subscription info only (no usage data)",
        _ => "No data available",
    };
}
