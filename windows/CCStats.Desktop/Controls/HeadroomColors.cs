using Avalonia.Media;
using CCStats.Core.Models;

namespace CCStats.Desktop.Controls;

public static class HeadroomColors
{
    // Headroom state colors (matching macOS)
    public static readonly Color Normal = Color.Parse("#66B866");
    public static readonly Color Caution = Color.Parse("#E6C15A");
    public static readonly Color Warning = Color.Parse("#F39A4B");
    public static readonly Color Critical = Color.Parse("#F0645B");
    public static readonly Color Exhausted = Color.Parse("#E6495A");
    public static readonly Color Disconnected = Color.Parse("#7B8798");

    // Extra usage color ramp (4-tier)
    public static readonly Color ExtraCool = Color.Parse("#51A3CC");
    public static readonly Color ExtraWarm = Color.Parse("#7F4CBF");
    public static readonly Color ExtraHot = Color.Parse("#CC3D99");
    public static readonly Color ExtraCritical = Color.Parse("#D92B40");

    // UI colors
    public static readonly Color CardBackground = Color.Parse("#1A1E26");
    public static readonly Color SurfaceBackground = Color.Parse("#141922");
    public static readonly Color BorderColor = Color.Parse("#202A38");
    public static readonly Color TextPrimary = Color.Parse("#F5F7FB");
    public static readonly Color TextSecondary = Color.Parse("#8FA0B8");
    public static readonly Color TextTertiary = Color.Parse("#6B7A8D");
    public static readonly Color AppBackground = Color.Parse("#0F1115");

    public static Color ForHeadroomState(HeadroomState state) => state switch
    {
        HeadroomState.Normal => Normal,
        HeadroomState.Caution => Caution,
        HeadroomState.Warning => Warning,
        HeadroomState.Critical => Critical,
        HeadroomState.Exhausted => Exhausted,
        HeadroomState.Disconnected => Disconnected,
        _ => Disconnected,
    };

    public static Color ForExtraUsage(double utilization) => utilization switch
    {
        < 50 => ExtraCool,
        < 75 => ExtraWarm,
        < 90 => ExtraHot,
        _ => ExtraCritical,
    };
}
