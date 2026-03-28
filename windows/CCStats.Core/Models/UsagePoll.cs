namespace CCStats.Core.Models;

public record UsagePoll(
    long Id,
    DateTimeOffset Timestamp,
    double FiveHourUtilization,
    DateTimeOffset? FiveHourResetsAt,
    double? SevenDayUtilization,
    DateTimeOffset? SevenDayResetsAt);
