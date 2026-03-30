using CCStats.Core.Formatting;

namespace CCStats.Tests;

public class DateTimeFormattingTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);

    // ── RelativeTimeAgo ──────────────────────────────────────────────

    [Fact]
    public void RelativeTimeAgo_ZeroElapsed_ReturnsJustNow()
    {
        var result = DateTimeFormatting.RelativeTimeAgo(Now, now: Now);
        Assert.Equal("just now", result);
    }

    [Fact]
    public void RelativeTimeAgo_NegativeElapsed_ReturnsJustNow()
    {
        var future = Now.AddSeconds(30);
        var result = DateTimeFormatting.RelativeTimeAgo(future, now: Now);
        Assert.Equal("just now", result);
    }

    [Fact]
    public void RelativeTimeAgo_UnderOneSecond_ReturnsJustNow()
    {
        var timestamp = Now.AddMilliseconds(-999);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("just now", result);
    }

    [Fact]
    public void RelativeTimeAgo_ExactlyOneSecond_ReturnsSecondsAgo()
    {
        var timestamp = Now.AddSeconds(-1);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("1s ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_59Seconds_ReturnsSecondsAgo()
    {
        var timestamp = Now.AddSeconds(-59);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("59s ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_Exactly60Seconds_ReturnsMinutesAgo()
    {
        var timestamp = Now.AddSeconds(-60);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("1m ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_59Minutes_ReturnsMinutesAgo()
    {
        var timestamp = Now.AddMinutes(-59);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("59m ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_Exactly60Minutes_ReturnsHoursAgo()
    {
        var timestamp = Now.AddMinutes(-60);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("1h ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_HoursWithMinutes_ReturnsHoursAndMinutes()
    {
        var timestamp = Now.AddHours(-2).AddMinutes(-30);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("2h 30m ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_ExactHours_OmitsMinutes()
    {
        var timestamp = Now.AddHours(-3);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("3h ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_23Hours59Minutes_StillShowsHours()
    {
        var timestamp = Now.AddHours(-23).AddMinutes(-59);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("23h 59m ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_Exactly24Hours_ReturnsDays()
    {
        var timestamp = Now.AddHours(-24);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("1d ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_DaysWithHours_ReturnsDaysAndHours()
    {
        var timestamp = Now.AddDays(-2).AddHours(-5);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("2d 5h ago", result);
    }

    [Fact]
    public void RelativeTimeAgo_ExactDays_OmitsHours()
    {
        var timestamp = Now.AddDays(-3);
        var result = DateTimeFormatting.RelativeTimeAgo(timestamp, now: Now);
        Assert.Equal("3d ago", result);
    }

    // ── CountdownString ──────────────────────────────────────────────

    [Fact]
    public void CountdownString_PastTimestamp_ReturnsZeroMinutes()
    {
        var past = Now.AddMinutes(-10);
        var result = DateTimeFormatting.CountdownString(past, now: Now);
        Assert.Equal("0m", result);
    }

    [Fact]
    public void CountdownString_ZeroRemaining_ReturnsZeroMinutes()
    {
        var result = DateTimeFormatting.CountdownString(Now, now: Now);
        Assert.Equal("0m", result);
    }

    [Fact]
    public void CountdownString_LessThanOneMinute_ReturnsZeroMinutes()
    {
        var timestamp = Now.AddSeconds(59);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("0m", result);
    }

    [Fact]
    public void CountdownString_ExactlyOneMinute_ReturnsOneMinute()
    {
        var timestamp = Now.AddMinutes(1);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("1m", result);
    }

    [Fact]
    public void CountdownString_59Minutes_Returns59Minutes()
    {
        var timestamp = Now.AddMinutes(59);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("59m", result);
    }

    [Fact]
    public void CountdownString_Exactly60Minutes_ReturnsHoursAndMinutes()
    {
        var timestamp = Now.AddMinutes(60);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("1h 0m", result);
    }

    [Fact]
    public void CountdownString_HoursAndMinutes_FormatsCorrectly()
    {
        var timestamp = Now.AddHours(2).AddMinutes(30);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("2h 30m", result);
    }

    [Fact]
    public void CountdownString_23Hours59Minutes_FormatsCorrectly()
    {
        var timestamp = Now.AddHours(23).AddMinutes(59);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("23h 59m", result);
    }

    [Fact]
    public void CountdownString_Exactly24Hours_ReturnsDaysAndHours()
    {
        var timestamp = Now.AddHours(24);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("1d 0h", result);
    }

    [Fact]
    public void CountdownString_MultiDay_ReturnsDaysAndHours()
    {
        var timestamp = Now.AddDays(3).AddHours(5);
        var result = DateTimeFormatting.CountdownString(timestamp, now: Now);
        Assert.Equal("3d 5h", result);
    }

    [Fact]
    public void CountdownString_NegativeRemaining_ReturnsZeroMinutes()
    {
        var past = Now.AddHours(-2);
        var result = DateTimeFormatting.CountdownString(past, now: Now);
        Assert.Equal("0m", result);
    }

    // ── AbsoluteTimeString ───────────────────────────────────────────

    [Fact]
    public void AbsoluteTimeString_SameDay_ShowsTimeOnly()
    {
        var timestamp = new DateTimeOffset(2026, 3, 29, 14, 30, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var result = DateTimeFormatting.AbsoluteTimeString(timestamp, now: now);
        // Should contain "at" and time in h:mm tt format, no day name
        Assert.StartsWith("at ", result);
        Assert.DoesNotContain("Sun", result);
        Assert.DoesNotContain("Mon", result);
        Assert.DoesNotContain("Tue", result);
        Assert.DoesNotContain("Wed", result);
        Assert.DoesNotContain("Thu", result);
        Assert.DoesNotContain("Fri", result);
        Assert.DoesNotContain("Sat", result);
    }

    [Fact]
    public void AbsoluteTimeString_DifferentDay_ShowsDayAndTime()
    {
        var timestamp = new DateTimeOffset(2026, 3, 30, 9, 15, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var result = DateTimeFormatting.AbsoluteTimeString(timestamp, now: now);
        // Should contain "at" and day abbreviation in ddd h:mm tt format
        Assert.StartsWith("at ", result);
        // 2026-03-30 is a Monday
        Assert.Contains("Mon", result);
    }

    [Fact]
    public void AbsoluteTimeString_SameDay_FormatsCorrectly()
    {
        // Use local offset so .LocalDateTime is a no-op
        var offset = DateTimeOffset.Now.Offset;
        var timestamp = new DateTimeOffset(2026, 3, 29, 15, 5, 0, offset);
        var now = new DateTimeOffset(2026, 3, 29, 10, 0, 0, offset);
        var result = DateTimeFormatting.AbsoluteTimeString(timestamp, now: now);
        // h:mm tt format: 3:05 PM
        Assert.Contains("3:05", result);
        Assert.Contains("PM", result);
    }

    [Fact]
    public void AbsoluteTimeString_MidnightBoundary_DifferentDays()
    {
        // Use local offset so .LocalDateTime preserves the date
        var offset = DateTimeOffset.Now.Offset;
        var timestamp = new DateTimeOffset(2026, 3, 30, 0, 0, 0, offset);
        var now = new DateTimeOffset(2026, 3, 29, 23, 59, 0, offset);
        var result = DateTimeFormatting.AbsoluteTimeString(timestamp, now: now);
        // Different calendar day, should include day name
        // 2026-03-30 is a Monday
        Assert.Contains("Mon", result);
    }
}
