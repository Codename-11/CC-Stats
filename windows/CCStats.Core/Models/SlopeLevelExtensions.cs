namespace CCStats.Core.Models;

public static class SlopeLevelExtensions
{
    public static string Arrow(this SlopeLevel slope) => slope switch
    {
        SlopeLevel.Declining => "\u2198",  // ↘
        SlopeLevel.Flat => "\u2192",       // →
        SlopeLevel.Rising => "\u2197",     // ↗
        SlopeLevel.Steep => "\u2B06",      // ⬆
        _ => string.Empty,
    };

    public static bool IsActionable(this SlopeLevel slope) => slope is SlopeLevel.Rising or SlopeLevel.Steep;

    public static bool IsPositive(this SlopeLevel slope) => slope is SlopeLevel.Declining;
}
