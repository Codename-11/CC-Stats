using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
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

    private int _lastPollIntervalSeconds;

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

            // Set up system tray icon
            _trayIconService = TrayIconService.Setup(
                this, _mainWindow,
                onAnalyticsRequested: () => Dispatcher.UIThread.Post(() => OnOpenAnalyticsRequested(null, EventArgs.Empty)),
                onSettingsRequested: () => Dispatcher.UIThread.Post(() => { _viewModel!.ShowSettings = true; _mainWindow?.Show(); _mainWindow?.Activate(); }),
                onSignOutRequested: () => Dispatcher.UIThread.Post(() => SignOut()));

            // Initialize all services and wire them together
            InitializeServices();

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

        // 3. Database (fire-and-forget init)
        _database = new DatabaseManager();
        _ = InitializeDatabaseAsync();

        // 4. Historical data
        _historyService = new HistoricalDataService(_database);

        // 5. API client
        _apiClient = new APIClient();

        // 6. Token refresh
        _tokenRefresh = new TokenRefreshService();

        // 7. Slope calculators (two instances: 5h and 7d)
        _fiveHourSlope = new SlopeCalculationService();
        _sevenDaySlope = new SlopeCalculationService();

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

        // --- Wire events ---
        WirePollingEngine();
        WireOAuthFlow();
        WireNotificationService();
        WirePreferencesChanges();

        // --- Connect services to ViewModel ---
        _viewModel!.ConnectServices(_oauthService, _pollingEngine, _preferences, _secureStorage, _database);

        // --- Auto-start if credentials exist ---
        var credentials = _secureStorage.LoadCredentials();
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

                _viewModel.ApplyState(new AppState
                {
                    OAuthState = OAuthState.Authenticated,
                    ConnectionStatus = ConnectionStatus.Connected,
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
                });
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

        // --- Fire-and-forget: check for updates ---
        _ = CheckForUpdatesAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await _database!.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Database initialization failed: {ex.Message}");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await _updateCheckService!.CheckForUpdateAsync();
            if (result is not null)
            {
                // Check if this version was dismissed
                if (_preferences!.DismissedVersion == result.LatestVersion)
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    _viewModel!.ShowUpdateBadge = true;
                    _viewModel.UpdateVersionText = result.LatestVersion;
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Update check failed: {ex.Message}");
        }
    }

    // --- Polling Engine Wiring ---

    private void WirePollingEngine()
    {
        _pollingEngine!.PollCompleted += async (_, e) =>
        {
            var state = _pollingEngine.CurrentState;

            // Record to database (fire-and-forget)
            try
            {
                if (state.FiveHour is not null)
                {
                    await _historyService!.RecordPollAsync(
                        state.FiveHour.Utilization,
                        state.SevenDay?.Utilization ?? 0,
                        state.FiveHour.ResetsAt,
                        state.SevenDay?.ResetsAt);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Failed to record poll: {ex.Message}");
            }

            // Get sparkline data
            IReadOnlyList<double> sparklineData;
            try
            {
                sparklineData = await _historyService!.GetSparklineDataAsync();
            }
            catch
            {
                sparklineData = state.SparklineData;
            }

            // Marshal state update to UI thread
            var stateWithSparkline = state with { SparklineData = sparklineData };
            Dispatcher.UIThread.Post(() =>
            {
                _viewModel!.ApplyState(stateWithSparkline);

                // Check headroom thresholds for notifications
                CheckHeadroomThresholds(stateWithSparkline);

                // Update tray icon to reflect current state
                _trayIconService?.UpdateIcon(
                    stateWithSparkline.FiveHour?.HeadroomPercentage ?? 0,
                    stateWithSparkline.MenuBarHeadroomState,
                    $"CC-Stats — {stateWithSparkline.MenuBarText}");

                // Refresh account list so tier labels stay current
                _viewModel.LoadAccounts();
            });
        };

        _pollingEngine.PollFailed += (_, e) =>
        {
            Debug.WriteLine($"[App] Poll failed: {e.Error}");
            var state = _pollingEngine.CurrentState;
            Dispatcher.UIThread.Post(() => _viewModel!.ApplyState(state));
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

        // Check extra usage thresholds
        if (state.ExtraUsageEnabled && state.ExtraUsageUtilization is { } extraUtil)
        {
            if (extraUtil >= 0.9 && _preferences.ExtraUsageAlert90)
                _notificationService.ShowExtraUsageAlert(ExtraUsageAlertLevel.Ninety, extraUtil * 100);
            else if (extraUtil >= 0.75 && _preferences.ExtraUsageAlert75)
                _notificationService.ShowExtraUsageAlert(ExtraUsageAlertLevel.SeventyFive, extraUtil * 100);
            else if (extraUtil >= 0.5 && _preferences.ExtraUsageAlert50)
                _notificationService.ShowExtraUsageAlert(ExtraUsageAlertLevel.Fifty, extraUtil * 100);
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

        _oauthService.AuthCompleted += (_, e) =>
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

                // Save credentials
                var credentials = new StoredCredentials
                {
                    AccessToken = tokenData.AccessToken,
                    RefreshToken = tokenData.RefreshToken,
                    ExpiresAt = expiresAt,
                    SubscriptionType = tokenData.SubscriptionType,
                };
                _secureStorage!.SaveCredentials(credentials);

                // Prompt user to name the account
                Dispatcher.UIThread.Post(() =>
                {
                    _viewModel!.PromptAccountName(credentials);
                });

                // Set API token and start polling
                _apiClient!.SetAccessToken(tokenData.AccessToken);
                _pollingEngine!.Start();

                Dispatcher.UIThread.Post(() =>
                {
                    _viewModel!.ApplyState(new AppState
                    {
                        OAuthState = OAuthState.Authenticated,
                        ConnectionStatus = ConnectionStatus.Disconnected,
                        SubscriptionTier = tokenData.SubscriptionType,
                    });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] OAuth token parsing failed: {ex.Message}");
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
        _pollingEngine?.Stop();
        _secureStorage?.ClearCredentials();
        _fiveHourSlope?.Clear();
        _sevenDaySlope?.Clear();
        _apiClient?.SetAccessToken(string.Empty);

        Dispatcher.UIThread.Post(() =>
        {
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

        // Position near bottom-right of screen (near system tray)
        var screen = _mainWindow.Screens.Primary;
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;
        var windowWidth = (int)(_mainWindow.Width * scaling);
        var x = (workArea.Right - windowWidth - (int)(8 * scaling)) / scaling;

        _mainWindow.Position = new PixelPoint((int)x, workArea.Bottom - 600);
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

        // Start polling and force immediate data fetch
        _pollingEngine.Start();
        _ = ForceImmediatePollAsync();

        _trayIconService?.UpdateIcon(0, CCStats.Core.Models.HeadroomState.Disconnected,
            $"CC-Stats — {creds.DisplayName ?? accountId}");
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
        _apiClient?.Dispose();

        // Dispose database (fire-and-forget since we're shutting down)
        if (_database is not null)
        {
            _ = _database.DisposeAsync();
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
