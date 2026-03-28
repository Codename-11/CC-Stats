using System.Text.Json;

namespace CCStats.Core.Services;

/// <summary>
/// Reads Claude Code's local statusline usage cache as a fallback data source.
/// Claude Code writes usage data to a temp file that we can read without making
/// additional API calls. This provides instant data on app startup before our
/// own polling has completed.
///
/// File location: %TEMP%/claude-statusline-usage-cache.json
/// Format: { usageData: { five_hour: { utilization, resets_at }, seven_day: {...}, extra_usage: {...} }, fetchedAt, ... }
/// </summary>
public static class LocalCacheService
{
    private static readonly string CachePath = Path.Combine(
        Path.GetTempPath(), "claude-statusline-usage-cache.json");

    /// <summary>
    /// Attempts to read the Claude Code statusline cache.
    /// Returns null if the file doesn't exist, is too old, or can't be parsed.
    /// </summary>
    public static LocalCacheData? ReadCache(TimeSpan? maxAge = null)
    {
        try
        {
            if (!File.Exists(CachePath)) return null;

            var json = File.ReadAllText(CachePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check freshness
            if (root.TryGetProperty("fetchedAt", out var fetchedAtProp))
            {
                if (DateTimeOffset.TryParse(fetchedAtProp.GetString(), out var fetchedAt))
                {
                    var age = DateTimeOffset.UtcNow - fetchedAt;
                    var limit = maxAge ?? TimeSpan.FromMinutes(10);
                    if (age > limit) return null; // too stale
                }
            }

            var result = new LocalCacheData();

            if (root.TryGetProperty("usageData", out var usage))
            {
                if (usage.TryGetProperty("five_hour", out var fh))
                {
                    result.FiveHourUtilization = GetNullableDouble(fh, "utilization");
                    result.FiveHourResetsAt = GetNullableString(fh, "resets_at");
                }
                if (usage.TryGetProperty("seven_day", out var sd))
                {
                    result.SevenDayUtilization = GetNullableDouble(sd, "utilization");
                    result.SevenDayResetsAt = GetNullableString(sd, "resets_at");
                }
                if (usage.TryGetProperty("extra_usage", out var eu))
                {
                    result.ExtraUsageEnabled = GetNullableBool(eu, "is_enabled");
                    result.ExtraUsageUtilization = GetNullableDouble(eu, "utilization");
                }
            }

            if (root.TryGetProperty("fetchedAt", out var fa))
                result.FetchedAt = DateTimeOffset.TryParse(fa.GetString(), out var dt) ? dt : null;

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static double? GetNullableDouble(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        return null;
    }

    private static string? GetNullableString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static bool? GetNullableBool(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
            return v.GetBoolean();
        return null;
    }
}

public sealed class LocalCacheData
{
    public double? FiveHourUtilization { get; set; }
    public string? FiveHourResetsAt { get; set; }
    public double? SevenDayUtilization { get; set; }
    public string? SevenDayResetsAt { get; set; }
    public bool? ExtraUsageEnabled { get; set; }
    public double? ExtraUsageUtilization { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
}
