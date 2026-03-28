namespace CCStats.Core.Models;

public sealed class GapPeriod
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public double DurationMinutes { get; init; }
}
