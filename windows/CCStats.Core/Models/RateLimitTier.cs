namespace CCStats.Core.Models;

public enum RateLimitTier
{
    Pro,
    Max5x,
    Max20x,
    Unknown,
}

public static class RateLimitTierExtensions
{
    public static CreditLimits GetCreditLimits(this RateLimitTier tier) => tier switch
    {
        RateLimitTier.Pro => new CreditLimits(550_000, 5_000_000, 20),
        RateLimitTier.Max5x => new CreditLimits(3_300_000, 41_670_000, 100),
        RateLimitTier.Max20x => new CreditLimits(11_000_000, 83_330_000, 200),
        _ => new CreditLimits(0, 0, null),
    };

    public static string DisplayName(this RateLimitTier tier) => tier switch
    {
        RateLimitTier.Pro => "Pro ($20)",
        RateLimitTier.Max5x => "Max 5x ($100)",
        RateLimitTier.Max20x => "Max 20x ($200)",
        _ => "Free",
    };

    public static RateLimitTier FromString(string? value) => value?.ToLowerInvariant() switch
    {
        // Exact API rate_limit_tier values (from profile endpoint)
        "default_claude_pro" => RateLimitTier.Pro,
        "default_claude_max_5x" => RateLimitTier.Max5x,
        "default_claude_max_20x" => RateLimitTier.Max20x,
        // Organization type values
        "claude_pro" or "pro" => RateLimitTier.Pro,
        "claude_max_5x" or "max_5x" or "max5x" or "max 5x" => RateLimitTier.Max5x,
        "claude_max_20x" or "max_20x" or "max20x" or "max 20x" => RateLimitTier.Max20x,
        // Enterprise / team
        "claude_enterprise" or "enterprise" => RateLimitTier.Max20x,
        "claude_team" or "team" => RateLimitTier.Max5x,
        // Generic "claude_max" is ambiguous — DON'T guess, fall through to Unknown
        // so the profile endpoint's rate_limit_tier can resolve it
        _ => RateLimitTier.Unknown,
    };
}
