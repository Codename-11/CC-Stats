namespace CCStats.Core.Models;

public sealed class OutagePeriod
{
    public long Id { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public double? DurationSeconds { get; init; }
    public string? ErrorType { get; init; }
    public bool IsOngoing => EndedAt is null;
}
