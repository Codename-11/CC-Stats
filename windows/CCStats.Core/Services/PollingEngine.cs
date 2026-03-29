using System.Runtime.Versioning;
using CCStats.Core.Models;
using CCStats.Core.State;

namespace CCStats.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class PollingEngine : IDisposable
{
    private readonly APIClient _apiClient;
    private readonly SecureStorageService _secureStorage;
    private readonly TokenRefreshService _tokenRefreshService;
    private readonly PreferencesManager _preferences;
    private readonly SlopeCalculationService _fiveHourSlope;
    private readonly SlopeCalculationService _sevenDaySlope;
    private readonly DatabaseManager? _database;

    private int _consecutiveFailures;
    private bool _outageActive;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    private AppState _currentState = new();

    public event EventHandler<PollCompletedEventArgs>? PollCompleted;
    public event EventHandler<PollFailedEventArgs>? PollFailed;

    public AppState CurrentState
    {
        get { lock (_lock) return _currentState; }
    }

    public bool IsRunning { get { lock (_lock) return _pollingTask is not null && !_pollingTask.IsCompleted; } }

    public PollingEngine(
        APIClient apiClient,
        SecureStorageService secureStorage,
        TokenRefreshService tokenRefreshService,
        PreferencesManager preferences,
        SlopeCalculationService fiveHourSlope,
        SlopeCalculationService sevenDaySlope,
        DatabaseManager? database = null)
    {
        _apiClient = apiClient;
        _secureStorage = secureStorage;
        _tokenRefreshService = tokenRefreshService;
        _preferences = preferences;
        _fiveHourSlope = fiveHourSlope;
        _sevenDaySlope = sevenDaySlope;
        _database = database;
    }

    public void Start()
    {
        Stop();
        var cts = new CancellationTokenSource();
        var task = PollLoopAsync(cts.Token);
        lock (_lock)
        {
            _cts = cts;
            _pollingTask = task;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_lock)
        {
            cts = _cts;
            task = _pollingTask;
            _cts = null;
            _pollingTask = null;
        }

        cts?.Cancel();

        // Wait briefly for the polling task to finish before disposing the CTS
        if (task is not null)
        {
            try { task.Wait(TimeSpan.FromSeconds(2)); } catch { /* expected OperationCanceled / AggregateException */ }
        }

        cts?.Dispose();
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken = default)
    {
        await ExecutePollCycleAsync(cancellationToken);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ExecutePollCycleAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PollingEngine] Poll cycle failed: {ex.Message}");
            }

            try
            {
                int seconds;
                try
                {
                    seconds = _preferences.AdaptivePolling
                        ? SessionDetectionService.GetAdaptiveInterval(_preferences.PollIntervalSeconds)
                        : _preferences.PollIntervalSeconds;
                }
                catch
                {
                    seconds = _preferences.PollIntervalSeconds; // fallback if session detection fails
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, seconds)), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ExecutePollCycleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        try
        {
            // Load credentials — don't reset OAuthState on transient file read failures
            var credentials = _secureStorage.LoadCredentials();
            if (credentials is null || string.IsNullOrEmpty(credentials.AccessToken))
            {
                UpdateState(state => state with
                {
                    ConnectionStatus = ConnectionStatus.NoCredentials,
                    // Keep existing OAuthState — only the API (401) should set Unauthenticated
                    // A missing file is a transient issue, not a sign-out
                    LastAttempted = now,
                });
                PollFailed?.Invoke(this, new PollFailedEventArgs("No credentials available"));
                return;
            }

            // Check and refresh token if needed
            if (TokenRefreshService.IsTokenExpired(credentials.ExpiresAt))
            {
                if (string.IsNullOrEmpty(credentials.RefreshToken))
                {
                    UpdateState(state => state with
                    {
                        ConnectionStatus = ConnectionStatus.TokenExpired,
                        LastAttempted = now,
                    });
                    return;
                }

                var refreshResult = await _tokenRefreshService.RefreshTokenAsync(credentials.RefreshToken, cancellationToken);
                if (!refreshResult.IsSuccess)
                {
                    UpdateState(state => state with
                    {
                        ConnectionStatus = ConnectionStatus.TokenExpired,
                        LastAttempted = now,
                    });
                    return;
                }

                credentials = credentials with
                {
                    AccessToken = refreshResult.AccessToken,
                    RefreshToken = refreshResult.RefreshToken ?? credentials.RefreshToken,
                    ExpiresAt = refreshResult.ExpiresAt,
                };
                _secureStorage.SaveCredentials(credentials);
            }

            _apiClient.SetAccessToken(credentials.AccessToken!);

            // Fetch usage data
            var usage = await _apiClient.FetchUsageAsync(cancellationToken);
            if (usage is null)
            {
                UpdateState(state => state with
                {
                    ConnectionStatus = ConnectionStatus.Disconnected,
                    LastAttempted = now,
                });
                _consecutiveFailures++;
                if (_consecutiveFailures >= 2 && !_outageActive && _database is not null)
                {
                    try { await _database.StartOutageAsync(now, "EmptyResponse"); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PollingEngine] Outage tracking error: {ex.Message}"); }
                    _outageActive = true;
                }
                PollFailed?.Invoke(this, new PollFailedEventArgs("Empty usage response"));
                return;
            }

            // Extract values from nested response structure
            var fiveHourUtil = usage.FiveHour?.Utilization ?? 0;
            var sevenDayUtil = usage.SevenDay?.Utilization;
            var fiveHourResetsAt = ParseTimestamp(usage.FiveHour?.ResetsAt);
            var sevenDayResetsAt = ParseTimestamp(usage.SevenDay?.ResetsAt);

            // Calculate slopes
            _fiveHourSlope.AddSample(fiveHourUtil, now);
            if (sevenDayUtil.HasValue)
            {
                _sevenDaySlope.AddSample(sevenDayUtil.Value, now);
            }

            // Determine credit limits — try stored credentials first, then profile API
            var tier = RateLimitTierExtensions.FromString(credentials.SubscriptionType);

            // Always try the rate_limit_tier from profile — it's the most accurate source
            if (tier == RateLimitTier.Unknown)
            {
                try
                {
                    var profile = await _apiClient.FetchProfileAsync(cancellationToken);
                    // rate_limit_tier is the definitive field (e.g. "default_claude_max_20x")
                    if (profile?.Organization?.RateLimitTier is { } rateLimitTier)
                    {
                        tier = RateLimitTierExtensions.FromString(rateLimitTier);
                    }
                    // Fall back to organization_type (e.g. "claude_max")
                    if (tier == RateLimitTier.Unknown && profile?.Organization?.OrganizationType is { } orgType)
                    {
                        tier = RateLimitTierExtensions.FromString(orgType);
                    }
                    // Persist the resolved tier to both active and per-account storage
                    if (tier != RateLimitTier.Unknown)
                    {
                        var tierString = profile?.Organization?.RateLimitTier
                                      ?? profile?.Organization?.OrganizationType;
                        if (tierString is not null)
                        {
                            credentials = credentials with { SubscriptionType = tierString };
                            _secureStorage.SaveCredentials(credentials);

                            // Also update the per-account file if it exists
                            if (credentials.AccountId is { } accountId)
                            {
                                var displayName = tier.DisplayName();
                                _secureStorage.SaveAccountCredentials(accountId,
                                    credentials with { DisplayName = displayName });
                            }
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PollingEngine] Profile fetch failed: {ex.Message}"); }
            }

            var creditLimits = tier.GetCreditLimits();

            // Apply custom overrides
            if (_preferences.CustomFiveHourCredits is not null || _preferences.CustomSevenDayCredits is not null)
            {
                creditLimits = new CreditLimits(
                    _preferences.CustomFiveHourCredits ?? creditLimits.FiveHourCredits,
                    _preferences.CustomSevenDayCredits ?? creditLimits.SevenDayCredits,
                    _preferences.CustomMonthlyPrice ?? creditLimits.MonthlyPrice);
            }

            UpdateState(state => state with
            {
                ConnectionStatus = ConnectionStatus.Connected,
                OAuthState = OAuthState.Authenticated,
                LastUpdated = now,
                LastAttempted = now,
                SubscriptionTier = tier.DisplayName(),
                CreditLimits = creditLimits,
                FiveHour = new WindowState(fiveHourUtil, fiveHourResetsAt),
                SevenDay = sevenDayUtil.HasValue
                    ? new WindowState(sevenDayUtil.Value, sevenDayResetsAt)
                    : null,
                FiveHourSlope = _fiveHourSlope.CalculateSlope(),
                FiveHourSlopeRate = _fiveHourSlope.GetRatePerMinute(),
                SevenDaySlope = _sevenDaySlope.CalculateSlope(),
                SevenDaySlopeRate = _sevenDaySlope.GetRatePerMinute(),
                ExtraUsageEnabled = usage.ExtraUsage?.IsEnabled ?? false,
                ExtraUsageMonthlyLimitCents = (int?)(usage.ExtraUsage?.MonthlyLimit),
                ExtraUsageUsedCreditsCents = (int?)(usage.ExtraUsage?.UsedCredits),
                ExtraUsageUtilization = usage.ExtraUsage?.Utilization,
            });

            // Clear outage tracking on success
            if (_outageActive && _database is not null)
            {
                try { await _database.CloseOutageAsync(now); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PollingEngine] Outage close error: {ex.Message}"); }
                _outageActive = false;
            }
            _consecutiveFailures = 0;

            PollCompleted?.Invoke(this, new PollCompletedEventArgs(now));
        }
        catch (ApiAuthenticationException)
        {
            UpdateState(state => state with
            {
                ConnectionStatus = ConnectionStatus.TokenExpired,
                LastAttempted = now,
            });
            PollFailed?.Invoke(this, new PollFailedEventArgs("Authentication failed"));
        }
        catch (ApiRateLimitException ex)
        {
            PollFailed?.Invoke(this, new PollFailedEventArgs($"Rate limited: retry after {ex.RetryAfter.TotalSeconds}s"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdateState(state => state with
            {
                ConnectionStatus = ConnectionStatus.Disconnected,
                LastAttempted = now,
            });
            _consecutiveFailures++;
            if (_consecutiveFailures >= 2 && !_outageActive && _database is not null)
            {
                try { await _database.StartOutageAsync(now, ex.GetType().Name); } catch (Exception dbEx) { System.Diagnostics.Debug.WriteLine($"[PollingEngine] Outage tracking error: {dbEx.Message}"); }
                _outageActive = true;
            }
            PollFailed?.Invoke(this, new PollFailedEventArgs(ex.Message));
        }
    }

    private void UpdateState(Func<AppState, AppState> updater)
    {
        lock (_lock)
        {
            _currentState = updater(_currentState);
        }
    }

    private static DateTimeOffset? ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp)) return null;
        return DateTimeOffset.TryParse(timestamp, out var result) ? result : null;
    }
}

public sealed class PollCompletedEventArgs : EventArgs
{
    public DateTimeOffset Timestamp { get; }

    public PollCompletedEventArgs(DateTimeOffset timestamp)
    {
        Timestamp = timestamp;
    }
}

public sealed class PollFailedEventArgs : EventArgs
{
    public string Error { get; }

    public PollFailedEventArgs(string error)
    {
        Error = error;
    }
}
