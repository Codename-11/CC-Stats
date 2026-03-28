using CCStats.Core.Models;

namespace CCStats.Core.Services;

public sealed class HistoricalDataService
{
    private readonly DatabaseManager _database;

    private double? _lastFiveHourUtil;
    private double? _lastSevenDayUtil;

    public HistoricalDataService(DatabaseManager database)
    {
        _database = database;
    }

    public async Task RecordPollAsync(
        double fiveHourUtil,
        double sevenDayUtil,
        DateTimeOffset? fiveHourResetsAt,
        DateTimeOffset? sevenDayResetsAt)
    {
        var now = DateTimeOffset.UtcNow;

        // Detect reset boundaries
        if (_lastFiveHourUtil.HasValue && fiveHourUtil < _lastFiveHourUtil.Value - 10)
        {
            await _database.InsertResetEventAsync(now, "five_hour", _lastFiveHourUtil.Value, fiveHourUtil);
        }

        if (_lastSevenDayUtil.HasValue && sevenDayUtil < _lastSevenDayUtil.Value - 10)
        {
            await _database.InsertResetEventAsync(now, "seven_day", _lastSevenDayUtil.Value, sevenDayUtil);
        }

        _lastFiveHourUtil = fiveHourUtil;
        _lastSevenDayUtil = sevenDayUtil;

        var poll = new UsagePoll(
            Id: 0,
            Timestamp: now,
            FiveHourUtilization: fiveHourUtil,
            FiveHourResetsAt: fiveHourResetsAt,
            SevenDayUtilization: sevenDayUtil,
            SevenDayResetsAt: sevenDayResetsAt);

        await _database.InsertPollAsync(poll);
    }

    public async Task<IReadOnlyList<double>> GetSparklineDataAsync(int hours = 24)
    {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddHours(-hours);
        var polls = await _database.QueryPollsAsync(from, now);

        if (polls.Count == 0)
        {
            return Array.Empty<double>();
        }

        // Downsample to ~24 points for sparkline
        var targetPoints = 24;
        if (polls.Count <= targetPoints)
        {
            return polls.Select(p => p.FiveHourUtilization).ToList();
        }

        var step = (double)polls.Count / targetPoints;
        var result = new List<double>(targetPoints);
        for (var i = 0; i < targetPoints; i++)
        {
            var index = (int)(i * step);
            result.Add(polls[index].FiveHourUtilization);
        }

        return result;
    }

    public async Task<IReadOnlyList<UsagePoll>> GetAnalyticsDataAsync(AnalyticsTimeRange timeRange)
    {
        var now = DateTimeOffset.UtcNow;
        var from = timeRange switch
        {
            AnalyticsTimeRange.OneHour => now.AddHours(-1),
            AnalyticsTimeRange.SixHours => now.AddHours(-6),
            AnalyticsTimeRange.TwentyFourHours => now.AddHours(-24),
            AnalyticsTimeRange.SevenDays => now.AddDays(-7),
            AnalyticsTimeRange.ThirtyDays => now.AddDays(-30),
            AnalyticsTimeRange.NinetyDays => now.AddDays(-90),
            _ => now.AddHours(-24),
        };

        var polls = await _database.QueryPollsAsync(from, now);

        // Apply resolution tiers
        return timeRange switch
        {
            AnalyticsTimeRange.OneHour or AnalyticsTimeRange.SixHours or AnalyticsTimeRange.TwentyFourHours
                => polls, // Raw data
            AnalyticsTimeRange.SevenDays
                => AggregatePolls(polls, TimeSpan.FromMinutes(5)),  // 5-min rollups
            AnalyticsTimeRange.ThirtyDays
                => AggregatePolls(polls, TimeSpan.FromHours(1)),    // Hourly rollups
            AnalyticsTimeRange.NinetyDays
                => AggregatePolls(polls, TimeSpan.FromDays(1)),     // Daily rollups
            _ => polls,
        };
    }

    public async Task CreateRollupsAsync(DateTimeOffset from, DateTimeOffset to, TimeSpan resolution)
    {
        var polls = await _database.QueryPollsAsync(from, to);
        if (polls.Count == 0) return;

        var resolutionName = resolution.TotalHours switch
        {
            < 1 => "5min",
            < 24 => "hourly",
            _ => "daily",
        };

        var buckets = polls
            .GroupBy(p => new DateTimeOffset(
                p.Timestamp.Ticks - (p.Timestamp.Ticks % resolution.Ticks), p.Timestamp.Offset))
            .OrderBy(g => g.Key);

        foreach (var bucket in buckets)
        {
            var items = bucket.ToList();
            var periodStart = bucket.Key;
            var periodEnd = periodStart + resolution;

            await _database.InsertRollupAsync(
                periodStart, periodEnd, resolutionName,
                avgFiveHour: items.Average(p => p.FiveHourUtilization),
                maxFiveHour: items.Max(p => p.FiveHourUtilization),
                minFiveHour: items.Min(p => p.FiveHourUtilization),
                avgSevenDay: items.All(p => p.SevenDayUtilization.HasValue)
                    ? items.Average(p => p.SevenDayUtilization!.Value) : null,
                maxSevenDay: items.All(p => p.SevenDayUtilization.HasValue)
                    ? items.Max(p => p.SevenDayUtilization!.Value) : null,
                minSevenDay: items.All(p => p.SevenDayUtilization.HasValue)
                    ? items.Min(p => p.SevenDayUtilization!.Value) : null,
                sampleCount: items.Count);
        }
    }

    /// <summary>Detects gaps in poll data where polls are missing for longer than the threshold.</summary>
    public async Task<IReadOnlyList<GapPeriod>> DetectGapsAsync(int hours = 24, int gapThresholdMinutes = 60)
    {
        var from = DateTimeOffset.UtcNow.AddHours(-hours);
        var to = DateTimeOffset.UtcNow;
        var polls = await _database.QueryPollsAsync(from, to);

        var gaps = new List<GapPeriod>();
        var threshold = TimeSpan.FromMinutes(gapThresholdMinutes);

        for (int i = 1; i < polls.Count; i++)
        {
            var delta = polls[i].Timestamp - polls[i - 1].Timestamp;
            if (delta > threshold)
            {
                gaps.Add(new GapPeriod
                {
                    Start = polls[i - 1].Timestamp,
                    End = polls[i].Timestamp,
                    DurationMinutes = delta.TotalMinutes,
                });
            }
        }

        return gaps;
    }

    /// <summary>Queries outage periods from the database.</summary>
    public async Task<IReadOnlyList<OutagePeriod>> GetOutagesAsync(int hours = 24)
    {
        var from = DateTimeOffset.UtcNow.AddHours(-hours);
        var to = DateTimeOffset.UtcNow;
        return await _database.QueryOutagesAsync(from, to);
    }

    private static IReadOnlyList<UsagePoll> AggregatePolls(IReadOnlyList<UsagePoll> polls, TimeSpan interval)
    {
        if (polls.Count == 0) return polls;

        return polls
            .GroupBy(p => new DateTimeOffset(
                p.Timestamp.Ticks - (p.Timestamp.Ticks % interval.Ticks), p.Timestamp.Offset))
            .OrderBy(g => g.Key)
            .Select(g => new UsagePoll(
                Id: 0,
                Timestamp: g.Key,
                FiveHourUtilization: g.Average(p => p.FiveHourUtilization),
                FiveHourResetsAt: g.Last().FiveHourResetsAt,
                SevenDayUtilization: g.All(p => p.SevenDayUtilization.HasValue)
                    ? g.Average(p => p.SevenDayUtilization!.Value) : null,
                SevenDayResetsAt: g.Last().SevenDayResetsAt))
            .ToList();
    }
}

public enum AnalyticsTimeRange
{
    OneHour,
    SixHours,
    TwentyFourHours,
    SevenDays,
    ThirtyDays,
    NinetyDays,
}
