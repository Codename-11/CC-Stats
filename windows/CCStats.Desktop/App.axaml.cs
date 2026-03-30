using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CCStats.Core.Models;
using CCStats.Core.Services;
using CCStats.Core.State;
using CCStats.Desktop.Services;
using CCStats.Desktop.ViewModels;
using CCStats.Desktop.Views;

namespace CCStats.Desktop;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _viewModel;
    private TrayIconService? _trayIconService;

    // --- Services ---
    private PreferencesManager? _preferences;
    private SecureStorageService? _secureStorage;
    private DatabaseManager? _database;
    private HistoricalDataService? _historyService;
    private APIClient? _apiClient;
    private TokenRefreshService? _tokenRefresh;
    private OAuthService? _oauthService;
    private PollingEngine? _pollingEngine;
    private SlopeCalculationService? _fiveHourSlope;
    private SlopeCalculationService? _sevenDaySlope;
    private NotificationService? _notificationService;
    private UpdateCheckService? _updateCheckService;
    private PromoClockService? _promoClockService;

    private int _lastPollIntervalSeconds;
    private bool _wasDisconnected;
    private readonly List<double> _inMemorySparkline = new();
    private DateTimeOffset _lastAccountLoadTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan AccountLoadThrottle = TimeSpan.FromMinutes(5);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainWindowViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = _viewModel,
            };

            // Don't set MainWindow so the app doesn't auto-show it;
            // instead we manage visibility ourselves via tray icon.
            // For development, show the window directly.
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Wire up the open analytics command to open the analytics window
            _viewModel.OpenAnalyticsRequested += OnOpenAnalyticsRequested;
            _viewModel.OpenSettingsRequested += OnOpenSettingsRequested;
            _viewModel.QuitRequested += OnQuitRequested;
            _viewModel.SwitchAccountRequested += OnSwitchAccountRequested;
            _viewModel.RefreshRequested += (_, _) => _ = ForceImmediatePollAsync();
            _viewModel.UpdateRequested += async (_, _) =>
            {
                if (_updateCheckService is null) return;

                // Two-step: first click checks + shows confirmation, second click installs
                if (_viewModel!.UpdateConfirmPending)
                {
                    // Second click — user confirmed, proceed with download
                    _viewModel.UpdateConfirmPending = false;
                    try
                    {
                        Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage($"Downloading {_viewModel.UpdateVersionText}...", 30000, "info"));
                        AppLogger.Log("Update", $"Downloading {_viewModel.UpdateVersionText}");

                        var result = await _updateCheckService.CheckForUpdateAsync();
                        if (result?.ExeDownloadUrl is null)
                        {
                            Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage("Download URL not found.", 6000, "error"));
                            AppLogger.Error("Update", "Download URL not found in release");
                            return;
                        }

                        var tempPath = await _updateCheckService.DownloadUpdateAsync(result.ExeDownloadUrl);
                        if (tempPath is not null)
                        {
                            Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage("Installing -- app will restart...", 5000, "success"));
                            AppLogger.Log("Update", "Installing update, restarting");
                            await Task.Delay(1500);
                            UpdateCheckService.ApplyUpdateAndRestart(tempPath);
                        }
                        else
                        {
                            Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage("Download failed. Try again later.", 6000, "error"));
                            AppLogger.Error("Update", "Download returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Update", "Update failed", ex);
                        Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage($"Update error: {ex.Message}", 8000, "error"));
                    }
                    return;
                }

                // First click — check for updates and show confirmation
                try
                {
                    Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage("Checking for updates...", 10000, "info"));
                    AppLogger.Log("Update", "Checking for updates");

                    var checkResult = await _updateCheckService.CheckForUpdateAsync();
                    if (checkResult is null)
                    {
                        Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage("You're on the latest version.", 3000, "success"));
                        AppLogger.Log("Update", "Already on latest version");
                        return;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        _viewModel.UpdateVersionText = checkResult.LatestVersion;
                        _viewModel.ShowUpdateBadge = true;
                        _viewModel.UpdateConfirmPending = true;
                        _viewModel.ShowToastMessage($"{checkResult.LatestVersion} available -- click update again to install", 15000, "info");
                    });
                    AppLogger.Log("Update", $"Update available: {checkResult.LatestVersion}");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Update", "Update check failed", ex);
                    Dispatcher.UIThread.Post(() => _viewModel.ShowToastMessage($"Update check failed: {ex.Message}", 6000, "error"));
                }
            };

            // Wire DB export and prune
            _viewModel.ExportDatabaseRequested += async (_, _) =>
            {
                try
                {
                    var from = DateTimeOffset.UtcNow.AddYears(-5);
                    var to = DateTimeOffset.UtcNow;
                    var polls = await _database!.QueryPollsAsync(from, to);

                    var csv = new System.Text.StringBuilder();
                    csv.AppendLine("Timestamp,FiveHourUtil,FiveHourResetsAt,SevenDayUtil,SevenDayResetsAt");
                    foreach (var p in polls)
                    {
                        csv.AppendLine($"{p.Timestamp:o},{p.FiveHourUtilization},{p.FiveHourResetsAt:o},{p.SevenDayUtilization},{p.SevenDayResetsAt:o}");
                    }

                    var path = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"ccstats-export-{DateTime.Now:yyyy-MM-dd}.csv");
                    await System.IO.File.WriteAllTextAsync(path, csv.ToString());

                    AppLogger.Log("Database", $"Exported {polls.Count} polls to {path}");
                    Dispatcher.UIThread.Post(() => _viewModel!.ShowToastMessage($"Exported {polls.Count} polls to Desktop", 4000, "success"));
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Database", "Export failed", ex);
                    Dispatcher.UIThread.Post(() => _viewModel!.ShowToastMessage($"Export failed: {ex.Message}", 5000, "error"));
                }
            };

            _viewModel.PruneDatabaseRequested += async (_, _) =>
            {
                try
                {
                    var days = _preferences!.DataRetentionDays;
                    var pruned = await _database!.PruneOldDataAsync(days);

                    // Refresh database size
                    var size = await _database.GetDatabaseSizeAsync();
                    var sizeText = size switch
                    {
                        < 1024 => $"{size} B",
                        < 1024 * 1024 => $"{size / 1024.0:F1} KB",
                        _ => $"{size / (1024.0 * 1024):F1} MB",
                    };

                    AppLogger.Log("Database", $"Pruned {pruned} records older than {days} days");
                    Dispatcher.UIThread.Post(() =>
                    {
                        _viewModel!.Settings!.DatabaseSize = sizeText;
                        _viewModel.ShowToastMessage($"Pruned {pruned} old records (kept last {days} days)", 4000, "success");
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Database", "Prune failed", ex);
                    Dispatcher.UIThread.Post(() => _viewModel!.ShowToastMessage($"Prune failed: {ex.Message}", 5000, "error"));
                }
            };

            // Set up system tray icon
            _trayIconService = TrayIconService.Setup(
                this, _mainWindow,
                onAnalyticsRequested: () => Dispatcher.UIThread.Post(() => OnOpenAnalyticsRequested(null, EventArgs.Empty)),
                onSettingsRequested: () => Dispatcher.UIThread.Post(() => { _viewModel!.ShowSettings = true; _mainWindow?.Show(); _mainWindow?.Activate(); }),
                onSignOutRequested: () => Dispatcher.UIThread.Post(() => SignOut()));

            // Initialize all services and wire them together
            InitializeServices();

            // First-launch registration: Start Menu shortcut + Add/Remove Programs
            AppInstallService.EnsureInstalled();

            // Wire shutdown hook
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeServices()
    {
        // 1. Preferences
        _preferences = new PreferencesManager();
        _preferences.Load();
        _lastPollIntervalSeconds = _preferences.PollIntervalSeconds;

        // 2. Secure storage
        _secureStorage = new SecureStorageService();

        // 3. Database — MUST complete before polling starts
        _database = new DatabaseManager();
        try
        {
            _database.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Database init failed: {ex.Message}");
        }

        // 4. Historical data
        _historyService = new HistoricalDataService(_database);

        // 5. API client
        _apiClient = new APIClient();

        // 6. Token refresh
        _tokenRefresh = new TokenRefreshService();

        // 7. Slope calculators (two instances: 5h and 7d)
        _fiveHourSlope = new SlopeCalculationService();
        _sevenDaySlope = new SlopeCalculationService();

        // Bootstrap slopes from recent DB data so trends are accurate on startup
        try
        {
            var recentPolls = _database.QueryPollsAsync(
                DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow).GetAwaiter().GetResult();
            if (recentPolls.Count >= 2)
            {
                _fiveHourSlope.Bootstrap(recentPolls.Select(p => (p.FiveHourUtilization, p.Timestamp)));
                _sevenDaySlope.Bootstrap(recentPolls
                    .Where(p => p.SevenDayUtilization.HasValue)
                    .Select(p => (p.SevenDayUtilization!.Value, p.Timestamp)));
                AppLogger.Log("Startup", $"Bootstrapped slopes from {recentPolls.Count} recent polls");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Startup", "Slope bootstrap failed", ex);
        }

        // 8. Notification service
        _notificationService = new NotificationService();

        // 9. OAuth service
        _oauthService = new OAuthService();

        // 10. Polling engine
        _pollingEngine = new PollingEngine(
            _apiClient, _secureStorage, _tokenRefresh,
            _preferences, _fiveHourSlope, _sevenDaySlope,
            _database);

        // 11. Update check service
        _updateCheckService = new UpdateCheckService();

        // 12. PromoClock service
        _promoClockService = new PromoClockService();

        // --- Wire events ---
        WirePollingEngine();
        WireOAuthFlow();
        WireNotificationService();
        WirePreferencesChanges();

        // --- Connect services to ViewModel ---
        _viewModel!.ConnectServices(_oauthService, _pollingEngine, _preferences, _secureStorage, _database);

        // --- Wire test notification ---
        _viewModel.TestNotificationRequested += (_, _) =>
        {
            _notificationService?.ShowThresholdAlert(HeadroomState.Warning, 15);
        };

        // --- Start PromoClock polling ---
        _ = PollPromoClockAsync();

        // --- Wire pattern dismissal persistence ---
        _viewModel.Analytics.SetDismissedPatterns(_preferences.DismissedPatternFindings);
        _viewModel.Analytics.PatternDismissed += (_, title) =>
        {
            if (!_preferences.DismissedPatternFindings.Contains(title))
            {
                _preferences.DismissedPatternFindings.Add(title);
                _preferences.Save();
            }
        };

        // --- Auto-start if credentials exist ---
        StoredCredentials? credentials = null;
        try
        {
            credentials = _secureStorage.LoadCredentials();
            AppLogger.Log("Startup", $"Credentials: {(credentials is not null ? "found" : "none")}, token: {(credentials?.AccessToken is not null ? "present" : "missing")}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Startup", "Failed to load credentials", ex);
        }

        // Also check account files if main credentials are missing
        if (credentials is null || string.IsNullOrEmpty(credentials.AccessToken))
        {
            var accountIds = _secureStorage.ListAccounts();
            AppLogger.Log("Startup", $"No active credentials, checking {accountIds.Count} account files");
            foreach (var id in accountIds)
            {
                var acct = _secureStorage.LoadAccountCredentials(id);
                if (acct is not null && !string.IsNullOrEmpty(acct.AccessToken))
                {
                    credentials = acct;
                    _secureStorage.SaveCredentials(acct);
                    AppLogger.Log("Startup", $"Recovered credentials from account {id[..Math.Min(6, id.Length)]}");
                    break;
                }
            }
        }

        if (credentials is not null && !string.IsNullOrEmpty(credentials.AccessToken))
        {
            _apiClient.SetAccessToken(credentials.AccessToken);
            _pollingEngine.Start();

            // Try to show instant data from Claude Code's local statusline cache
            // before our first API poll completes
            var localCache = LocalCacheService.ReadCache();
            if (localCache?.FiveHourUtilization is not null)
            {
                var fhReset = localCache.FiveHourResetsAt is not null
                    ? DateTimeOffset.TryParse(localCache.FiveHourResetsAt, out var r) ? r : (DateTimeOffset?)null
                    : null;
                var sdReset = localCache.SevenDayResetsAt is not null
                    ? DateTimeOffset.TryParse(localCache.SevenDayResetsAt, out var r2) ? r2 : (DateTimeOffset?)null
                    : null;

                // Seed sparkline with local cache value so chart appears immediately
                _inMemorySparkline.Add(localCache.FiveHourUtilization.Value);
                _inMemorySparkline.Add(localCache.FiveHourUtilization.Value); // duplicate to meet >= 2 threshold

                var cacheAge = localCache.FetchedAt.HasValue
                    ? (int)(DateTimeOffset.UtcNow - localCache.FetchedAt.Value).TotalSeconds
                    : (int?)null;
                var cacheSource = cacheAge is null or <= 120
                    ? CCStats.Core.Models.UsageSource.LocalCache
                    : CCStats.Core.Models.UsageSource.Cached;
                _viewModel.ApplyState(new AppState
                {
                    OAuthState = OAuthState.Authenticated,
                    ConnectionStatus = ConnectionStatus.Connected,
                    DataSource = cacheSource,
                    CacheAgeSeconds = cacheAge,
                    SubscriptionTier = credentials.DisplayName
                        ?? RateLimitTierExtensions.FromString(credentials.SubscriptionType).DisplayName(),
                    FiveHour = new CCStats.Core.Models.WindowState(
                        localCache.FiveHourUtilization.Value, fhReset),
                    SevenDay = localCache.SevenDayUtilization is not null
                        ? new CCStats.Core.Models.WindowState(localCache.SevenDayUtilization.Value, sdReset)
                        : null,
                    LastUpdated = localCache.FetchedAt,
                    ExtraUsageEnabled = localCache.ExtraUsageEnabled ?? false,
                    ExtraUsageUtilization = localCache.ExtraUsageUtilization,
                    SparklineData = _inMemorySparkline.ToArray(),
                });
                AppLogger.Log("Data", $"Startup from local cache ({cacheAge}s old)");
                _trayIconService?.UpdateIcon(
                    100 - localCache.FiveHourUtilization.Value,
                    CCStats.Core.Models.HeadroomStateHelpers.FromUtilization(localCache.FiveHourUtilization),
                    $"CC-Stats — {100 - localCache.FiveHourUtilization.Value:F0}%");
            }
            else
            {
                _viewModel.ApplyState(new AppState
                {
                    OAuthState = OAuthState.Authenticated,
                    ConnectionStatus = ConnectionStatus.Disconnected,
                    SubscriptionTier = credentials.DisplayName
                        ?? RateLimitTierExtensions.FromString(credentials.SubscriptionType).DisplayName(),
                });
                _trayIconService?.UpdateIcon(0, HeadroomState.Disconnected, "CC-Stats — Connecting...");
            }
        }
        else
        {
            _trayIconService?.UpdateIcon(0, HeadroomState.Disconnected, "CC-Stats — Not signed in");
        }

        // --- Load historical sparkline from DB (don't wait for first poll) ---
        _ = LoadStartupSparklineAsync(credentials);

        // --- Fire-and-forget: check for updates ---
        _ = CheckForUpdatesAsync();
    }

    /// <summary>
    /// Load historical sparkline data from SQLite on startup so the chart shows
    /// real history even when polls are failing (expired token, rate limited, etc.).
    /// Falls back to local cache seed if DB has no data.
    /// </summary>
    private async Task LoadStartupSparklineAsync(StoredCredentials? credentials)
    {
        try
        {
            // Small delay to let DB init complete
            await Task.Delay(500);

            var dbSparkline = await _historyService!.GetSparklineDataAsync();
            if (dbSparkline.Count >= 2)
            {
                AppLogger.Log("Startup", $"Loaded {dbSparkline.Count} sparkline points from DB");
                _inMemorySparkline.Clear(); // DB has real data

                Dispatcher.UIThread.Post(() =>
                {
                    if (_viewModel is null) return;
                    var current = _viewModel.CurrentState;
                    _viewModel.ApplyState(current with
                    {
                        SparklineData = dbSparkline,
                    });
                });
            }
            else
            {
                AppLogger.Log("Startup", $"DB has {dbSparkline.Count} sparkline points (insufficient)");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Startup", "Failed to load startup sparkline from DB", ex);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        // Check on startup, then every 6 hours
        while (true)
        {
            try
            {
                var result = await _updateCheckService!.CheckForUpdateAsync();
                if (result is not null)
                {
                    if (_preferences!.DismissedVersion != result.LatestVersion)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _viewModel!.ShowUpdateBadge = true;
                            _viewModel.UpdateVersionText = result.LatestVersion;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Update check failed: {ex.Message}");
            }

            // Wait 6 hours before checking again
            try { await Task.Delay(TimeSpan.FromHours(6)); }
            catch (TaskCanceledException) { break; }
        }
    }

    // --- Polling Engine Wiring ---

    private void WirePollingEngine()
    {
        _pollingEngine!.PollCompleted += async (_, e) =>
        {
          try
          {
            var state = _pollingEngine.CurrentState;

            // Record to database (fire-and-forget)
            try
            {
                if (state.FiveHour is not null)
                {
                    var activeAccountId = _secureStorage?.LoadCredentials()?.AccountId;
                    await _historyService!.RecordPollAsync(
                        state.FiveHour.Utilization,
                        state.SevenDay?.Utilization ?? 0,
                        state.FiveHour.ResetsAt,
                        state.SevenDay?.ResetsAt,
                        activeAccountId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Failed to record poll: {ex.Message}");
            }

            // Get sparkline data and reset events from DB
            IReadOnlyList<double> sparklineData;
            IReadOnlyList<CCStats.Core.Models.ResetEvent> resetEvents;
            try
            {
                sparklineData = await _historyService!.GetSparklineDataAsync();
                resetEvents = await _historyService.GetResetEventsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Failed to fetch sparkline/reset data: {ex.Message}");
                sparklineData = Array.Empty<double>();
                resetEvents = Array.Empty<CCStats.Core.Models.ResetEvent>();
            }

            // If DB has no history yet, build sparkline from accumulated polls
            if (sparklineData.Count < 2 && state.FiveHour is not null)
            {
                _inMemorySparkline.Add(state.FiveHour.Utilization);
                while (_inMemorySparkline.Count > 24) _inMemorySparkline.RemoveAt(0);
                sparklineData = _inMemorySparkline.ToArray();
                Debug.WriteLine($"[App] Sparkline: DB={sparklineData.Count - 1} pts, in-memory={_inMemorySparkline.Count} pts");
            }
            // Also use in-memory if it has more data than DB (early startup)
            else if (sparklineData.Count < _inMemorySparkline.Count && state.FiveHour is not null)
            {
                _inMemorySparkline.Add(state.FiveHour.Utilization);
                while (_inMemorySparkline.Count > 24) _inMemorySparkline.RemoveAt(0);
                sparklineData = _inMemorySparkline.ToArray();
            }
            else if (sparklineData.Count >= 2)
            {
                _inMemorySparkline.Clear(); // DB has real data, stop in-memory tracking
            }

            // Marshal state update to UI thread
            var src = state.DataSource == CCStats.Core.Models.UsageSource.Api ? "API" : state.DataSource.Label();
            AppLogger.Log("Poll", $"OK: 5h={state.FiveHour?.Utilization:F0}% 7d={state.SevenDay?.Utilization:F0}% src={src}");
            var stateWithSparkline = state with { SparklineData = sparklineData };
            Dispatcher.UIThread.Post(() =>
            {
                _viewModel!.SetResetEvents(resetEvents);
                _viewModel!.ApplyState(stateWithSparkline);
                _viewModel!.SetPollingComplete();

                // Detect connectivity recovery
                if (_wasDisconnected && stateWithSparkline.ConnectionStatus == ConnectionStatus.Connected)
                {
                    _notificationService?.ShowApiStatusAlert("Claude API is back online");
                }
                _wasDisconnected = stateWithSparkline.ConnectionStatus == ConnectionStatus.Disconnected;

                // Check headroom thresholds for notifications
                CheckHeadroomThresholds(stateWithSparkline);

                // Update tray icon to reflect current state
                _trayIconService?.UpdateIcon(
                    stateWithSparkline.FiveHour?.HeadroomPercentage ?? 0,
                    stateWithSparkline.MenuBarHeadroomState,
                    $"CC-Stats — {stateWithSparkline.MenuBarText}");

                // Refresh account list periodically so tier labels stay current
                var now = DateTimeOffset.UtcNow;
                if (now - _lastAccountLoadTime > AccountLoadThrottle)
                {
                    _lastAccountLoadTime = now;
                    _viewModel.LoadAccounts();
                    RefreshTrayAccountMenu();
                }
            });
          }
          catch (Exception ex)
          {
              AppLogger.Error("Poll", "PollCompleted handler error", ex);
              // Ensure spinner clears even if handler threw before UI dispatch
              Dispatcher.UIThread.Post(() => _viewModel!.SetPollingComplete());
          }
        };

        _pollingEngine.PollStarting += (_, _) =>
        {
            Dispatcher.UIThread.Post(() => _viewModel!.SetPollingActive());
        };

        _pollingEngine.PollFailed += (_, e) =>
        {
            AppLogger.Log("Poll", $"Failed: {e.Error}");
            var state = _pollingEngine.CurrentState;
            var nextInterval = _pollingEngine.LastComputedInterval;
            // Don't downgrade from authenticated to unauthenticated on transient failures
            // Only API 401 (TokenExpired) should trigger re-auth
            Dispatcher.UIThread.Post(() =>
            {
                // Never downgrade to signed-out if we have stored accounts — show
                // "session expired" in the status instead of the sign-in screen
                var hasStoredAccounts = _secureStorage!.ListAccounts().Count > 0;
                if (state.OAuthState == OAuthState.Unauthenticated && _viewModel!.IsAuthenticated)
                {
                    if (state.ConnectionStatus == ConnectionStatus.TokenExpired && hasStoredAccounts)
                    {
                        // Token expired but account exists — keep authenticated, show error
                        AppLogger.Log("Poll", "Token expired but account exists -- keeping authenticated, prompting re-auth");
                        _viewModel!.UpdateDataSource(state.DataSource, state.CacheAgeSeconds);
                    }
                    else
                    {
                        AppLogger.Log("Poll", "Ignoring auth state downgrade -- keeping authenticated");
                        _viewModel!.UpdateDataSource(state.DataSource, state.CacheAgeSeconds);
                    }
                }
                else
                {
                    // Only show sign-in if we truly have no accounts
                    if (state.OAuthState == OAuthState.Unauthenticated && hasStoredAccounts)
                    {
                        // Accounts exist but we're not authenticated yet — apply authenticated
                        var acctCreds = _secureStorage.ListAccounts()
                            .Select(id => _secureStorage.LoadAccountCredentials(id))
                            .FirstOrDefault(c => c is not null);
                        _viewModel!.ApplyState(state with
                        {
                            OAuthState = OAuthState.Authenticated,
                            SubscriptionTier = acctCreds?.DisplayName ?? state.SubscriptionTier,
                        });
                        AppLogger.Log("Poll", "Promoted to authenticated -- stored accounts exist");
                    }
                    else
                    {
                        _viewModel!.ApplyState(state);
                    }
                }
                // Always clear the spinner and show the error, even if state downgrade was ignored
                // Append next retry time if the engine has computed an interval
                var errorText = e.Error;
                if (nextInterval.TotalSeconds > 0)
                {
                    var label = nextInterval.TotalSeconds >= 60
                        ? $"{nextInterval.TotalMinutes:F0}m"
                        : $"{nextInterval.TotalSeconds:F0}s";
                    errorText = $"{e.Error} - retry in {label}";
                }
                _viewModel!.SetPollingError(errorText);
            });
        };
    }

    private void CheckHeadroomThresholds(AppState state)
    {
        if (_notificationService is null || _preferences is null) return;
        if (state.ConnectionStatus != ConnectionStatus.Connected) return;

        var fiveHourHeadroom = state.FiveHour?.HeadroomPercentage;
        if (fiveHourHeadroom is null) return;

        // Check critical threshold (check first so both can fire independently)
        if (fiveHourHeadroom <= _preferences.CriticalThreshold)
        {
            _notificationService.ShowThresholdAlert(HeadroomState.Critical, fiveHourHeadroom.Value);
        }
        // Check warning threshold
        else if (fiveHourHeadroom <= _preferences.WarningThreshold)
        {
            var headroomState = state.FiveHour!.HeadroomState;
            _notificationService.ShowThresholdAlert(headroomState, fiveHourHeadroom.Value);
        }

        // Billing period key: "YYYY-MM-dd" based on billing cycle day
        var billingDay = _preferences.BillingCycleDay > 0 ? _preferences.BillingCycleDay : 1;
        var now = DateTime.Now;
        var periodStart = now.Day >= billingDay
            ? new DateTime(now.Year, now.Month, billingDay)
            : new DateTime(now.Year, now.Month, billingDay).AddMonths(-1);
        var periodKey = periodStart.ToString("yyyy-MM-dd");

        if (_preferences.LastExtraUsagePeriodKey != periodKey)
        {
            _preferences.LastExtraUsagePeriodKey = periodKey;
            _preferences.ExtraUsage50Fired = false;
            _preferences.ExtraUsage75Fired = false;
            _preferences.ExtraUsage90Fired = false;
            _preferences.ExtraUsageEnteredFired = false;
            _preferences.Save();
        }

        // Check extra usage thresholds (billing-period-aware, fires once per period)
        if (state.ExtraUsageEnabled && state.ExtraUsageUtilization is { } extraUtil)
        {
            if (extraUtil >= 0.9 && _preferences.ExtraUsageAlert90 && !_preferences.ExtraUsage90Fired)
            {
                _notificationService.ShowExtraUsageAlert(ExtraUsageAlertLevel.Ninety, extraUtil * 100);
                _preferences.ExtraUsage90Fired = true;
                _preferences.Save();
            }
            else if (extraUtil >= 0.75 && _preferences.ExtraUsageAlert75 && !_preferences.ExtraUsage75Fired)
            {
                _notificationService.ShowExtraUsageAlert(ExtraUsageAlertLevel.SeventyFive, extraUtil * 100);
                _preferences.ExtraUsage75Fired = true;
                _preferences.Save();
            }
            else if (extraUtil >= 0.5 && _preferences.ExtraUsageAlert50 && !_preferences.ExtraUsage50Fired)
            {
                _notificationService.ShowExtraUsageAlert(ExtraUsageAlertLevel.Fifty, extraUtil * 100);
                _preferences.ExtraUsage50Fired = true;
                _preferences.Save();
            }
        }

        // Entered extra usage alert (once per billing period)
        if (state.IsExtraUsageActive && !_preferences.ExtraUsageEnteredFired)
        {
            _notificationService.ShowEnteredExtraUsageAlert();
            _preferences.ExtraUsageEnteredFired = true;
            _preferences.Save();
        }
    }

    // --- OAuth Flow Wiring ---

    private void WireOAuthFlow()
    {
        _oauthService!.AuthStarted += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _viewModel!.ApplyState(AppState.CreatePreviewAuthorizing());
            });
        };

        _oauthService.AuthCompleted += async (_, e) =>
        {
            try
            {
                var tokenData = JsonSerializer.Deserialize<OAuthTokenResponse>(e.TokenResponseJson,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        PropertyNameCaseInsensitive = true,
                    });

                if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _viewModel!.ApplyState(AppState.CreatePreviewSignedOut());
                    });
                    return;
                }

                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenData.ExpiresIn);

                // Detect tier from profile API before saving
                _apiClient!.SetAccessToken(tokenData.AccessToken);
                string? resolvedTier = tokenData.SubscriptionType;
                try
                {
                    var profile = await _apiClient.FetchProfileAsync(CancellationToken.None);
                    resolvedTier = profile?.Organization?.RateLimitTier
                                ?? profile?.Organization?.OrganizationType
                                ?? tokenData.SubscriptionType;
                    AppLogger.Log("Auth", $"Profile tier: {resolvedTier}");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Auth", "Profile fetch during auth failed", ex);
                }

                var credentials = new StoredCredentials
                {
                    AccessToken = tokenData.AccessToken,
                    RefreshToken = tokenData.RefreshToken,
                    ExpiresAt = expiresAt,
                    SubscriptionType = resolvedTier,
                };

                // Determine if this is "add new account" vs "re-auth"
                // If polling is already running, the user is authenticated and
                // clicked "+ Add Account" from Settings — always create new.
                var existingAccounts = _secureStorage!.ListAccounts();
                var isAddingNewAccount = _pollingEngine!.IsRunning && existingAccounts.Count > 0;

                string? matchedAccountId = null;

                if (!isAddingNewAccount && existingAccounts.Count > 0)
                {
                    // Re-auth flow: try to match an existing account
                    if (existingAccounts.Count == 1)
                    {
                        matchedAccountId = existingAccounts[0];
                        AppLogger.Log("Auth", $"Re-auth: matched single existing account {matchedAccountId}");
                    }
                    else
                    {
                        // Multiple accounts — try to match by subscription type
                        foreach (var id in existingAccounts)
                        {
                            var existing = _secureStorage.LoadAccountCredentials(id);
                            if (existing?.SubscriptionType == resolvedTier)
                            {
                                matchedAccountId = id;
                                AppLogger.Log("Auth", $"Re-auth: matched account {id} by tier {resolvedTier}");
                                break;
                            }
                        }
                    }
                }

                if (matchedAccountId is not null)
                {
                    // Update existing account with new tokens (preserve display name)
                    var existing = _secureStorage.LoadAccountCredentials(matchedAccountId);
                    var updated = credentials with
                    {
                        AccountId = matchedAccountId,
                        DisplayName = existing?.DisplayName ?? RateLimitTierExtensions.FromString(resolvedTier).DisplayName(),
                    };
                    _secureStorage.SaveAccountCredentials(matchedAccountId, updated);
                    _secureStorage.SaveCredentials(updated);
                    AppLogger.Log("Auth", $"Updated existing account \"{updated.DisplayName}\"");

                    // Skip naming prompt — go straight to authenticated
                    _pollingEngine!.Start();
                    Dispatcher.UIThread.Post(() =>
                    {
                        _viewModel!.ApplyState(new AppState
                        {
                            OAuthState = OAuthState.Authenticated,
                            ConnectionStatus = ConnectionStatus.Disconnected,
                            SubscriptionTier = RateLimitTierExtensions.FromString(resolvedTier).DisplayName(),
                        });
                        _viewModel.LoadAccounts();
                        _viewModel.ShowToastMessage($"Welcome back, {updated.DisplayName}!", 3000, "success");
                    });
                }
                else
                {
                    // Genuinely new account — prompt for name
                    _secureStorage.SaveCredentials(credentials);
                    if (!_pollingEngine!.IsRunning)
                        _pollingEngine.Start();
                    Dispatcher.UIThread.Post(() =>
                    {
                        _viewModel!.ShowSettings = false; // close settings to show naming prompt
                        _viewModel.PromptAccountName(credentials);
                        _viewModel!.ApplyState(new AppState
                        {
                            OAuthState = OAuthState.Authenticated,
                            ConnectionStatus = ConnectionStatus.Disconnected,
                            SubscriptionTier = RateLimitTierExtensions.FromString(resolvedTier).DisplayName(),
                        });
                    });
                    AppLogger.Log("Auth", "New account — prompting for name");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Auth", "OAuth token processing failed", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    _viewModel!.ApplyState(AppState.CreatePreviewSignedOut());
                });
            }
        };

        _oauthService.AuthFailed += (_, e) =>
        {
            Debug.WriteLine($"[App] OAuth failed: {e.Error}");
            Dispatcher.UIThread.Post(() =>
            {
                _viewModel!.ApplyState(AppState.CreatePreviewSignedOut());
            });
        };
    }

    // --- Notification Service Wiring ---

    private void WireNotificationService()
    {
        _notificationService!.NotificationRequested += (_, e) =>
        {
            // For now, just log notifications. Will be wired to Windows toast notifications later.
            Debug.WriteLine($"[Notification] {e.Title}: {e.Body}");
        };
    }

    // --- Preferences Changes Wiring ---

    private void WirePreferencesChanges()
    {
        _preferences!.PreferencesChanged += (_, _) =>
        {
            // Restart polling if interval changed
            if (_preferences.PollIntervalSeconds != _lastPollIntervalSeconds)
            {
                _lastPollIntervalSeconds = _preferences.PollIntervalSeconds;
                if (_pollingEngine!.IsRunning)
                {
                    _pollingEngine.Stop();
                    _pollingEngine.Start();
                }
            }
        };
    }

    // --- Sign Out ---

    public void SignOut()
    {
        AppLogger.Log("Auth", "Signed out");
        _pollingEngine?.Stop();
        _secureStorage?.ClearCredentials();
        _fiveHourSlope?.Clear();
        _sevenDaySlope?.Clear();
        _apiClient?.SetAccessToken(string.Empty);

        Dispatcher.UIThread.Post(() =>
        {
            _viewModel?.SetPollingComplete(); // Clear spinner if poll was in-flight
            _viewModel?.ApplyState(AppState.CreatePreviewSignedOut());
        });
    }

    // --- Window Management ---

    public void ToggleMainWindow()
    {
        if (_mainWindow is null) return;

        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            PositionWindowNearTray();
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    private void PositionWindowNearTray()
    {
        if (_mainWindow is null) return;
        TrayIconService.PositionWindowNearTray(_mainWindow);
    }

    private void OnOpenAnalyticsRequested(object? sender, EventArgs e)
    {
        if (_viewModel?.Analytics is null) return;
        var analyticsWindow = new AnalyticsWindow
        {
            DataContext = _viewModel.Analytics,
        };
        analyticsWindow.Show();
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        // Settings is shown as a view within the main window via ViewModel toggle.
    }

    private void OnSwitchAccountRequested(object? sender, string accountId)
    {
        if (_secureStorage is null || _pollingEngine is null || _apiClient is null) return;

        var creds = _secureStorage.LoadAccountCredentials(accountId);
        if (creds is null || string.IsNullOrEmpty(creds.AccessToken)) return;

        AppLogger.Log("Auth", $"Switching to account {accountId[..Math.Min(6, accountId.Length)]} ({creds.DisplayName})");

        // Stop current polling
        _pollingEngine.Stop();
        _fiveHourSlope?.Clear();
        _sevenDaySlope?.Clear();

        // Set the new account as the active credentials
        _secureStorage.SaveCredentials(creds);
        _apiClient.SetAccessToken(creds.AccessToken);

        // Update UI synchronously on the UI thread — show connecting state
        _viewModel?.ApplyState(new AppState
        {
            OAuthState = OAuthState.Authenticated,
            ConnectionStatus = ConnectionStatus.Connected,
            SubscriptionTier = creds.DisplayName ?? "Switching...",
        });
        _viewModel?.LoadAccounts();
        RefreshTrayAccountMenu();

        // Start polling and force immediate data fetch
        _pollingEngine.Start();
        _ = ForceImmediatePollAsync();

        _trayIconService?.UpdateIcon(0, CCStats.Core.Models.HeadroomState.Disconnected,
            $"CC-Stats — {creds.DisplayName ?? accountId}");
    }

    private void RefreshTrayAccountMenu()
    {
        _trayIconService?.UpdateAccountMenu(
            _viewModel?.Settings?.Accounts.Select(a => new AccountInfo
            {
                Id = a.AccountId,
                DisplayName = a.DisplayName,
                IsActive = a.IsActive,
            }).ToList() ?? new List<AccountInfo>(),
            accountId => Dispatcher.UIThread.Post(() => OnSwitchAccountRequested(null, accountId)));
    }

    private async System.Threading.Tasks.Task ForceImmediatePollAsync()
    {
        try
        {
            await _pollingEngine!.PollOnceAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Immediate poll after switch failed: {ex.Message}");
        }
    }

    private void OnQuitRequested(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // --- PromoClock Polling ---

    private async Task PollPromoClockAsync()
    {
        while (true)
        {
            try
            {
                if (_preferences!.PromoClockEnabled)
                {
                    // Peak hours: weekdays 9 AM - 5 PM local time (highest API contention)
                    var now = DateTime.Now;
                    var isWeekday = now.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
                    var isPeak = isWeekday && now.Hour >= 9 && now.Hour < 17;

                    Dispatcher.UIThread.Post(() =>
                    {
                        _viewModel!.ShowPromoClock = true;
                        _viewModel.PromoClockLabel = isPeak ? "Peak" : "Off-Peak";
                    });
                }
                else
                {
                    Dispatcher.UIThread.Post(() => _viewModel!.ShowPromoClock = false);
                }
            }
            catch { }

            try { await Task.Delay(TimeSpan.FromMinutes(5)); }
            catch (TaskCanceledException) { break; }
        }
    }

    // --- Clean Shutdown ---

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        CleanupServices();
    }

    private void CleanupServices()
    {
        _trayIconService?.Dispose();
        _pollingEngine?.Stop();
        _pollingEngine?.Dispose();
        _oauthService?.Dispose();
        _tokenRefresh?.Dispose();
        _apiClient?.Dispose();
        _promoClockService?.Dispose();

        // Dispose database — block briefly to ensure connection closes cleanly
        if (_database is not null)
        {
            try { _database.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { Debug.WriteLine($"[App] Database dispose error: {ex.Message}"); }
        }

        // Save preferences
        _preferences?.Save();
    }

    // --- Internal DTO for OAuth token response ---

    private sealed record OAuthTokenResponse
    {
        public string? AccessToken { get; init; }
        public string? RefreshToken { get; init; }
        public int ExpiresIn { get; init; }
        public string? TokenType { get; init; }
        public string? SubscriptionType { get; init; }
    }
}
