namespace CCStats.Core.Models;

public sealed record WindowState(double Utilization, DateTimeOffset? ResetsAt)
{
    public double HeadroomPercentage => Math.Max(0, 100.0 - Utilization);

    public HeadroomState HeadroomState => HeadroomStateHelpers.FromUtilization(Utilization);
}
