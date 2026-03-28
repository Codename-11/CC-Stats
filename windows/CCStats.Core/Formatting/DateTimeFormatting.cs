namespace CCStats.Core.Formatting;

public static class DateTimeFormatting
{
    public static string RelativeTimeAgo(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.Now;
        var elapsed = current - timestamp;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 1)
        {
            return "just now";
        }

        if (elapsed.TotalMinutes < 1)
        {
            return $"{(int)elapsed.TotalSeconds}s ago";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed.TotalDays < 1)
        {
            var hours = (int)elapsed.TotalHours;
            var minutes = elapsed.Minutes;
            return minutes > 0 ? $"{hours}h {minutes}m ago" : $"{hours}h ago";
        }

        var days = (int)elapsed.TotalDays;
        return elapsed.Hours > 0 ? $"{days}d {elapsed.Hours}h ago" : $"{days}d ago";
    }

    public static string CountdownString(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.Now;
        var remaining = timestamp - current;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var totalMinutes = (int)remaining.TotalMinutes;
        if (totalMinutes <= 0)
        {
            return "0m";
        }

        var totalHours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (totalHours == 0)
        {
            return $"{totalMinutes}m";
        }

        var days = totalHours / 24;
        var hours = totalHours % 24;
        if (days > 0)
        {
            return $"{days}d {hours}h";
        }

        return $"{totalHours}h {minutes}m";
    }

    public static string AbsoluteTimeString(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.Now;
        var format = timestamp.Date == current.Date ? "h:mm tt" : "ddd h:mm tt";
        return $"at {timestamp.LocalDateTime.ToString(format)}";
    }
}
