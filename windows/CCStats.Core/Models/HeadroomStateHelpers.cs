namespace CCStats.Core.Models;

public static class HeadroomStateHelpers
{
    public static HeadroomState FromUtilization(double? utilization)
    {
        if (utilization is null)
        {
            return HeadroomState.Disconnected;
        }

        var headroom = 100.0 - utilization.Value;

        return headroom switch
        {
            <= 0 => HeadroomState.Exhausted,
            < 5 => HeadroomState.Critical,
            < 20 => HeadroomState.Warning,
            <= 40 => HeadroomState.Caution,
            _ => HeadroomState.Normal,
        };
    }
}
