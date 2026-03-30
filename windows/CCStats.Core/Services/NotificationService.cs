using CCStats.Core.Models;

namespace CCStats.Core.Services;

public sealed class NotificationService
{
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new();
    private readonly TimeSpan _defaultCooldown = TimeSpan.FromMinutes(15);

    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public void ShowThresholdAlert(HeadroomState headroomState, double percentage)
    {
        var key = $"threshold_{headroomState}";
        if (IsOnCooldown(key)) return;

        var title = headroomState switch
        {
            HeadroomState.Warning => "Usage Warning",
            HeadroomState.Critical => "Usage Critical",
            HeadroomState.Exhausted => "Rate Limit Reached",
            _ => "Usage Update",
        };

        var body = headroomState switch
        {
            HeadroomState.Exhausted => "You have reached your rate limit. Usage will reset soon.",
            _ => $"Headroom is at {percentage:F0}%. Consider slowing down.",
        };

        RaiseNotification(key, title, body);
    }

    public void ShowPatternAlert(string title, string summary)
    {
        var key = $"pattern_{title}";
        if (IsOnCooldown(key)) return;

        RaiseNotification(key, title, summary);
    }

    public void ShowExtraUsageAlert(ExtraUsageAlertLevel level, double percentage)
    {
        var key = $"extra_usage_{level}";
        if (IsOnCooldown(key)) return;

        var title = "Extra Usage Alert";
        var body = level switch
        {
            ExtraUsageAlertLevel.Fifty => $"Extra usage is at {percentage:F0}% of your monthly limit.",
            ExtraUsageAlertLevel.SeventyFive => $"Extra usage is at {percentage:F0}% of your monthly limit.",
            ExtraUsageAlertLevel.Ninety => $"Extra usage is at {percentage:F0}% of your monthly limit. Nearing cap.",
            _ => $"Extra usage update: {percentage:F0}%.",
        };

        RaiseNotification(key, title, body);
    }

    public void ShowEnteredExtraUsageAlert()
    {
        RaiseNotification("entered_extra_usage",
            "Entered Extra Usage",
            "Your plan quota is exhausted. Additional usage is being billed at overage rates.");
    }

    public void ShowApiStatusAlert(string message)
    {
        var key = "api_status";
        if (IsOnCooldown(key)) return;

        RaiseNotification(key, "API Status", message);
    }

    public void ClearCooldowns()
    {
        _cooldowns.Clear();
    }

    private bool IsOnCooldown(string key)
    {
        if (_cooldowns.TryGetValue(key, out var lastShown))
        {
            if (DateTimeOffset.UtcNow - lastShown < _defaultCooldown)
            {
                return true;
            }

            // Entry expired — remove stale cooldown
            _cooldowns.Remove(key);
        }

        return false;
    }

    private void RaiseNotification(string key, string title, string body)
    {
        _cooldowns[key] = DateTimeOffset.UtcNow;
        NotificationRequested?.Invoke(this, new NotificationEventArgs(title, body));
    }
}

public sealed class NotificationEventArgs : EventArgs
{
    public string Title { get; }
    public string Body { get; }

    public NotificationEventArgs(string title, string body)
    {
        Title = title;
        Body = body;
    }
}

public enum ExtraUsageAlertLevel
{
    Fifty,
    SeventyFive,
    Ninety,
}
