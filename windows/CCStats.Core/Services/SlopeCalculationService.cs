using CCStats.Core.Models;

namespace CCStats.Core.Services;

public sealed class SlopeCalculationService
{
    private static readonly TimeSpan BufferWindow = TimeSpan.FromMinutes(10);

    // Rate thresholds in percent per minute
    private const double FlatThreshold = 0.3;
    private const double RisingThreshold = 1.5;

    private readonly object _lock = new();
    private readonly List<UtilizationSample> _samples = [];

    public void AddSample(double utilization, DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            _samples.Add(new UtilizationSample(utilization, timestamp));
            PruneOldSamples(timestamp);
        }
    }

    public SlopeLevel CalculateSlope()
    {
        lock (_lock)
        {
            if (_samples.Count < 2)
            {
                return SlopeLevel.Flat;
            }

            // Linear regression over the buffer window
            var ratePerMinute = CalculateRatePerMinute();

            return ratePerMinute switch
            {
                >= RisingThreshold => SlopeLevel.Steep,
                >= FlatThreshold => SlopeLevel.Rising,
                _ => SlopeLevel.Flat,
            };
        }
    }

    public double GetRatePerMinute()
    {
        lock (_lock)
        {
            return _samples.Count < 2 ? 0 : CalculateRatePerMinute();
        }
    }

    public void Bootstrap(IEnumerable<(double Utilization, DateTimeOffset Timestamp)> historicalSamples)
    {
        lock (_lock)
        {
            _samples.Clear();
            foreach (var (utilization, timestamp) in historicalSamples)
            {
                _samples.Add(new UtilizationSample(utilization, timestamp));
            }

            if (_samples.Count > 0)
            {
                PruneOldSamples(_samples[^1].Timestamp);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
        }
    }

    private double CalculateRatePerMinute()
    {
        if (_samples.Count < 2)
        {
            return 0;
        }

        var baseTime = _samples[0].Timestamp;

        // Calculate means
        double sumX = 0, sumY = 0;
        foreach (var sample in _samples)
        {
            sumX += (sample.Timestamp - baseTime).TotalMinutes;
            sumY += sample.Utilization;
        }

        var meanX = sumX / _samples.Count;
        var meanY = sumY / _samples.Count;

        // Calculate slope using least squares
        double numerator = 0, denominator = 0;
        foreach (var sample in _samples)
        {
            var x = (sample.Timestamp - baseTime).TotalMinutes;
            var dx = x - meanX;
            var dy = sample.Utilization - meanY;
            numerator += dx * dy;
            denominator += dx * dx;
        }

        return denominator == 0 ? 0 : numerator / denominator;
    }

    private void PruneOldSamples(DateTimeOffset now)
    {
        var cutoff = now - BufferWindow;
        _samples.RemoveAll(s => s.Timestamp < cutoff);
    }

    private sealed record UtilizationSample(double Utilization, DateTimeOffset Timestamp);
}
