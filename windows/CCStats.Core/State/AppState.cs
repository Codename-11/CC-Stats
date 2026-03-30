using CCStats.Core.Formatting;
using CCStats.Core.Models;

namespace CCStats.Core.State;

public sealed record AppState
{
    public WindowState? FiveHour { get; init; }
    public WindowState? SevenDay { get; init; }
    public ConnectionStatus ConnectionStatus { get; init; } = ConnectionStatus.Disconnected;
    public OAuthState OAuthState { get; init; } = OAuthState.Unauthenticated;
    public DateTimeOffset? LastUpdated { get; init; }
    public DateTimeOffset? LastAttempted { get; init; }
    public string? SubscriptionTier { get; init; }
    public StatusMessage? StatusMessage { get; init; }
    public SlopeLevel FiveHourSlope { get; init; } = SlopeLevel.Flat;
    public double FiveHourSlopeRate { get; init; }
    public SlopeLevel SevenDaySlope { get; init; } = SlopeLevel.Flat;
    public double SevenDaySlopeRate { get; init; }
    public CreditLimits? CreditLimits { get; init; }
    public bool ExtraUsageEnabled { get; init; }
    public int? ExtraUsageMonthlyLimitCents { get; init; }
    public int? ExtraUsageUsedCreditsCents { get; init; }
    public double? ExtraUsageUtilization { get; init; }
    public IReadOnlyList<double> SparklineData { get; init; } = Array.Empty<double>();
    public UsageSource DataSource { get; init; } = UsageSource.None;
    public int? CacheAgeSeconds { get; init; }

    public bool IsAuthenticated => OAuthState == OAuthState.Authenticated;
    public bool IsUnauthenticated => OAuthState == OAuthState.Unauthenticated;
    public bool IsAuthorizing => OAuthState == OAuthState.Authorizing;

    public double? QuotasRemaining
    {
        get
        {
            if (CreditLimits is null || CreditLimits.FiveHourCredits <= 0 || SevenDay is null)
            {
                return null;
            }

            var remainingSevenDayCredits = ((100.0 - SevenDay.Utilization) / 100.0) * CreditLimits.SevenDayCredits;
            return remainingSevenDayCredits / CreditLimits.FiveHourCredits;
        }
    }

    public DisplayedWindow DisplayedWindow
    {
        get
        {
            if (ConnectionStatus != ConnectionStatus.Connected || FiveHour is null)
            {
                return DisplayedWindow.FiveHour;
            }

            if (FiveHour.HeadroomState == HeadroomState.Exhausted)
            {
                return DisplayedWindow.FiveHour;
            }

            if (QuotasRemaining is { } quotas)
            {
                return quotas < 1.0 ? DisplayedWindow.SevenDay : DisplayedWindow.FiveHour;
            }

            var fiveHourHeadroom = FiveHour.HeadroomPercentage;
            var sevenDayHeadroom = SevenDay?.HeadroomPercentage ?? 0;
            if (SevenDay is not null &&
                SevenDay.HeadroomState is HeadroomState.Warning or HeadroomState.Critical &&
                sevenDayHeadroom < fiveHourHeadroom)
            {
                return DisplayedWindow.SevenDay;
            }

            return DisplayedWindow.FiveHour;
        }
    }

    public HeadroomState MenuBarHeadroomState => ConnectionStatus != ConnectionStatus.Connected
        ? HeadroomState.Disconnected
        : DisplayedWindow == DisplayedWindow.FiveHour
            ? FiveHour?.HeadroomState ?? HeadroomState.Disconnected
            : SevenDay?.HeadroomState ?? HeadroomState.Disconnected;

    public SlopeLevel DisplayedSlope => DisplayedWindow == DisplayedWindow.FiveHour ? FiveHourSlope : SevenDaySlope;

    public bool IsExtraUsageActive =>
        ExtraUsageEnabled &&
        (FiveHour?.HeadroomState == HeadroomState.Exhausted || SevenDay?.HeadroomState == HeadroomState.Exhausted);

    public int? ExtraUsageRemainingBalanceCents =>
        ExtraUsageMonthlyLimitCents is { } limit && ExtraUsageUsedCreditsCents is { } used ? limit - used : null;

    public string? MenuBarExtraUsageText
    {
        get
        {
            if (!IsExtraUsageActive)
            {
                return null;
            }

            if (ExtraUsageRemainingBalanceCents is { } remaining)
            {
                return FormatCents(remaining);
            }

            if (ExtraUsageUsedCreditsCents is { } used)
            {
                return $"{FormatCents(used)} spent";
            }

            return "$0.00";
        }
    }

    public string MenuBarText
    {
        get
        {
            if (MenuBarHeadroomState == HeadroomState.Disconnected)
            {
                return "\u2014";
            }

            if (MenuBarExtraUsageText is not null)
            {
                return MenuBarExtraUsageText;
            }

            var window = DisplayedWindow == DisplayedWindow.FiveHour ? FiveHour : SevenDay;
            if (window is not null && window.HeadroomState == HeadroomState.Exhausted && window.ResetsAt is { } resetsAt)
            {
                return $"\u21BB {DateTimeFormatting.CountdownString(resetsAt)}";
            }

            var headroom = Math.Max(0, (int)Math.Round(window?.HeadroomPercentage ?? 0));
            var slope = DisplayedSlope;
            return slope.IsActionable() && window?.HeadroomState != HeadroomState.Exhausted
                ? $"{headroom}% {slope.Arrow()}"
                : $"{headroom}%";
        }
    }

    public StatusMessage? ResolvedStatusMessage
    {
        get
        {
            return ConnectionStatus switch
            {
                ConnectionStatus.Disconnected when StatusMessage is not null => StatusMessage,
                ConnectionStatus.Disconnected when LastAttempted is null && LastUpdated is null =>
                    new StatusMessage("Unable to reach Claude API", "Attempting to connect..."),
                ConnectionStatus.Disconnected =>
                    new StatusMessage("Unable to reach Claude API", $"Last attempt: {DateTimeFormatting.RelativeTimeAgo((LastAttempted ?? LastUpdated)!.Value)}"),
                ConnectionStatus.TokenExpired => new StatusMessage("Session expired", "Sign in again to continue"),
                ConnectionStatus.NoCredentials => new StatusMessage("Not signed in", "Click Sign In to authenticate"),
                _ => StatusMessage,
            };
        }
    }

    public static string FormatCents(int cents, string symbol = "$")
    {
        var absolute = Math.Abs(cents);
        var dollars = absolute / 100;
        var remainder = absolute % 100;
        var prefix = cents < 0 ? "-" : string.Empty;
        return $"{prefix}{symbol}{dollars}.{remainder:00}";
    }

    public static AppState CreatePreviewSignedOut() => new()
    {
        OAuthState = OAuthState.Unauthenticated,
        ConnectionStatus = ConnectionStatus.NoCredentials,
        SparklineData = new[] { 12d, 14d, 20d, 18d, 17d, 22d, 19d, 15d, 16d, 14d, 13d, 12d },
    };

    public static AppState CreatePreviewAuthorizing() => new()
    {
        OAuthState = OAuthState.Authorizing,
        ConnectionStatus = ConnectionStatus.NoCredentials,
        StatusMessage = new StatusMessage("Waiting for browser auth...", "Complete sign-in in your browser, then return here."),
        SparklineData = new[] { 10d, 11d, 9d, 12d, 10d, 11d, 8d, 9d, 7d, 8d, 9d, 10d },
    };

    public static AppState CreatePreviewConnected() => new()
    {
        OAuthState = OAuthState.Authenticated,
        ConnectionStatus = ConnectionStatus.Connected,
        SubscriptionTier = "Max 5x",
        CreditLimits = new CreditLimits(3_300_000, 41_666_700, 100),
        LastUpdated = DateTimeOffset.Now.AddSeconds(-18),
        FiveHour = new WindowState(32, DateTimeOffset.Now.AddHours(2).AddMinutes(13)),
        SevenDay = new WindowState(41, DateTimeOffset.Now.AddDays(2).AddHours(4)),
        FiveHourSlope = SlopeLevel.Rising,
        SevenDaySlope = SlopeLevel.Flat,
        SparklineData = new[] { 22d, 24d, 21d, 28d, 34d, 33d, 39d, 46d, 43d, 49d, 52d, 58d, 55d, 63d, 60d, 68d },
    };

    public static AppState CreatePreviewCritical() => new()
    {
        OAuthState = OAuthState.Authenticated,
        ConnectionStatus = ConnectionStatus.Connected,
        SubscriptionTier = "Max 5x",
        CreditLimits = new CreditLimits(3_300_000, 41_666_700, 100),
        LastUpdated = DateTimeOffset.Now.AddSeconds(-9),
        FiveHour = new WindowState(97, DateTimeOffset.Now.AddMinutes(23)),
        SevenDay = new WindowState(88, DateTimeOffset.Now.AddDays(1).AddHours(7)),
        FiveHourSlope = SlopeLevel.Steep,
        SevenDaySlope = SlopeLevel.Rising,
        ExtraUsageEnabled = true,
        ExtraUsageMonthlyLimitCents = 7500,
        ExtraUsageUsedCreditsCents = 5600,
        ExtraUsageUtilization = 0.75,
        SparklineData = new[] { 38d, 40d, 42d, 45d, 48d, 53d, 57d, 62d, 67d, 71d, 76d, 82d, 88d, 91d, 94d, 97d },
    };

    public static AppState CreatePreviewDisconnected() => new()
    {
        OAuthState = OAuthState.Authenticated,
        ConnectionStatus = ConnectionStatus.Disconnected,
        SubscriptionTier = "Max 5x",
        LastUpdated = DateTimeOffset.Now.AddMinutes(-6),
        LastAttempted = DateTimeOffset.Now.AddSeconds(-32),
        StatusMessage = new StatusMessage("API temporarily unreachable", "Polling continues in the background."),
        SparklineData = new[] { 20d, 18d, 17d, 19d, 21d, 23d, 20d, 18d, 17d, 16d, 15d, 14d },
    };
}
