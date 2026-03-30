using CCStats.Core.Models;
using CCStats.Core.State;

namespace CCStats.Tests;

public class WindowStateTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(32, 68)]
    [InlineData(50, 50)]
    [InlineData(100, 0)]
    [InlineData(99.5, 0.5)]
    public void HeadroomPercentage_Returns_100_Minus_Utilization(double utilization, double expected)
    {
        var ws = new WindowState(utilization, null);
        Assert.Equal(expected, ws.HeadroomPercentage);
    }

    [Fact]
    public void HeadroomPercentage_Clamps_At_Zero_When_Over_100()
    {
        var ws = new WindowState(120, null);
        Assert.Equal(0, ws.HeadroomPercentage);
    }

    [Theory]
    [InlineData(0, HeadroomState.Normal)]       // 100% headroom
    [InlineData(59, HeadroomState.Normal)]       // 41% headroom
    [InlineData(60, HeadroomState.Caution)]      // 40% headroom (boundary)
    [InlineData(70, HeadroomState.Caution)]      // 30% headroom
    [InlineData(80, HeadroomState.Caution)]      // 20% headroom (boundary)
    [InlineData(80.1, HeadroomState.Warning)]    // 19.9% headroom
    [InlineData(90, HeadroomState.Warning)]      // 10% headroom
    [InlineData(95, HeadroomState.Warning)]      // 5% headroom
    [InlineData(95.1, HeadroomState.Critical)]   // 4.9% headroom
    [InlineData(99, HeadroomState.Critical)]      // 1% headroom
    [InlineData(100, HeadroomState.Exhausted)]   // 0% headroom
    [InlineData(105, HeadroomState.Exhausted)]   // negative headroom
    public void HeadroomState_Thresholds(double utilization, HeadroomState expected)
    {
        var ws = new WindowState(utilization, null);
        Assert.Equal(expected, ws.HeadroomState);
    }
}

public class AppStateAuthTests
{
    [Fact]
    public void IsAuthenticated_True_When_Authenticated()
    {
        var state = new AppState { OAuthState = OAuthState.Authenticated };
        Assert.True(state.IsAuthenticated);
        Assert.False(state.IsUnauthenticated);
        Assert.False(state.IsAuthorizing);
    }

    [Fact]
    public void IsUnauthenticated_True_When_Unauthenticated()
    {
        var state = new AppState { OAuthState = OAuthState.Unauthenticated };
        Assert.True(state.IsUnauthenticated);
        Assert.False(state.IsAuthenticated);
        Assert.False(state.IsAuthorizing);
    }

    [Fact]
    public void IsAuthorizing_True_When_Authorizing()
    {
        var state = new AppState { OAuthState = OAuthState.Authorizing };
        Assert.True(state.IsAuthorizing);
        Assert.False(state.IsAuthenticated);
        Assert.False(state.IsUnauthenticated);
    }

    [Fact]
    public void Default_State_Is_Unauthenticated()
    {
        var state = new AppState();
        Assert.True(state.IsUnauthenticated);
    }
}

public class AppStateDisplayedWindowTests
{
    [Fact]
    public void Disconnected_Returns_FiveHour()
    {
        var state = new AppState { ConnectionStatus = ConnectionStatus.Disconnected };
        Assert.Equal(DisplayedWindow.FiveHour, state.DisplayedWindow);
    }

    [Fact]
    public void Connected_Without_FiveHour_Returns_FiveHour()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = null,
        };
        Assert.Equal(DisplayedWindow.FiveHour, state.DisplayedWindow);
    }

    [Fact]
    public void FiveHour_Exhausted_Returns_FiveHour()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(100, null),
        };
        Assert.Equal(DisplayedWindow.FiveHour, state.DisplayedWindow);
    }

    [Fact]
    public void QuotasRemaining_Below_One_Returns_SevenDay()
    {
        // Set up so that remaining seven-day credits / five-hour credits < 1.0
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(50, null),
            SevenDay = new WindowState(99, null), // only 1% remaining of 7-day
            CreditLimits = new CreditLimits(3_300_000, 41_666_700),
        };
        // QuotasRemaining = (1% * 41_666_700) / 3_300_000 = ~0.126 < 1.0
        Assert.Equal(DisplayedWindow.SevenDay, state.DisplayedWindow);
    }

    [Fact]
    public void QuotasRemaining_Above_One_Returns_FiveHour()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(50, null),
            SevenDay = new WindowState(50, null),
            CreditLimits = new CreditLimits(3_300_000, 41_666_700),
        };
        // QuotasRemaining = (50% * 41_666_700) / 3_300_000 = ~6.3 > 1.0
        Assert.Equal(DisplayedWindow.FiveHour, state.DisplayedWindow);
    }

    [Fact]
    public void SevenDay_Warning_Lower_Than_FiveHour_Returns_SevenDay()
    {
        // No CreditLimits so QuotasRemaining is null, falls through to headroom comparison
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(50, null),  // 50% headroom
            SevenDay = new WindowState(90, null),   // 10% headroom = Warning
        };
        Assert.Equal(DisplayedWindow.SevenDay, state.DisplayedWindow);
    }

    [Fact]
    public void SevenDay_Normal_Does_Not_Override_FiveHour()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(50, null),
            SevenDay = new WindowState(30, null), // 70% headroom = Normal
        };
        Assert.Equal(DisplayedWindow.FiveHour, state.DisplayedWindow);
    }
}

public class AppStateResolvedStatusMessageTests
{
    [Fact]
    public void Disconnected_With_StatusMessage_Returns_It()
    {
        var msg = new StatusMessage("Custom error", "Details here");
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Disconnected,
            StatusMessage = msg,
        };
        Assert.Equal(msg, state.ResolvedStatusMessage);
    }

    [Fact]
    public void Disconnected_No_StatusMessage_No_LastAttempted_No_LastUpdated()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Disconnected,
        };
        var resolved = state.ResolvedStatusMessage!;
        Assert.Equal("Unable to reach Claude API", resolved.Title);
        Assert.Equal("Attempting to connect...", resolved.Detail);
    }

    [Fact]
    public void Disconnected_No_StatusMessage_With_LastAttempted()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Disconnected,
            LastAttempted = DateTimeOffset.Now.AddMinutes(-2),
        };
        var resolved = state.ResolvedStatusMessage!;
        Assert.Equal("Unable to reach Claude API", resolved.Title);
        Assert.StartsWith("Last attempt:", resolved.Detail);
    }

    [Fact]
    public void Disconnected_No_StatusMessage_With_LastUpdated_Only()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Disconnected,
            LastUpdated = DateTimeOffset.Now.AddMinutes(-5),
        };
        var resolved = state.ResolvedStatusMessage!;
        Assert.Equal("Unable to reach Claude API", resolved.Title);
        Assert.StartsWith("Last attempt:", resolved.Detail);
    }

    [Fact]
    public void TokenExpired_Returns_SessionExpired()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.TokenExpired,
        };
        var resolved = state.ResolvedStatusMessage!;
        Assert.Equal("Session expired", resolved.Title);
        Assert.Equal("Sign in again to continue", resolved.Detail);
    }

    [Fact]
    public void NoCredentials_Returns_NotSignedIn()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.NoCredentials,
        };
        var resolved = state.ResolvedStatusMessage!;
        Assert.Equal("Not signed in", resolved.Title);
        Assert.Contains("Sign In", resolved.Detail);
    }

    [Fact]
    public void Connected_Without_StatusMessage_Returns_Null()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
        };
        Assert.Null(state.ResolvedStatusMessage);
    }

    [Fact]
    public void Connected_With_StatusMessage_Returns_It()
    {
        var msg = new StatusMessage("Info", "Some info");
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            StatusMessage = msg,
        };
        Assert.Equal(msg, state.ResolvedStatusMessage);
    }
}

public class AppStateMenuBarTextTests
{
    [Fact]
    public void Disconnected_Returns_EmDash()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Disconnected,
        };
        Assert.Equal("\u2014", state.MenuBarText);
    }

    [Fact]
    public void Connected_Shows_Headroom_Percentage()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            OAuthState = OAuthState.Authenticated,
            FiveHour = new WindowState(32, null),
        };
        Assert.Equal("68%", state.MenuBarText);
    }

    [Fact]
    public void Connected_With_Rising_Slope_Shows_Arrow()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            OAuthState = OAuthState.Authenticated,
            FiveHour = new WindowState(32, null),
            FiveHourSlope = SlopeLevel.Rising,
        };
        Assert.Equal("68% \u2197", state.MenuBarText); // 68% ↗
    }

    [Fact]
    public void Connected_With_Flat_Slope_No_Arrow()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            OAuthState = OAuthState.Authenticated,
            FiveHour = new WindowState(50, null),
            FiveHourSlope = SlopeLevel.Flat,
        };
        Assert.Equal("50%", state.MenuBarText);
    }

    [Fact]
    public void Exhausted_With_ResetsAt_Shows_Countdown()
    {
        var resetsAt = DateTimeOffset.Now.AddHours(1);
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            OAuthState = OAuthState.Authenticated,
            FiveHour = new WindowState(100, resetsAt),
            FiveHourSlope = SlopeLevel.Steep, // actionable but exhausted, no arrow
        };
        Assert.StartsWith("\u21BB ", state.MenuBarText); // ↻ countdown
    }

    [Fact]
    public void ExtraUsage_Active_Shows_Dollar_Amount()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            OAuthState = OAuthState.Authenticated,
            FiveHour = new WindowState(100, null), // exhausted
            ExtraUsageEnabled = true,
            ExtraUsageMonthlyLimitCents = 7500,
            ExtraUsageUsedCreditsCents = 5600,
        };
        // IsExtraUsageActive: exhausted + enabled
        // RemainingBalanceCents = 7500 - 5600 = 1900 => $19.00
        Assert.Equal("$19.00", state.MenuBarText);
    }
}

public class AppStateMenuBarHeadroomStateTests
{
    [Fact]
    public void Disconnected_Returns_HeadroomState_Disconnected()
    {
        var state = new AppState { ConnectionStatus = ConnectionStatus.Disconnected };
        Assert.Equal(HeadroomState.Disconnected, state.MenuBarHeadroomState);
    }

    [Fact]
    public void Connected_FiveHour_Displayed_Returns_FiveHour_State()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(97, null), // Critical
        };
        Assert.Equal(HeadroomState.Critical, state.MenuBarHeadroomState);
    }

    [Fact]
    public void Connected_No_Window_Returns_Disconnected()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = null,
        };
        Assert.Equal(HeadroomState.Disconnected, state.MenuBarHeadroomState);
    }
}

public class AppStateExtraUsageTests
{
    [Fact]
    public void IsExtraUsageActive_Requires_Enabled_And_Exhausted()
    {
        var state = new AppState
        {
            ExtraUsageEnabled = true,
            FiveHour = new WindowState(100, null),
        };
        Assert.True(state.IsExtraUsageActive);
    }

    [Fact]
    public void IsExtraUsageActive_False_When_Not_Exhausted()
    {
        var state = new AppState
        {
            ExtraUsageEnabled = true,
            FiveHour = new WindowState(50, null),
        };
        Assert.False(state.IsExtraUsageActive);
    }

    [Fact]
    public void IsExtraUsageActive_False_When_Not_Enabled()
    {
        var state = new AppState
        {
            ExtraUsageEnabled = false,
            FiveHour = new WindowState(100, null),
        };
        Assert.False(state.IsExtraUsageActive);
    }

    [Fact]
    public void ExtraUsageRemainingBalanceCents_Calculates_Correctly()
    {
        var state = new AppState
        {
            ExtraUsageMonthlyLimitCents = 10000,
            ExtraUsageUsedCreditsCents = 3500,
        };
        Assert.Equal(6500, state.ExtraUsageRemainingBalanceCents);
    }

    [Fact]
    public void ExtraUsageRemainingBalanceCents_Null_When_Missing_Data()
    {
        var state = new AppState
        {
            ExtraUsageMonthlyLimitCents = 10000,
            ExtraUsageUsedCreditsCents = null,
        };
        Assert.Null(state.ExtraUsageRemainingBalanceCents);
    }

    [Fact]
    public void MenuBarExtraUsageText_Null_When_Not_Active()
    {
        var state = new AppState { ExtraUsageEnabled = false };
        Assert.Null(state.MenuBarExtraUsageText);
    }

    [Fact]
    public void MenuBarExtraUsageText_Shows_Remaining_When_Available()
    {
        var state = new AppState
        {
            ExtraUsageEnabled = true,
            FiveHour = new WindowState(100, null),
            ExtraUsageMonthlyLimitCents = 5000,
            ExtraUsageUsedCreditsCents = 2000,
        };
        Assert.Equal("$30.00", state.MenuBarExtraUsageText);
    }

    [Fact]
    public void MenuBarExtraUsageText_Shows_Spent_When_No_Limit()
    {
        var state = new AppState
        {
            ExtraUsageEnabled = true,
            FiveHour = new WindowState(100, null),
            ExtraUsageMonthlyLimitCents = null,
            ExtraUsageUsedCreditsCents = 1250,
        };
        Assert.Equal("$12.50 spent", state.MenuBarExtraUsageText);
    }

    [Fact]
    public void MenuBarExtraUsageText_Shows_Zero_When_No_Data()
    {
        var state = new AppState
        {
            ExtraUsageEnabled = true,
            FiveHour = new WindowState(100, null),
            ExtraUsageMonthlyLimitCents = null,
            ExtraUsageUsedCreditsCents = null,
        };
        Assert.Equal("$0.00", state.MenuBarExtraUsageText);
    }
}

public class AppStateQuotasRemainingTests
{
    [Fact]
    public void Null_When_No_CreditLimits()
    {
        var state = new AppState
        {
            CreditLimits = null,
            SevenDay = new WindowState(50, null),
        };
        Assert.Null(state.QuotasRemaining);
    }

    [Fact]
    public void Null_When_No_SevenDay()
    {
        var state = new AppState
        {
            CreditLimits = new CreditLimits(100, 1000),
            SevenDay = null,
        };
        Assert.Null(state.QuotasRemaining);
    }

    [Fact]
    public void Null_When_FiveHourCredits_Zero()
    {
        var state = new AppState
        {
            CreditLimits = new CreditLimits(0, 1000),
            SevenDay = new WindowState(50, null),
        };
        Assert.Null(state.QuotasRemaining);
    }

    [Fact]
    public void Calculates_Correctly()
    {
        var state = new AppState
        {
            CreditLimits = new CreditLimits(1000, 10000),
            SevenDay = new WindowState(50, null), // 50% remaining
        };
        // (50% * 10000) / 1000 = 5.0
        Assert.Equal(5.0, state.QuotasRemaining);
    }
}

public class AppStateFormatCentsTests
{
    [Theory]
    [InlineData(0, "$0.00")]
    [InlineData(100, "$1.00")]
    [InlineData(1, "$0.01")]
    [InlineData(1050, "$10.50")]
    [InlineData(7500, "$75.00")]
    [InlineData(-250, "-$2.50")]
    public void FormatCents_Formats_Correctly(int cents, string expected)
    {
        Assert.Equal(expected, AppState.FormatCents(cents));
    }

    [Fact]
    public void FormatCents_Custom_Symbol()
    {
        Assert.Equal("EUR5.00", AppState.FormatCents(500, "EUR"));
    }
}

public class AppStatePreviewFactoryTests
{
    [Fact]
    public void CreatePreviewSignedOut_Is_Unauthenticated()
    {
        var state = AppState.CreatePreviewSignedOut();
        Assert.Equal(OAuthState.Unauthenticated, state.OAuthState);
        Assert.Equal(ConnectionStatus.NoCredentials, state.ConnectionStatus);
        Assert.True(state.IsUnauthenticated);
        Assert.NotEmpty(state.SparklineData);
    }

    [Fact]
    public void CreatePreviewAuthorizing_Is_Authorizing()
    {
        var state = AppState.CreatePreviewAuthorizing();
        Assert.Equal(OAuthState.Authorizing, state.OAuthState);
        Assert.True(state.IsAuthorizing);
        Assert.NotNull(state.StatusMessage);
    }

    [Fact]
    public void CreatePreviewConnected_Has_Valid_Data()
    {
        var state = AppState.CreatePreviewConnected();
        Assert.Equal(OAuthState.Authenticated, state.OAuthState);
        Assert.Equal(ConnectionStatus.Connected, state.ConnectionStatus);
        Assert.True(state.IsAuthenticated);
        Assert.NotNull(state.FiveHour);
        Assert.NotNull(state.SevenDay);
        Assert.NotNull(state.CreditLimits);
        Assert.NotNull(state.SubscriptionTier);
        Assert.NotNull(state.LastUpdated);
        Assert.NotEmpty(state.SparklineData);
    }

    [Fact]
    public void CreatePreviewCritical_Has_High_Utilization()
    {
        var state = AppState.CreatePreviewCritical();
        Assert.True(state.IsAuthenticated);
        Assert.True(state.FiveHour!.Utilization >= 95);
        Assert.True(state.ExtraUsageEnabled);
        Assert.NotNull(state.ExtraUsageMonthlyLimitCents);
        Assert.NotNull(state.ExtraUsageUsedCreditsCents);
    }

    [Fact]
    public void CreatePreviewDisconnected_Is_Disconnected()
    {
        var state = AppState.CreatePreviewDisconnected();
        Assert.Equal(ConnectionStatus.Disconnected, state.ConnectionStatus);
        Assert.True(state.IsAuthenticated);
        Assert.NotNull(state.StatusMessage);
        Assert.NotNull(state.LastUpdated);
        Assert.NotNull(state.LastAttempted);
    }
}

public class AppStateDisplayedSlopeTests
{
    [Fact]
    public void Returns_FiveHour_Slope_When_FiveHour_Displayed()
    {
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(50, null),
            FiveHourSlope = SlopeLevel.Rising,
            SevenDaySlope = SlopeLevel.Declining,
        };
        Assert.Equal(DisplayedWindow.FiveHour, state.DisplayedWindow);
        Assert.Equal(SlopeLevel.Rising, state.DisplayedSlope);
    }

    [Fact]
    public void Returns_SevenDay_Slope_When_SevenDay_Displayed()
    {
        // Force SevenDay display via QuotasRemaining < 1.0
        var state = new AppState
        {
            ConnectionStatus = ConnectionStatus.Connected,
            FiveHour = new WindowState(10, null),
            SevenDay = new WindowState(99, null),
            CreditLimits = new CreditLimits(3_300_000, 41_666_700),
            FiveHourSlope = SlopeLevel.Flat,
            SevenDaySlope = SlopeLevel.Steep,
        };
        Assert.Equal(DisplayedWindow.SevenDay, state.DisplayedWindow);
        Assert.Equal(SlopeLevel.Steep, state.DisplayedSlope);
    }
}

public class AppStateDefaultsTests
{
    [Fact]
    public void Default_Values_Are_Sensible()
    {
        var state = new AppState();
        Assert.Null(state.FiveHour);
        Assert.Null(state.SevenDay);
        Assert.Equal(ConnectionStatus.Disconnected, state.ConnectionStatus);
        Assert.Equal(OAuthState.Unauthenticated, state.OAuthState);
        Assert.Null(state.LastUpdated);
        Assert.Null(state.LastAttempted);
        Assert.Null(state.SubscriptionTier);
        Assert.Null(state.StatusMessage);
        Assert.Equal(SlopeLevel.Flat, state.FiveHourSlope);
        Assert.Equal(SlopeLevel.Flat, state.SevenDaySlope);
        Assert.Equal(0, state.FiveHourSlopeRate);
        Assert.Equal(0, state.SevenDaySlopeRate);
        Assert.Null(state.CreditLimits);
        Assert.False(state.ExtraUsageEnabled);
        Assert.Empty(state.SparklineData);
        Assert.Equal(UsageSource.None, state.DataSource);
        Assert.Null(state.CacheAgeSeconds);
    }
}
