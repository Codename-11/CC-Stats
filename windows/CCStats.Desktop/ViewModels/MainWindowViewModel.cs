using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using Avalonia.Media;
using Avalonia.Threading;
using CCStats.Core.Formatting;
using CCStats.Core.Models;
using CCStats.Core.Services;
using CCStats.Core.State;
using CCStats.Desktop.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ReactiveUI;
using SkiaSharp;

namespace CCStats.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private AppState _state;
    private bool _showSettings;
    private int _previewIndex;
    private bool _isServicesConnected;
    private bool _showUpdateBadge;
    private string _updateVersionText = string.Empty;
    private bool _isDocked = true;
    private bool _isTopmost = true;
    private bool _showInlineAnalytics;
    private AnalyticsViewModel _analyticsVm;
    private bool _showAccountNaming;
    private string _newAccountName = "";
    private bool _isPolling;
    private string? _pollErrorText;
    private DateTimeOffset? _lastCheckTime;
    private readonly DispatcherTimer _freshnessTimer;

    // Service references (set via ConnectServices)
    private OAuthService? _oauthService;
    private PollingEngine? _pollingEngine;
    private PreferencesManager? _preferences;
    private SecureStorageService? _secureStorage;

    // Settings sub-viewmodel
    private SettingsViewModel? _settings;

    private static readonly AppState[] PreviewStates =
    [
        AppState.CreatePreviewSignedOut(),
        AppState.CreatePreviewAuthorizing(),
        AppState.CreatePreviewConnected(),
        AppState.CreatePreviewCritical(),
        AppState.CreatePreviewDisconnected(),
    ];

    public event EventHandler? OpenAnalyticsRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? QuitRequested;
    public event EventHandler<bool>? DockStateChanged;
    public event EventHandler<string>? SwitchAccountRequested;
    public event EventHandler<bool>? InlineAnalyticsChanged;
    public event EventHandler? UpdateRequested;
    public event EventHandler? ExportDatabaseRequested;
    public event EventHandler? PruneDatabaseRequested;
    public event EventHandler? TestNotificationRequested;

    /// <summary>
    /// Shared AnalyticsViewModel used by both inline expanded view and popout window.
    /// </summary>
    public AnalyticsViewModel Analytics => _analyticsVm;

    private IReadOnlyList<CCStats.Core.Models.ResetEvent> _resetEvents = Array.Empty<CCStats.Core.Models.ResetEvent>();

    public void SetResetEvents(IReadOnlyList<CCStats.Core.Models.ResetEvent> events) => _resetEvents = events;

    private void RefreshAnalytics()
    {
        _analyticsVm.UpdateData(_state.SparklineData, _state, _resetEvents);
    }

    public MainWindowViewModel()
    {
        _state = AppState.CreatePreviewSignedOut();
        _previewIndex = 0; // Signed out
        _analyticsVm = new AnalyticsViewModel();
        SparklineSeries = new ISeries[] { _sparklineStepSeries };
        RefreshSparklineSeries();

        SignInCommand = ReactiveCommand.Create(OnSignIn);
        OpenAnalyticsCommand = ReactiveCommand.Create(OnOpenAnalytics);
        OpenSettingsCommand = ReactiveCommand.Create(OnOpenSettings);
        CloseSettingsCommand = ReactiveCommand.Create(() => { ShowSettings = false; });
        SignOutCommand = ReactiveCommand.Create(OnSignOut);
        QuitCommand = ReactiveCommand.Create(OnQuit);
        CyclePreviewCommand = ReactiveCommand.Create(CyclePreview);
        ToggleDockCommand = ReactiveCommand.Create(OnToggleDock);
        ToggleTopmostCommand = ReactiveCommand.Create(() => { IsTopmost = !IsTopmost; });
        ToggleInlineAnalyticsCommand = ReactiveCommand.Create(() => { ShowInlineAnalytics = !ShowInlineAnalytics; });
        PopOutAnalyticsCommand = ReactiveCommand.Create(OnPopOutAnalytics);
        ConfirmAccountNameCommand = ReactiveCommand.Create(ConfirmAccountName);
        CopyStatusCommand = ReactiveCommand.Create(OnCopyStatus);
        RefreshCommand = ReactiveCommand.Create(OnRefresh);
        UpdateCommand = ReactiveCommand.Create(() => UpdateRequested?.Invoke(this, EventArgs.Empty));

        // Update "X ago" freshness text every 30 seconds
        _freshnessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _freshnessTimer.Tick += (_, _) => RaiseFreshnessChanged();
        _freshnessTimer.Start();
    }

    // --- Service connection ---

    /// <summary>
    /// Whether real services are connected. When false, buttons use preview behavior.
    /// </summary>
    public bool IsServicesConnected
    {
        get => _isServicesConnected;
        private set => this.RaiseAndSetIfChanged(ref _isServicesConnected, value);
    }

    /// <summary>
    /// Connects real services to this ViewModel. Once called, SignIn triggers OAuth,
    /// SignOut clears credentials, and settings sync with PreferencesManager.
    /// </summary>
    public void ConnectServices(
        OAuthService oauth,
        PollingEngine polling,
        PreferencesManager prefs,
        SecureStorageService secureStorage,
        CCStats.Core.Services.DatabaseManager? database = null)
    {
        _oauthService = oauth;
        _pollingEngine = polling;
        _preferences = prefs;
        _secureStorage = secureStorage;
        IsServicesConnected = true;

        // Initialize settings sub-viewmodel with current preferences
        _settings = new SettingsViewModel();
        LoadSettingsFromPreferences();
        LoadAccounts();

        // Wire settings changes back to preferences
        WireSettingsToPreferences();

        // Wire account switch/remove
        _settings.AccountSwitchRequested += (_, accountId) => SwitchAccountRequested?.Invoke(this, accountId);
        _settings.AccountRemoveRequested += (_, accountId) =>
        {
            _secureStorage?.RemoveAccount(accountId);
            LoadAccounts();
            AppLogger.Log("Accounts", $"Removed account {accountId[..Math.Min(6, accountId.Length)]}");
            ShowToastMessage("Account removed", 3000, "info");
        };
        _settings.AccountNameSaved += (_, args) =>
        {
            var existing = _secureStorage?.LoadAccountCredentials(args.AccountId);
            if (existing is not null)
            {
                _secureStorage!.SaveAccountCredentials(args.AccountId, existing with { DisplayName = args.NewName });
                // Also update active credentials if this is the current account
                var active = _secureStorage.LoadCredentials();
                if (active?.AccountId == args.AccountId)
                {
                    _secureStorage.SaveCredentials(active with { DisplayName = args.NewName });
                }
                AppLogger.Log("Accounts", $"Renamed account {args.AccountId[..Math.Min(6, args.AccountId.Length)]} to \"{args.NewName}\"");
                this.RaisePropertyChanged(nameof(ActiveAccountLabel));
                ShowToastMessage($"Saved as \"{args.NewName}\"", 3000, "success");
            }
        };

        // Wire export/prune events
        _settings.ExportRequested += (_, _) => ExportDatabaseRequested?.Invoke(this, EventArgs.Empty);
        _settings.PruneRequested += (_, _) => PruneDatabaseRequested?.Invoke(this, EventArgs.Empty);

        // Wire test notification
        _settings.TestNotificationRequested += (_, _) => TestNotificationRequested?.Invoke(this, EventArgs.Empty);

        // Wire debug log copy to clipboard
        _settings.CopyLogRequested += (_, text) => CopyStatusRequested?.Invoke(this, text);

        // Wire database size (fire-and-forget)
        if (database is not null)
        {
            _ = UpdateDatabaseSizeAsync(database);
        }
    }

    private async System.Threading.Tasks.Task UpdateDatabaseSizeAsync(CCStats.Core.Services.DatabaseManager db)
    {
        try
        {
            var sizeBytes = await db.GetDatabaseSizeAsync();
            var sizeText = sizeBytes switch
            {
                < 1024 => $"{sizeBytes} B",
                < 1024 * 1024 => $"{sizeBytes / 1024.0:F1} KB",
                < 1024L * 1024 * 1024 => $"{sizeBytes / (1024.0 * 1024):F1} MB",
                _ => $"{sizeBytes / (1024.0 * 1024 * 1024):F2} GB",
            };
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_settings is not null)
                {
                    _settings.DatabaseSize = sizeText;
                    _settings.ShowClearDatabase = sizeBytes > 500 * 1024 * 1024;
                }
            });
        }
        catch
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_settings is not null) _settings.DatabaseSize = "Not available";
            });
        }
    }

    /// <summary>
    /// The settings sub-viewmodel, available after ConnectServices is called.
    /// </summary>
    public SettingsViewModel? Settings => _settings;

    // --- Auth state ---

    public bool IsAuthenticated => _state.IsAuthenticated;
    public bool IsAuthorizing => _state.IsAuthorizing;
    public bool IsUnauthenticated => _state.IsUnauthenticated;

    // --- Settings toggle ---

    public bool ShowSettings
    {
        get => _showSettings;
        set
        {
            this.RaiseAndSetIfChanged(ref _showSettings, value);
            this.RaisePropertyChanged(nameof(ShowMainContent));
            this.RaisePropertyChanged(nameof(ShowAuthenticatedContent));
            this.RaisePropertyChanged(nameof(ShowUnauthenticatedContent));
            this.RaisePropertyChanged(nameof(ShowAuthorizingContent));
        }
    }

    public bool ShowMainContent => !ShowSettings && !ShowAccountNaming;
    public bool ShowAuthenticatedContent => IsAuthenticated && !ShowSettings && !ShowAccountNaming;
    public bool ShowUnauthenticatedContent => IsUnauthenticated && !ShowSettings && !ShowAccountNaming;
    public bool ShowAuthorizingContent => IsAuthorizing && !ShowSettings && !ShowAccountNaming;

    // --- Account naming (shown after OAuth completes) ---

    public bool ShowAccountNaming
    {
        get => _showAccountNaming;
        set
        {
            this.RaiseAndSetIfChanged(ref _showAccountNaming, value);
            this.RaisePropertyChanged(nameof(ShowMainContent));
            this.RaisePropertyChanged(nameof(ShowAuthenticatedContent));
            this.RaisePropertyChanged(nameof(ShowUnauthenticatedContent));
            this.RaisePropertyChanged(nameof(ShowAuthorizingContent));
        }
    }

    public string NewAccountName
    {
        get => _newAccountName;
        set => this.RaiseAndSetIfChanged(ref _newAccountName, value);
    }

    private StoredCredentials? _pendingAccountCredentials;

    /// <summary>Shows the account naming prompt. Called from App.axaml.cs after OAuth.</summary>
    public void PromptAccountName(StoredCredentials credentials)
    {
        _pendingAccountCredentials = credentials;
        NewAccountName = _state.SubscriptionTier ?? "My Account";
        ShowAccountNaming = true;
    }

    /// <summary>Confirms the account name and saves.</summary>
    public void ConfirmAccountName()
    {
        if (_pendingAccountCredentials is not null)
        {
            var name = string.IsNullOrWhiteSpace(NewAccountName)
                ? _state.SubscriptionTier ?? "My Account"
                : NewAccountName.Trim();
            var named = _pendingAccountCredentials with { DisplayName = name };
            SaveAndRefreshAccount(named);
            _pendingAccountCredentials = null;
            AppLogger.Log("Accounts", $"Account created: \"{name}\"");
            ShowToastMessage($"Welcome, {name}!", 3000, "success");
        }
        ShowAccountNaming = false;
    }

    // --- 5h gauge section ---

    public double FiveHourPercentage => _state.FiveHour?.HeadroomPercentage ?? 0;
    public string FiveHourUsedText => $"{(int)Math.Round(_state.FiveHour?.Utilization ?? 0)}% used";

    public Color FiveHourFillColor => _state.FiveHour is not null
        ? HeadroomColors.ForHeadroomState(_state.FiveHour.HeadroomState)
        : HeadroomColors.Disconnected;

    public string FiveHourSlopeArrow => _state.FiveHourSlope.Arrow();

    public string FiveHourTrendText => _state.FiveHourSlope switch
    {
        SlopeLevel.Declining => "\u2198 Declining",
        SlopeLevel.Rising => "\u2197 Rising",
        SlopeLevel.Steep => "\u2B06 Rapid",
        _ => "\u2192 Stable",
    };

    public bool FiveHourSlopeIsActionable => _state.FiveHourSlope.IsActionable();

    public Color FiveHourSlopeColor => _state.FiveHourSlope switch
    {
        SlopeLevel.Declining => HeadroomColors.Normal,
        SlopeLevel.Rising => HeadroomColors.Warning,
        SlopeLevel.Steep => HeadroomColors.Critical,
        _ => HeadroomColors.TextSecondary,
    };

    /// <summary>
    /// Contextual status line below the 5h gauge. Shows:
    /// - Success: "On track -- Xh Ym ahead of reset" (green) when healthy
    /// - Warning: "~Xm of active use left" (orange) when rising
    /// - Critical: "< 5m left" (red) when nearly exhausted
    /// </summary>
    public string FiveHourBudgetText
    {
        get
        {
            var util = _state.FiveHour?.Utilization ?? 0;
            var rate = _state.FiveHourSlopeRate;
            var resetsAt = _state.FiveHour?.ResetsAt;

            // Exhausted state
            if (util >= 100) return "";

            // Rising: show estimated time until exhaustion
            // (SlopeCalculationService requires 3+ samples over 2+ minutes before reporting rate > 0)
            if (rate >= 0.3)
            {
                var remainingPercent = 100.0 - util;
                var minutesLeft = remainingPercent / rate;

                if (minutesLeft > 300) return ""; // longer than the window, not useful
                if (minutesLeft < 1) return "< 1m of active use left";
                if (minutesLeft > 60)
                {
                    var hours = (int)(minutesLeft / 60);
                    var mins = (int)(minutesLeft % 60);
                    return $"~{hours}h {mins}m of active use left";
                }
                return $"~{(int)minutesLeft}m of active use left";
            }

            // Healthy: show time margin ahead of reset
            if (resetsAt is { } reset && util < 60)
            {
                var timeUntilReset = reset - DateTimeOffset.Now;
                if (timeUntilReset.TotalMinutes > 0)
                {
                    var margin = FormatDuration(timeUntilReset);
                    return $"On track -- {margin} until reset";
                }
            }

            return "";
        }
    }

    public bool ShowBudgetEstimate => !string.IsNullOrEmpty(FiveHourBudgetText);

    public Color FiveHourBudgetColor
    {
        get
        {
            var rate = _state.FiveHourSlopeRate;
            if (rate >= 1.5) return HeadroomColors.Critical;
            if (rate >= 0.3) return HeadroomColors.Warning;
            return HeadroomColors.Normal; // success state
        }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            var h = (int)ts.TotalHours;
            var m = ts.Minutes;
            return m > 0 ? $"{h}h {m}m" : $"{h}h";
        }
        return $"{(int)ts.TotalMinutes}m";
    }

    public DateTimeOffset? FiveHourResetTime => _state.FiveHour?.ResetsAt;

    public bool FiveHourIsExhausted => _state.FiveHour?.HeadroomState == HeadroomState.Exhausted;

    public string FiveHourResetCountdown => _state.FiveHour?.ResetsAt is { } reset
        ? $"resets in {DateTimeFormatting.CountdownString(reset)}"
        : "reset unknown";

    public string FiveHourResetAbsolute => _state.FiveHour?.ResetsAt is { } reset
        ? DateTimeFormatting.AbsoluteTimeString(reset)
        : string.Empty;

    // --- PromoClock peak/off-peak indicator ---

    private string _promoClockLabel = "";
    private bool _showPromoClock;

    public string PromoClockLabel
    {
        get => _promoClockLabel;
        set
        {
            this.RaiseAndSetIfChanged(ref _promoClockLabel, value);
            this.RaisePropertyChanged(nameof(PromoClockColor));
        }
    }

    public bool ShowPromoClock
    {
        get => _showPromoClock;
        set
        {
            this.RaiseAndSetIfChanged(ref _showPromoClock, value);
            this.RaisePropertyChanged(nameof(ShowPeakOverlays));
            this.RaisePropertyChanged(nameof(SparklineSections));
            // Sync to analytics chart
            _analyticsVm.ShowPeakOverlays = value;
        }
    }

    public Color PromoClockColor => _promoClockLabel == "Peak"
        ? HeadroomColors.Warning    // orange for peak
        : HeadroomColors.Normal;    // green for off-peak

    /// <summary>Whether peak hour overlays should be drawn on charts.</summary>
    public bool ShowPeakOverlays => _showPromoClock;

    // --- 7d gauge section ---

    public bool ShowSevenDay => _state.SevenDay is not null;

    public double SevenDayPercentage => _state.SevenDay?.HeadroomPercentage ?? 0;
    public string SevenDayUsedText => $"{(int)Math.Round(_state.SevenDay?.Utilization ?? 0)}% used";

    public Color SevenDayFillColor => _state.SevenDay is not null
        ? HeadroomColors.ForHeadroomState(_state.SevenDay.HeadroomState)
        : HeadroomColors.Disconnected;

    public string SevenDaySlopeArrow => _state.SevenDaySlope.Arrow();

    public string SevenDayTrendText => _state.SevenDaySlope switch
    {
        SlopeLevel.Declining => "\u2198 Declining",
        SlopeLevel.Rising => "\u2197 Rising",
        SlopeLevel.Steep => "\u2B06 Rapid",
        _ => "\u2192 Stable",
    };

    public Color SevenDaySlopeColor => _state.SevenDaySlope switch
    {
        SlopeLevel.Declining => HeadroomColors.Normal,
        SlopeLevel.Rising => HeadroomColors.Warning,
        SlopeLevel.Steep => HeadroomColors.Critical,
        _ => HeadroomColors.TextSecondary,
    };

    public DateTimeOffset? SevenDayResetTime => _state.SevenDay?.ResetsAt;

    public bool SevenDayIsExhausted => _state.SevenDay?.HeadroomState == HeadroomState.Exhausted;

    public string SevenDayQuotasRemaining
    {
        get
        {
            if (_state.QuotasRemaining is { } quotas)
            {
                var rounded = Math.Round(quotas, 1);
                return $"{rounded:0.#} full 5h quotas left";
            }
            return string.Empty;
        }
    }

    public Color SevenDayQuotasColor
    {
        get
        {
            if (_state.QuotasRemaining is not { } quotas) return HeadroomColors.TextSecondary;
            return quotas switch
            {
                < 1.0 => HeadroomColors.Critical,
                < 2.0 => HeadroomColors.Warning,
                < 4.0 => HeadroomColors.Caution,
                _ => HeadroomColors.Normal,
            };
        }
    }

    public bool ShowSevenDayQuotas => !string.IsNullOrEmpty(SevenDayQuotasRemaining);

    // --- Extra usage section ---

    public bool ShowExtraUsage => _state.ExtraUsageEnabled;

    public double ExtraUsageUtilization => (_state.ExtraUsageUtilization ?? 0) * 100.0;

    public string ExtraUsageText
    {
        get
        {
            if (_state.ExtraUsageMonthlyLimitCents is { } limit && _state.ExtraUsageUsedCreditsCents is { } used)
            {
                return $"{AppState.FormatCents(used)} / {AppState.FormatCents(limit)}";
            }
            if (_state.ExtraUsageUsedCreditsCents is { } spentOnly)
            {
                return $"{AppState.FormatCents(spentOnly)} spent";
            }
            return "$0.00";
        }
    }

    public string ExtraUsagePercentText => $"{(int)Math.Round(ExtraUsageUtilization)}%";

    public Color ExtraUsageProgressColor => HeadroomColors.ForExtraUsage(ExtraUsageUtilization);

    public string ExtraUsageResetText
    {
        get
        {
            // Next month start
            var now = DateTimeOffset.Now;
            var nextMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(1);
            return $"Resets {nextMonth:MMM d}";
        }
    }

    // --- Sparkline ---

    public IReadOnlyList<double> SparklineData => _state.SparklineData;
    public bool HasSparklineData => _state.SparklineData.Count >= 2;

    public RectangularSection[] SparklineSections
    {
        get
        {
            var sections = new List<RectangularSection>();
            var data = _state.SparklineData;

            // Peak hour overlays — subtle warm tint behind peak windows
            if (ShowPeakOverlays && data.Count >= 2)
            {
                AddPeakHourBands(sections, data.Count, hoursWindow: 24);
            }

            if (data.Count > 2)
            {
                for (int i = 1; i < data.Count - 1; i++)
                {
                    if (data[i] == 0 && data[i - 1] > 0)
                    {
                        int gapStart = i;
                        while (i < data.Count && data[i] == 0) i++;
                        int gapEnd = i;

                        sections.Add(new RectangularSection
                        {
                            Xi = gapStart,
                            Xj = gapEnd,
                            Fill = new SolidColorPaint(SKColor.Parse("#14808080")),
                        });
                    }
                }
            }

            // Detect reset boundaries: sharp drops in utilization (>30% drop)
            // Gold/yellow to distinguish from amber peak hour overlays
            if (data.Count > 1)
            {
                for (int i = 1; i < data.Count; i++)
                {
                    if (data[i - 1] - data[i] > 30)
                    {
                        sections.Add(new RectangularSection
                        {
                            Xi = i - 0.5,
                            Xj = i + 0.5,
                            Fill = new SolidColorPaint(SKColor.Parse("#50E6C15A")), // gold/yellow
                        });
                    }
                }
            }

            return sections.ToArray();
        }
    }

    /// <summary>
    /// Adds subtle peak-hour background bands to chart sections.
    /// Peak = weekdays 9 AM – 5 PM local time. Mapped onto x-axis indices
    /// by distributing data points evenly across the time window.
    /// </summary>
    private static void AddPeakHourBands(List<RectangularSection> sections, int dataCount, double hoursWindow)
    {
        var now = DateTimeOffset.Now;
        var peakFill = new SolidColorPaint(SKColor.Parse("#0CF39A4B")); // orange at ~5% opacity

        // Walk backwards through the time window in 1-hour steps,
        // identify contiguous peak runs, and merge into bands
        double? bandStart = null;

        for (int h = 0; h <= (int)hoursWindow; h++)
        {
            var t = now.AddHours(-h);
            var isPeak = t.DayOfWeek >= DayOfWeek.Monday
                      && t.DayOfWeek <= DayOfWeek.Friday
                      && t.Hour >= 9 && t.Hour < 17;

            // Map hours-ago to x position (right edge = dataCount-1, left edge = 0)
            var x = dataCount - 1 - (h / hoursWindow) * (dataCount - 1);

            if (isPeak)
            {
                bandStart ??= x;
            }
            else if (bandStart is not null)
            {
                // End of a peak run — emit band
                sections.Add(new RectangularSection
                {
                    Xi = Math.Min(x, bandStart.Value),
                    Xj = Math.Max(x, bandStart.Value),
                    Fill = peakFill,
                });
                bandStart = null;
            }
        }

        // Close any open band at the left edge
        if (bandStart is not null)
        {
            sections.Add(new RectangularSection
            {
                Xi = 0,
                Xj = bandStart.Value,
                Fill = peakFill,
            });
        }
    }

    // Stable series object — only update Values to avoid chart flash on refresh
    private readonly StepLineSeries<double> _sparklineStepSeries = new()
    {
        Fill = new SolidColorPaint(SKColor.Parse("#8066B866")),
        Stroke = new SolidColorPaint(SKColor.Parse("#66B866")) { StrokeThickness = 1.5f },
        GeometrySize = 0,
        GeometryFill = null,
        GeometryStroke = null,
        AnimationsSpeed = TimeSpan.FromMilliseconds(300),
    };

    public ISeries[] SparklineSeries { get; private set; }

    private void RefreshSparklineSeries()
    {
        _sparklineStepSeries.Values = _state.SparklineData;
        SparklineSeries ??= new ISeries[] { _sparklineStepSeries };
    }

    public Axis[] SparklineXAxes => new Axis[]
    {
        new Axis
        {
            ShowSeparatorLines = false,
            IsVisible = false,
        }
    };

    public Axis[] SparklineYAxes => new Axis[]
    {
        new Axis
        {
            ShowSeparatorLines = false,
            IsVisible = false,
            MinLimit = 0,
            MaxLimit = 100,
        }
    };

    // --- Inline Analytics ---

    public bool ShowInlineAnalytics
    {
        get => _showInlineAnalytics;
        set
        {
            this.RaiseAndSetIfChanged(ref _showInlineAnalytics, value);
            this.RaisePropertyChanged(nameof(ShowSparklineOnly));
            InlineAnalyticsChanged?.Invoke(this, value);
        }
    }

    public bool ShowSparklineOnly => !ShowInlineAnalytics;


    // --- Status ---

    public bool ShowStatus => _state.ResolvedStatusMessage is not null && _state.IsAuthenticated;
    public string StatusTitle => _state.ResolvedStatusMessage?.Title ?? string.Empty;
    public string StatusDetail => _state.ResolvedStatusMessage?.Detail ?? string.Empty;

    // --- Toast banner (transient messages) ---

    private string _toastMessage = "";
    private bool _showToast;
    private string _toastType = "info"; // "success", "error", "info"
    private int _toastGeneration; // prevents stale timers from hiding newer toasts

    public string ToastMessage
    {
        get => _toastMessage;
        set => this.RaiseAndSetIfChanged(ref _toastMessage, value);
    }

    public bool ShowToast
    {
        get => _showToast;
        set => this.RaiseAndSetIfChanged(ref _showToast, value);
    }

    public string ToastType
    {
        get => _toastType;
        set => this.RaiseAndSetIfChanged(ref _toastType, value);
    }

    public IBrush ToastBackground => ToastType switch
    {
        "success" => new SolidColorBrush(Color.Parse("#1A2E1A")),  // dark green
        "error" => new SolidColorBrush(Color.Parse("#2E1A1A")),    // dark red
        _ => new SolidColorBrush(Color.Parse("#1A1E2E")),          // dark blue
    };

    public IBrush ToastForeground => ToastType switch
    {
        "success" => new SolidColorBrush(Color.Parse("#66B866")),  // green
        "error" => new SolidColorBrush(Color.Parse("#F06050")),    // red
        _ => new SolidColorBrush(Color.Parse("#6BA3D9")),          // blue
    };

    public string ToastIcon => ToastType switch
    {
        "success" => "\u2713",  // checkmark
        "error" => "\u2717",    // cross mark
        _ => "\u2139",          // info
    };

    /// <summary>Shows a transient toast that auto-hides. Type: "success", "error", or "info".</summary>
    public void ShowToastMessage(string message, int durationMs = 3000, string type = "info")
    {
        ToastMessage = message;
        ToastType = type;
        this.RaisePropertyChanged(nameof(ToastBackground));
        this.RaisePropertyChanged(nameof(ToastForeground));
        this.RaisePropertyChanged(nameof(ToastIcon));
        ShowToast = true;
        var gen = ++_toastGeneration;
        _ = HideToastAfterDelay(durationMs, gen);
    }

    private async System.Threading.Tasks.Task HideToastAfterDelay(int ms, int generation)
    {
        await System.Threading.Tasks.Task.Delay(ms);
        Dispatcher.UIThread.Post(() =>
        {
            if (_toastGeneration == generation)
                ShowToast = false;
        });
    }

    // --- Update badge ---

    private bool _updateConfirmPending;

    public bool ShowUpdateBadge
    {
        get => _showUpdateBadge;
        set
        {
            this.RaiseAndSetIfChanged(ref _showUpdateBadge, value);
        }
    }

    public string UpdateVersionText
    {
        get => _updateVersionText;
        set
        {
            this.RaiseAndSetIfChanged(ref _updateVersionText, value);
        }
    }

    public bool UpdateConfirmPending
    {
        get => _updateConfirmPending;
        set => this.RaiseAndSetIfChanged(ref _updateConfirmPending, value);
    }

    // --- Footer ---

    public string SubscriptionTier => string.IsNullOrWhiteSpace(_state.SubscriptionTier)
        ? "\u2014"
        : _state.SubscriptionTier;

    /// <summary>Active account display name for the footer.</summary>
    public string ActiveAccountLabel
    {
        get
        {
            if (_settings?.Accounts.Count > 0)
            {
                var active = _settings.Accounts.FirstOrDefault(a => a.IsActive);
                if (active is not null && !string.IsNullOrWhiteSpace(active.DisplayName))
                    return active.DisplayName;
            }
            return SubscriptionTier;
        }
    }

    public bool HasMultipleAccounts => _settings?.Accounts.Count > 1;

    public bool IsPolling
    {
        get => _isPolling;
        private set => this.RaiseAndSetIfChanged(ref _isPolling, value);
    }

    public string FreshnessText
    {
        get
        {
            if (_isPolling)
                return "Checking...";

            if (_pollErrorText is not null)
            {
                // Make it clear the user can click to fix auth issues
                if (NeedsReauth)
                    return "Session expired \u00b7 tap to sign in";
                return _pollErrorText;
            }

            if (_state.LastUpdated is not { } lastUpdated)
                return "Tap to refresh";

            var ago = DateTimeFormatting.RelativeTimeAgo(lastUpdated);
            return $"{ago} \u00b7 tap to refresh";
        }
    }

    public bool IsFreshnessStale
    {
        get
        {
            if (_isPolling) return false;
            if (_pollErrorText is not null) return true;
            if (_state.LastUpdated is not { } lastUpdated) return false;
            var elapsed = DateTimeOffset.Now - lastUpdated;
            return elapsed.TotalMinutes > 5;
        }
    }

    public IBrush FreshnessColor
    {
        get
        {
            if (_isPolling) return new SolidColorBrush(HeadroomColors.TextTertiary);
            if (_pollErrorText is not null) return new SolidColorBrush(HeadroomColors.Caution);
            return IsFreshnessStale
                ? new SolidColorBrush(HeadroomColors.Caution)
                : new SolidColorBrush(HeadroomColors.TextTertiary);
        }
    }

    public string FreshnessTooltip
    {
        get
        {
            var lines = new List<string>();
            // Data source
            var src = _state.DataSource;
            if (src != UsageSource.None)
                lines.Add($"Source: {src.Label()} - {src.Description()}");
            // Last synced
            if (_state.LastUpdated is { } lu)
                lines.Add($"Synced: {lu.ToLocalTime():MMM d, h:mm:ss tt}");
            // Cache age
            if (_state.CacheAgeSeconds is { } age)
                lines.Add($"Cache age: {(age >= 60 ? $"{age / 60}m {age % 60}s" : $"{age}s")}");
            // Last check attempt
            if (_lastCheckTime is { } t)
                lines.Add($"Last check: {t.ToLocalTime():h:mm:ss tt}");
            lines.Add("Click to refresh");
            return string.Join("\n", lines);
        }
    }

    // --- Data source indicator ---

    public string DataSourceLabel => _state.DataSource.Label();

    public bool ShowDataSource => _state.DataSource != UsageSource.None && _state.IsAuthenticated;

    public string DataSourceTooltip => _state.DataSource.Description();

    public IBrush DataSourceColor => _state.DataSource switch
    {
        UsageSource.LocalCache => new SolidColorBrush(Color.Parse("#4CAF50")), // green
        UsageSource.Api => new SolidColorBrush(Color.Parse("#4A90D9")),        // blue
        UsageSource.Cached => new SolidColorBrush(Color.Parse("#FF9800")),     // amber
        UsageSource.CredentialsOnly => new SolidColorBrush(Color.Parse("#9C7CDB")), // purple
        _ => new SolidColorBrush(Color.Parse("#6B7A8D")),
    };

    public IBrush DataSourceBackground => _state.DataSource switch
    {
        UsageSource.LocalCache => new SolidColorBrush(Color.Parse("#264CAF50")),
        UsageSource.Api => new SolidColorBrush(Color.Parse("#264A90D9")),
        UsageSource.Cached => new SolidColorBrush(Color.Parse("#26FF9800")),
        UsageSource.CredentialsOnly => new SolidColorBrush(Color.Parse("#269C7CDB")),
        _ => new SolidColorBrush(Color.Parse("#266B7A8D")),
    };

    /// <summary>Call from UI thread when a poll starts.</summary>
    public void SetPollingActive()
    {
        _isPolling = true;
        _pollErrorText = null;
        _lastCheckTime = DateTimeOffset.Now;
        RaiseFreshnessChanged();
    }

    /// <summary>Call from UI thread when a poll completes successfully.</summary>
    public void SetPollingComplete()
    {
        _isPolling = false;
        _pollErrorText = null;
        NeedsReauth = false;
        RaiseFreshnessChanged();
    }

    /// <summary>Call from UI thread when a poll fails (rate limit, error, etc.).</summary>
    public void SetPollingError(string shortMessage, ConnectionStatus connectionStatus = ConnectionStatus.Disconnected)
    {
        _isPolling = false;
        _pollErrorText = shortMessage;
        // Use actual connection status rather than fragile string matching
        NeedsReauth = connectionStatus == ConnectionStatus.TokenExpired;
        RaiseFreshnessChanged();
    }

    private bool _needsReauth;

    /// <summary>True when the poll error indicates the user needs to re-authenticate.</summary>
    public bool NeedsReauth
    {
        get => _needsReauth;
        private set => this.RaiseAndSetIfChanged(ref _needsReauth, value);
    }

    /// <summary>Update just the data source indicator without a full state apply.</summary>
    public void UpdateDataSource(UsageSource source, int? cacheAgeSeconds)
    {
        _state = _state with { DataSource = source, CacheAgeSeconds = cacheAgeSeconds };
        this.RaisePropertyChanged(nameof(DataSourceLabel));
        this.RaisePropertyChanged(nameof(ShowDataSource));
        this.RaisePropertyChanged(nameof(DataSourceColor));
        this.RaisePropertyChanged(nameof(DataSourceBackground));
        this.RaisePropertyChanged(nameof(DataSourceTooltip));
        this.RaisePropertyChanged(nameof(FreshnessTooltip));
    }

    private void RaiseFreshnessChanged()
    {
        this.RaisePropertyChanged(nameof(IsPolling));
        this.RaisePropertyChanged(nameof(FreshnessText));
        this.RaisePropertyChanged(nameof(IsFreshnessStale));
        this.RaisePropertyChanged(nameof(FreshnessColor));
        this.RaisePropertyChanged(nameof(FreshnessTooltip));
        this.RaisePropertyChanged(nameof(DataSourceLabel));
        this.RaisePropertyChanged(nameof(ShowDataSource));
        this.RaisePropertyChanged(nameof(DataSourceColor));
        this.RaisePropertyChanged(nameof(DataSourceBackground));
        this.RaisePropertyChanged(nameof(DataSourceTooltip));
    }

    // --- Commands ---

    public ReactiveCommand<Unit, Unit> SignInCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenAnalyticsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> SignOutCommand { get; }
    public ReactiveCommand<Unit, Unit> QuitCommand { get; }
    public ReactiveCommand<Unit, Unit> CyclePreviewCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleDockCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleTopmostCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleInlineAnalyticsCommand { get; }
    public ReactiveCommand<Unit, Unit> PopOutAnalyticsCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmAccountNameCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyStatusCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }

    // --- Refresh (force immediate poll) ---

    public event EventHandler? RefreshRequested;

    private void OnRefresh()
    {
        if (NeedsReauth)
        {
            // Token is permanently expired — trigger OAuth re-auth instead of futile poll retry
            AppLogger.Log("Auth", "Re-auth triggered from error banner");
            OnSignIn();
            return;
        }
        AppLogger.Log("Poll", "Manual refresh requested");
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    // --- Copy status to clipboard ---

    public event EventHandler<string>? CopyStatusRequested;

    public string ClipboardStatusText
    {
        get
        {
            var fh = _state.FiveHour;
            var sd = _state.SevenDay;
            var parts = new List<string>();

            if (fh is not null)
            {
                parts.Add($"5h: {fh.HeadroomPercentage:F0}% remaining ({fh.Utilization:F0}% used)");
                if (fh.ResetsAt is { } r)
                    parts.Add($"  Resets: {DateTimeFormatting.CountdownString(r)} (at {r.LocalDateTime:h:mm tt})");
            }
            if (sd is not null)
            {
                parts.Add($"7d: {sd.HeadroomPercentage:F0}% remaining ({sd.Utilization:F0}% used)");
            }
            parts.Add($"Tier: {SubscriptionTier}");

            return string.Join("\n", parts);
        }
    }

    private void OnCopyStatus()
    {
        CopyStatusRequested?.Invoke(this, ClipboardStatusText);
    }

    // --- Dock state ---

    public bool IsDocked
    {
        get => _isDocked;
        set
        {
            this.RaiseAndSetIfChanged(ref _isDocked, value);
            this.RaisePropertyChanged(nameof(DockIcon));
            this.RaisePropertyChanged(nameof(DockTooltip));
        }
    }

    public string DockIcon => _isDocked ? "\u2197" : "\u2199"; // ↗ undock, ↙ dock
    public string DockTooltip => _isDocked ? "Undock window" : "Dock to taskbar";

    // --- Topmost state ---

    public bool IsTopmost
    {
        get => _isTopmost;
        set
        {
            this.RaiseAndSetIfChanged(ref _isTopmost, value);
            this.RaisePropertyChanged(nameof(TopmostIcon));
            this.RaisePropertyChanged(nameof(TopmostTooltip));
        }
    }

    public string TopmostIcon => _isTopmost ? "\uD83D\uDCCC" : "\u25CB"; // 📌 pinned, ○ unpinned
    public string TopmostTooltip => _isTopmost ? "Unpin (allow behind other windows)" : "Pin on top";

    // --- Command handlers ---

    private async void OnSignIn()
    {
        if (_isServicesConnected && _oauthService is not null)
        {
            // Real OAuth flow
            try
            {
                var authUrl = await _oauthService.StartAuthFlowAsync();
                // Open the browser for the user to authenticate
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ViewModel] OAuth start failed: {ex.Message}");
                // Fall back to preview behavior on failure
                ApplyState(AppState.CreatePreviewSignedOut());
            }
        }
        else
        {
            // Preview mode: cycle through auth states
            if (_state.IsUnauthenticated)
            {
                ApplyState(AppState.CreatePreviewAuthorizing());
            }
            else if (_state.IsAuthorizing)
            {
                ApplyState(AppState.CreatePreviewConnected());
            }
        }
    }

    private void OnOpenAnalytics()
    {
        OpenAnalyticsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenSettings()
    {
        ShowSettings = !ShowSettings;
        this.RaisePropertyChanged(nameof(ShowMainContent));
        // Auto-refresh debug log when settings opens with log visible
        if (ShowSettings && _settings?.ShowDebugLog == true)
            _settings.RefreshDebugLog();
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSignOut()
    {
        if (_isServicesConnected)
        {
            // Real sign out: stop polling, clear credentials
            _pollingEngine?.Stop();
            _secureStorage?.ClearCredentials();
        }
        ApplyState(AppState.CreatePreviewSignedOut());
    }

    private void OnQuit()
    {
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnToggleDock()
    {
        IsDocked = !IsDocked;
        DockStateChanged?.Invoke(this, IsDocked);
    }

    private void OnPopOutAnalytics()
    {
        ShowInlineAnalytics = false; // collapse inline
        OpenAnalyticsRequested?.Invoke(this, EventArgs.Empty); // open popout
    }

    /// <summary>
    /// Cycles through preview states. Bound to F5 key for development.
    /// Always works regardless of service connection state.
    /// </summary>
    public void CyclePreview()
    {
        _previewIndex = (_previewIndex + 1) % PreviewStates.Length;
        ApplyState(PreviewStates[_previewIndex]);
    }

    /// <summary>
    /// Applies a new AppState snapshot to the ViewModel, raising all property change
    /// notifications. Must be called on the UI thread.
    /// </summary>
    public AppState CurrentState => _state;

    public void ApplyState(AppState state)
    {
        _state = state;
        RefreshSparklineSeries(); // Update values in-place (no flash)
        RaiseAllChanged();
        RefreshAnalytics();
    }

    // --- Settings / Preferences Wiring ---

    private void LoadSettingsFromPreferences()
    {
        if (_preferences is null || _settings is null) return;

        _settings.WarningThreshold = _preferences.WarningThreshold;
        _settings.CriticalThreshold = _preferences.CriticalThreshold;
        _settings.ApiStatusAlerts = _preferences.ApiStatusAlerts;
        _settings.ExtraUsageAlerts = _preferences.ExtraUsageAlerts;
        _settings.ExtraUsageAlert50 = _preferences.ExtraUsageAlert50;
        _settings.ExtraUsageAlert75 = _preferences.ExtraUsageAlert75;
        _settings.ExtraUsageAlert90 = _preferences.ExtraUsageAlert90;
        _settings.AdaptivePolling = _preferences.AdaptivePolling;
        _settings.LaunchAtLogin = _preferences.LaunchAtLogin;

        _settings.SelectedPollInterval = PollIntervalToString(_preferences.PollIntervalSeconds);
        _settings.SelectedDataRetention = DataRetentionToString(_preferences.DataRetentionDays);
        _settings.SelectedBillingCycleDay = _preferences.BillingCycleDay == 0
            ? "Not set"
            : _preferences.BillingCycleDay.ToString();

        _settings.FiveHourCreditLimit = _preferences.CustomFiveHourCredits?.ToString() ?? string.Empty;
        _settings.SevenDayCreditLimit = _preferences.CustomSevenDayCredits?.ToString() ?? string.Empty;
        _settings.MonthlyPrice = _preferences.CustomMonthlyPrice?.ToString() ?? string.Empty;

        // PromoClock
        _settings.PromoClockEnabled = _preferences.PromoClockEnabled;
    }

    // --- Multi-account loading ---

    public void LoadAccounts()
    {
        if (_secureStorage is null || _settings is null) return;
        _settings.Accounts.Clear();

        var accountIds = _secureStorage.ListAccounts();
        var currentCreds = _secureStorage.LoadCredentials();
        var activeToken = currentCreds?.AccessToken;

        foreach (var id in accountIds)
        {
            var creds = _secureStorage.LoadAccountCredentials(id);
            if (creds is null) continue;

            var tier = RateLimitTierExtensions.FromString(creds.SubscriptionType);
            var item = new AccountItemViewModel
            {
                AccountId = id,
                DisplayName = creds.DisplayName ?? $"Account {id[..Math.Min(6, id.Length)]}",
                TierLabel = tier.DisplayName(),
                IsActive = creds.AccessToken == activeToken,
            };


            _settings.Accounts.Add(item);
        }

        // If no accounts stored yet, migrate the current credentials
        if (currentCreds is not null && !string.IsNullOrEmpty(currentCreds.AccessToken) && accountIds.Count == 0)
        {
            var newId = Guid.NewGuid().ToString("N")[..8];
            // Use the current AppState tier (resolved from profile API) rather than stored subscription type
            var stateTier = _state.SubscriptionTier;
            var displayName = !string.IsNullOrWhiteSpace(stateTier) ? stateTier : "Primary";
            var withId = currentCreds with { AccountId = newId, DisplayName = displayName };
            _secureStorage.SaveAccountCredentials(newId, withId);
            _settings.Accounts.Add(new AccountItemViewModel
            {
                AccountId = newId,
                DisplayName = displayName,
                TierLabel = !string.IsNullOrWhiteSpace(stateTier) ? stateTier : "Unknown",
                IsActive = true,
            });
        }

        // Notify footer label to update with the (possibly renamed) active account
        this.RaisePropertyChanged(nameof(ActiveAccountLabel));
        this.RaisePropertyChanged(nameof(HasMultipleAccounts));
    }

    /// <summary>
    /// Saves new credentials as an account entry and refreshes the account list.
    /// Called from App.axaml.cs after OAuth completion.
    /// </summary>
    public void SaveAndRefreshAccount(StoredCredentials credentials)
    {
        if (_secureStorage is null || _settings is null) return;

        var accountId = credentials.AccountId
            ?? Guid.NewGuid().ToString("N")[..8];
        // Use the name the user provided; only fall back to tier/default if blank
        var displayName = !string.IsNullOrWhiteSpace(credentials.DisplayName)
            ? credentials.DisplayName
            : !string.IsNullOrWhiteSpace(_state.SubscriptionTier)
                ? _state.SubscriptionTier
                : "New Account";
        var withMeta = credentials with
        {
            AccountId = accountId,
            DisplayName = displayName,
        };

        _secureStorage.SaveAccountCredentials(accountId, withMeta);
        // Also update the active credentials with the account ID
        // so PollingEngine can persist tier updates to the right account file
        _secureStorage.SaveCredentials(withMeta);
        LoadAccounts();
    }

    private void WireSettingsToPreferences()
    {
        if (_preferences is null || _settings is null) return;

        _settings.PropertyChanged += (_, e) =>
        {
            if (_preferences is null) return;

            switch (e.PropertyName)
            {
                case nameof(SettingsViewModel.WarningThreshold):
                    _preferences.WarningThreshold = _settings.WarningThreshold;
                    break;
                case nameof(SettingsViewModel.CriticalThreshold):
                    _preferences.CriticalThreshold = _settings.CriticalThreshold;
                    break;
                case nameof(SettingsViewModel.ApiStatusAlerts):
                    _preferences.ApiStatusAlerts = _settings.ApiStatusAlerts;
                    break;
                case nameof(SettingsViewModel.ExtraUsageAlerts):
                    _preferences.ExtraUsageAlerts = _settings.ExtraUsageAlerts;
                    break;
                case nameof(SettingsViewModel.ExtraUsageAlert50):
                    _preferences.ExtraUsageAlert50 = _settings.ExtraUsageAlert50;
                    break;
                case nameof(SettingsViewModel.ExtraUsageAlert75):
                    _preferences.ExtraUsageAlert75 = _settings.ExtraUsageAlert75;
                    break;
                case nameof(SettingsViewModel.ExtraUsageAlert90):
                    _preferences.ExtraUsageAlert90 = _settings.ExtraUsageAlert90;
                    break;
                case nameof(SettingsViewModel.AdaptivePolling):
                    _preferences.AdaptivePolling = _settings.AdaptivePolling;
                    break;
                case nameof(SettingsViewModel.LaunchAtLogin):
                    _preferences.LaunchAtLogin = _settings.LaunchAtLogin;
                    break;
                case nameof(SettingsViewModel.SelectedPollInterval):
                    _preferences.PollIntervalSeconds = PollIntervalFromString(_settings.SelectedPollInterval);
                    break;
                case nameof(SettingsViewModel.SelectedDataRetention):
                    _preferences.DataRetentionDays = DataRetentionFromString(_settings.SelectedDataRetention);
                    break;
                case nameof(SettingsViewModel.SelectedBillingCycleDay):
                    _preferences.BillingCycleDay = _settings.SelectedBillingCycleDay == "Not set"
                        ? 0
                        : int.TryParse(_settings.SelectedBillingCycleDay, out var day) ? day : 0;
                    break;
                case nameof(SettingsViewModel.FiveHourCreditLimit):
                    _preferences.CustomFiveHourCredits = int.TryParse(_settings.FiveHourCreditLimit, out var c5)
                        ? c5
                        : null;
                    break;
                case nameof(SettingsViewModel.SevenDayCreditLimit):
                    _preferences.CustomSevenDayCredits = int.TryParse(_settings.SevenDayCreditLimit, out var c7)
                        ? c7
                        : null;
                    break;
                case nameof(SettingsViewModel.MonthlyPrice):
                    _preferences.CustomMonthlyPrice = double.TryParse(_settings.MonthlyPrice, out var mp)
                        ? mp
                        : null;
                    break;
                case nameof(SettingsViewModel.PromoClockEnabled):
                    _preferences.PromoClockEnabled = _settings.PromoClockEnabled;
                    break;
            }
        };
    }

    // --- Poll interval string conversion ---

    private static string PollIntervalToString(int seconds) => seconds switch
    {
        10 => "10s",
        15 => "15s",
        30 => "30s",
        60 => "1m",
        120 => "2m",
        300 => "5m",
        600 => "10m",
        900 => "15m",
        1800 => "30m",
        _ => "1m",
    };

    private static int PollIntervalFromString(string s) => s switch
    {
        "10s" => 10,
        "15s" => 15,
        "30s" => 30,
        "1m" => 60,
        "2m" => 120,
        "5m" => 300,
        "10m" => 600,
        "15m" => 900,
        "30m" => 1800,
        _ => 60,
    };

    // --- Data retention string conversion ---

    private static string DataRetentionToString(int days) => days switch
    {
        30 => "30d",
        90 => "90d",
        180 => "180d",
        365 => "1y",
        730 => "2y",
        1825 => "5y",
        _ => "1y",
    };

    private static int DataRetentionFromString(string s) => s switch
    {
        "30d" => 30,
        "90d" => 90,
        "180d" => 180,
        "1y" => 365,
        "2y" => 730,
        "5y" => 1825,
        _ => 365,
    };

    // --- Property change notifications ---

    private void RaiseAllChanged()
    {
        this.RaisePropertyChanged(nameof(IsAuthenticated));
        this.RaisePropertyChanged(nameof(IsAuthorizing));
        this.RaisePropertyChanged(nameof(IsUnauthenticated));
        this.RaisePropertyChanged(nameof(ShowAuthenticatedContent));
        this.RaisePropertyChanged(nameof(ShowUnauthenticatedContent));
        this.RaisePropertyChanged(nameof(ShowAuthorizingContent));

        this.RaisePropertyChanged(nameof(FiveHourPercentage));
        this.RaisePropertyChanged(nameof(FiveHourUsedText));
        this.RaisePropertyChanged(nameof(FiveHourFillColor));
        this.RaisePropertyChanged(nameof(FiveHourSlopeArrow));
        this.RaisePropertyChanged(nameof(FiveHourTrendText));
        this.RaisePropertyChanged(nameof(FiveHourSlopeIsActionable));
        this.RaisePropertyChanged(nameof(FiveHourSlopeColor));
        this.RaisePropertyChanged(nameof(FiveHourResetTime));
        this.RaisePropertyChanged(nameof(FiveHourIsExhausted));
        this.RaisePropertyChanged(nameof(FiveHourResetCountdown));
        this.RaisePropertyChanged(nameof(FiveHourResetAbsolute));
        this.RaisePropertyChanged(nameof(FiveHourBudgetText));
        this.RaisePropertyChanged(nameof(ShowBudgetEstimate));
        this.RaisePropertyChanged(nameof(FiveHourBudgetColor));

        this.RaisePropertyChanged(nameof(ShowSevenDay));
        this.RaisePropertyChanged(nameof(SevenDayPercentage));
        this.RaisePropertyChanged(nameof(SevenDayUsedText));
        this.RaisePropertyChanged(nameof(SevenDayFillColor));
        this.RaisePropertyChanged(nameof(SevenDaySlopeArrow));
        this.RaisePropertyChanged(nameof(SevenDayTrendText));
        this.RaisePropertyChanged(nameof(SevenDaySlopeColor));
        this.RaisePropertyChanged(nameof(SevenDayResetTime));
        this.RaisePropertyChanged(nameof(SevenDayIsExhausted));
        this.RaisePropertyChanged(nameof(SevenDayQuotasRemaining));
        this.RaisePropertyChanged(nameof(SevenDayQuotasColor));
        this.RaisePropertyChanged(nameof(ShowSevenDayQuotas));

        this.RaisePropertyChanged(nameof(ShowExtraUsage));
        this.RaisePropertyChanged(nameof(ExtraUsageUtilization));
        this.RaisePropertyChanged(nameof(ExtraUsageText));
        this.RaisePropertyChanged(nameof(ExtraUsagePercentText));
        this.RaisePropertyChanged(nameof(ExtraUsageProgressColor));
        this.RaisePropertyChanged(nameof(ExtraUsageResetText));

        this.RaisePropertyChanged(nameof(SparklineData));
        this.RaisePropertyChanged(nameof(HasSparklineData));
        this.RaisePropertyChanged(nameof(SparklineSeries));
        this.RaisePropertyChanged(nameof(SparklineSections));
        this.RaisePropertyChanged(nameof(SparklineXAxes));
        this.RaisePropertyChanged(nameof(SparklineYAxes));

        this.RaisePropertyChanged(nameof(ShowStatus));
        this.RaisePropertyChanged(nameof(StatusTitle));
        this.RaisePropertyChanged(nameof(StatusDetail));

        this.RaisePropertyChanged(nameof(ShowUpdateBadge));
        this.RaisePropertyChanged(nameof(UpdateVersionText));

        this.RaisePropertyChanged(nameof(SubscriptionTier));
        this.RaisePropertyChanged(nameof(ActiveAccountLabel));
        this.RaisePropertyChanged(nameof(HasMultipleAccounts));
        this.RaisePropertyChanged(nameof(FreshnessText));
        this.RaisePropertyChanged(nameof(IsFreshnessStale));
        this.RaisePropertyChanged(nameof(FreshnessColor));
        this.RaisePropertyChanged(nameof(FreshnessTooltip));
        this.RaisePropertyChanged(nameof(DataSourceLabel));
        this.RaisePropertyChanged(nameof(ShowDataSource));
        this.RaisePropertyChanged(nameof(DataSourceColor));
        this.RaisePropertyChanged(nameof(DataSourceBackground));
        this.RaisePropertyChanged(nameof(DataSourceTooltip));
    }
}
