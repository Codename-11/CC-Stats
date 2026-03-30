using CCStats.Core.Models;
using CCStats.Core.Services;

namespace CCStats.Tests;

public class SlopeCalculationServiceTests
{
    private readonly SlopeCalculationService _sut = new();
    private readonly DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Add 3 linearly-spaced samples over the given time span (meets minimum data requirements).</summary>
    private void AddLinearSamples(double startUtil, double endUtil, double totalMinutes)
    {
        var mid = (startUtil + endUtil) / 2.0;
        _sut.AddSample(startUtil, _now);
        _sut.AddSample(mid, _now.AddMinutes(totalMinutes / 2.0));
        _sut.AddSample(endUtil, _now.AddMinutes(totalMinutes));
    }

    // ── CalculateSlope: insufficient samples ──────────────────────────

    [Fact]
    public void CalculateSlope_NoSamples_ReturnsFlat()
    {
        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_OneSample_ReturnsFlat()
    {
        _sut.AddSample(50.0, _now);

        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
    }

    // ── CalculateSlope: flat (rate below 0.3%/min) ────────────────────

    [Fact]
    public void CalculateSlope_StableUtilization_ReturnsFlat()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(50.0, _now.AddMinutes(5));

        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_SlightIncreaseBelowThreshold_ReturnsFlat()
    {
        // 0.2%/min over 5 minutes = 1% total, well below 0.3%/min threshold
        _sut.AddSample(50.0, _now);
        _sut.AddSample(51.0, _now.AddMinutes(5));

        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_SlightDecreaseBelowThreshold_ReturnsFlat()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(49.0, _now.AddMinutes(5));

        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
    }

    // ── CalculateSlope: rising (0.3 - 1.5%/min) ──────────────────────

    [Fact]
    public void CalculateSlope_ModerateIncrease_ReturnsRising()
    {
        // 0.5%/min over 4 minutes = 2% total (3 samples, >2 min span)
        AddLinearSamples(50.0, 52.0, 4);

        Assert.Equal(SlopeLevel.Rising, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_AtRisingLowerBound_ReturnsRising()
    {
        // Exactly 0.3%/min: 1.5% over 5 minutes
        AddLinearSamples(50.0, 51.5, 5);

        Assert.Equal(SlopeLevel.Rising, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_JustBelowSteepThreshold_ReturnsRising()
    {
        // 1.4%/min over 5 minutes = 7% total
        AddLinearSamples(50.0, 57.0, 5);

        Assert.Equal(SlopeLevel.Rising, _sut.CalculateSlope());
    }

    // ── CalculateSlope: steep (>= 1.5%/min) ──────────────────────────

    [Fact]
    public void CalculateSlope_RapidIncrease_ReturnsSteep()
    {
        // 2.0%/min over 5 minutes = 10% total
        AddLinearSamples(50.0, 60.0, 5);

        Assert.Equal(SlopeLevel.Steep, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_AtSteepThreshold_ReturnsSteep()
    {
        // Exactly 1.5%/min: 7.5% over 5 minutes
        AddLinearSamples(50.0, 57.5, 5);

        Assert.Equal(SlopeLevel.Steep, _sut.CalculateSlope());
    }

    // ── CalculateSlope: declining (<= -0.3%/min) ─────────────────────

    [Fact]
    public void CalculateSlope_ModerateDecrease_ReturnsDeclining()
    {
        // -0.5%/min over 4 minutes = -2% total
        AddLinearSamples(50.0, 48.0, 4);

        Assert.Equal(SlopeLevel.Declining, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_RapidDecrease_ReturnsDeclining()
    {
        // -2.0%/min over 5 minutes = -10% total
        AddLinearSamples(50.0, 40.0, 5);

        Assert.Equal(SlopeLevel.Declining, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_AtDecliningThreshold_ReturnsDeclining()
    {
        // Exactly -0.3%/min: -1.5% over 5 minutes
        AddLinearSamples(50.0, 48.5, 5);

        Assert.Equal(SlopeLevel.Declining, _sut.CalculateSlope());
    }

    // ── CalculateSlope: multiple samples (linear regression) ─────────

    [Fact]
    public void CalculateSlope_MultipleSamplesRising_ReturnsRising()
    {
        // Steady increase of 0.5%/min across several samples
        _sut.AddSample(50.0, _now);
        _sut.AddSample(51.0, _now.AddMinutes(2));
        _sut.AddSample(52.0, _now.AddMinutes(4));
        _sut.AddSample(53.0, _now.AddMinutes(6));

        Assert.Equal(SlopeLevel.Rising, _sut.CalculateSlope());
    }

    [Fact]
    public void CalculateSlope_MultipleSamplesSteep_ReturnsSteep()
    {
        // Rapid increase of 2%/min
        _sut.AddSample(50.0, _now);
        _sut.AddSample(54.0, _now.AddMinutes(2));
        _sut.AddSample(58.0, _now.AddMinutes(4));
        _sut.AddSample(62.0, _now.AddMinutes(6));

        Assert.Equal(SlopeLevel.Steep, _sut.CalculateSlope());
    }

    // ── GetRatePerMinute ─────────────────────────────────────────────

    [Fact]
    public void GetRatePerMinute_NoSamples_ReturnsZero()
    {
        Assert.Equal(0.0, _sut.GetRatePerMinute());
    }

    [Fact]
    public void GetRatePerMinute_OneSample_ReturnsZero()
    {
        _sut.AddSample(50.0, _now);

        Assert.Equal(0.0, _sut.GetRatePerMinute());
    }

    [Fact]
    public void GetRatePerMinute_TwoSamples_ReturnsZero_InsufficientData()
    {
        // Only 2 samples — now requires 3+ samples over 2+ min
        _sut.AddSample(50.0, _now);
        _sut.AddSample(60.0, _now.AddMinutes(5));

        Assert.Equal(0.0, _sut.GetRatePerMinute());
    }

    [Fact]
    public void GetRatePerMinute_ThreeSamples_ReturnsCorrectRate()
    {
        // 10% over 5 minutes = 2%/min (3 samples, >2 min span)
        AddLinearSamples(50.0, 60.0, 5);

        Assert.Equal(2.0, _sut.GetRatePerMinute(), precision: 6);
    }

    [Fact]
    public void GetRatePerMinute_NegativeSlope_ReturnsNegativeRate()
    {
        // -5% over 5 minutes = -1%/min
        AddLinearSamples(50.0, 45.0, 5);

        Assert.Equal(-1.0, _sut.GetRatePerMinute(), precision: 6);
    }

    [Fact]
    public void GetRatePerMinute_MultipleSamples_ReturnsLinearRegressionSlope()
    {
        // Perfectly linear: 1%/min
        _sut.AddSample(50.0, _now);
        _sut.AddSample(52.0, _now.AddMinutes(2));
        _sut.AddSample(54.0, _now.AddMinutes(4));

        Assert.Equal(1.0, _sut.GetRatePerMinute(), precision: 6);
    }

    [Fact]
    public void GetRatePerMinute_SameTimestamp_ReturnsZero()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(60.0, _now);

        Assert.Equal(0.0, _sut.GetRatePerMinute());
    }

    // ── Pruning (10-minute buffer window) ────────────────────────────

    [Fact]
    public void AddSample_PrunesOlderThan10Minutes()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(53.0, _now.AddMinutes(3));
        _sut.AddSample(55.0, _now.AddMinutes(5));
        _sut.AddSample(57.0, _now.AddMinutes(7));

        // This sample is 11 minutes after the first; first should be pruned
        _sut.AddSample(60.0, _now.AddMinutes(11));

        // First sample (at _now) pruned, leaving 4 samples over 8 min span
        // Rate: ~0.87%/min → Rising
        var rate = _sut.GetRatePerMinute();
        Assert.True(rate > 0.3 && rate < 1.5,
            $"Expected Rising-range rate after pruning, got {rate}%/min");
        Assert.Equal(SlopeLevel.Rising, _sut.CalculateSlope());
    }

    [Fact]
    public void AddSample_KeepsSamplesExactly10MinutesOld()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(55.0, _now.AddMinutes(5));
        // Sample at exactly 10 minutes: cutoff = now+10 - 10 = now, so sample at
        // _now is NOT < cutoff, it is equal, so it should be kept.
        _sut.AddSample(60.0, _now.AddMinutes(10));

        // All 3 samples present, giving 1%/min
        Assert.Equal(1.0, _sut.GetRatePerMinute(), precision: 6);
    }

    [Fact]
    public void AddSample_PrunesAllOldSamples()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(55.0, _now.AddMinutes(1));
        _sut.AddSample(60.0, _now.AddMinutes(2));

        // Jump ahead by 15 minutes; all previous samples older than 10 min window
        _sut.AddSample(70.0, _now.AddMinutes(15));

        // Only one sample remains, so slope is Flat
        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
        Assert.Equal(0.0, _sut.GetRatePerMinute());
    }

    // ── Clear ────────────────────────────────────────────────────────

    [Fact]
    public void Clear_EmptiesAllSamples()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(60.0, _now.AddMinutes(2));

        _sut.Clear();

        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
        Assert.Equal(0.0, _sut.GetRatePerMinute());
    }

    [Fact]
    public void Clear_ThenAddNewSamples_WorksCorrectly()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(55.0, _now.AddMinutes(2));
        _sut.AddSample(60.0, _now.AddMinutes(4));

        _sut.Clear();

        // 3 samples over 4 min, 5%/min -> Steep
        _sut.AddSample(10.0, _now.AddMinutes(10));
        _sut.AddSample(20.0, _now.AddMinutes(12));
        _sut.AddSample(30.0, _now.AddMinutes(14));

        Assert.Equal(SlopeLevel.Steep, _sut.CalculateSlope());
    }

    // ── Bootstrap ────────────────────────────────────────────────────

    [Fact]
    public void Bootstrap_LoadsHistoricalSamples()
    {
        var historical = new List<(double, DateTimeOffset)>
        {
            (50.0, _now),
            (52.0, _now.AddMinutes(2)),
            (54.0, _now.AddMinutes(4)),
        };

        _sut.Bootstrap(historical);

        // 1%/min -> Rising
        Assert.Equal(SlopeLevel.Rising, _sut.CalculateSlope());
        Assert.Equal(1.0, _sut.GetRatePerMinute(), precision: 6);
    }

    [Fact]
    public void Bootstrap_PrunesOldSamples()
    {
        var historical = new List<(double, DateTimeOffset)>
        {
            (10.0, _now),                    // older than 10 min from latest — will be pruned
            (50.0, _now.AddMinutes(5)),
            (53.0, _now.AddMinutes(8)),
            (55.0, _now.AddMinutes(11)),     // latest -> cutoff = +1 min
        };

        _sut.Bootstrap(historical);

        // The first sample at _now should be pruned (11 min before latest).
        // Remaining 3 samples: +5, +8, +11 => ~0.83%/min -> Rising
        Assert.Equal(SlopeLevel.Rising, _sut.CalculateSlope());
    }

    [Fact]
    public void Bootstrap_ReplacesExistingSamples()
    {
        _sut.AddSample(90.0, _now);
        _sut.AddSample(95.0, _now.AddMinutes(1));

        var historical = new List<(double, DateTimeOffset)>
        {
            (50.0, _now.AddMinutes(5)),
            (50.0, _now.AddMinutes(7)),
        };

        _sut.Bootstrap(historical);

        // Old samples replaced; new ones are flat
        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
    }

    [Fact]
    public void Bootstrap_EmptyCollection_ClearsSamples()
    {
        _sut.AddSample(50.0, _now);
        _sut.AddSample(60.0, _now.AddMinutes(2));

        _sut.Bootstrap([]);

        Assert.Equal(SlopeLevel.Flat, _sut.CalculateSlope());
        Assert.Equal(0.0, _sut.GetRatePerMinute());
    }

    // ── Thread safety ────────────────────────────────────────────────

    [Fact]
    public void ConcurrentAddSample_DoesNotThrow()
    {
        var tasks = new Task[100];
        for (var i = 0; i < tasks.Length; i++)
        {
            var offset = i;
            tasks[i] = Task.Run(() =>
                _sut.AddSample(50.0 + offset * 0.1, _now.AddSeconds(offset)));
        }

        Task.WaitAll(tasks);

        // Should not throw and should return a valid result
        var slope = _sut.CalculateSlope();
        Assert.True(Enum.IsDefined(slope));
    }

    [Fact]
    public void ConcurrentMixedOperations_DoesNotThrow()
    {
        var tasks = new Task[200];
        for (var i = 0; i < tasks.Length; i++)
        {
            var offset = i;
            tasks[i] = (offset % 4) switch
            {
                0 => Task.Run(() => _sut.AddSample(50.0 + offset, _now.AddSeconds(offset))),
                1 => Task.Run(() => _sut.CalculateSlope()),
                2 => Task.Run(() => _sut.GetRatePerMinute()),
                _ => Task.Run(() => _sut.Clear()),
            };
        }

        Task.WaitAll(tasks);

        // Should not throw; any result is valid after concurrent clears
        Assert.True(Enum.IsDefined(_sut.CalculateSlope()));
    }
}
