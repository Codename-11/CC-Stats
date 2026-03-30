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
    private TimeSpan? _retryAfterOverride;
    private bool _authFailed; // stops poll loop from retrying expired tokens

    private readonly object _lock = new();
    private readonly SemaphoreSlim _pollGate = new(1, 1); // prevent concurrent polls
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    private AppState _currentState = new();

    public event EventHandler? PollStarting;
    public event EventHandler<PollCompletedEventArgs>? PollCompleted;
    public event EventHandler<PollFailedEventArgs>? PollFailed;

    public AppState CurrentState
    {
        get { lock (_lock) return _currentState; }
    }

    /// <summary>The most recently computed poll interval (for UI display).</summary>
    public TimeSpan LastComputedInterval { get; private set; }

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
        _authFailed = false;
        _consecutiveFailures = 0;
        _retryAfterOverride = null;
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
        if (_authFailed) return; // token expired — need re-auth, not retry
        if (!await _pollGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            await ExecutePollCycleAsync(cancellationToken);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _pollGate.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _pollGate.WaitAsync(cancellationToken);
                try
                {
                    await ExecutePollCycleAsync(cancellationToken);
                }
                finally
                {
                    _pollGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Poll", "Poll cycle exception", ex);
            }

            try
            {
                var interval = ComputeNextInterval();
                LastComputedInterval = interval;
                AppLogger.Log("Poll", $"Next in {interval.TotalSeconds:F0}s (failures: {_consecutiveFailures}, retryAfter: {_retryAfterOverride?.TotalSeconds.ToString("F0") ?? "none"})");
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ExecutePollCycleAsync(CancellationToken cancellationToken)
    {
        // Don't retry if auth has permanently failed — wait for re-auth via Start()
        if (_authFailed) return;

        var now = DateTimeOffset.UtcNow;
        PollStarting?.Invoke(this, EventArgs.Empty);

        try
        {
            // Load credentials — don't reset OAuthState on transient file read failures
            var credentials = _secureStorage.LoadCredentials();
            if (credentials is null || string.IsNullOrEmpty(credentials.AccessToken))
            {
                _consecutiveFailures = 0;
                _retryAfterOverride = null;
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
                    _consecutiveFailures = 0;
                    _retryAfterOverride = null;
                    UpdateState(state => state with
                    {
                        ConnectionStatus = ConnectionStatus.TokenExpired,
                        LastAttempted = now,
                    });
                    PollFailed?.Invoke(this, new PollFailedEventArgs("Session expired"));
                    return;
                }

                var refreshResult = await _tokenRefreshService.RefreshTokenAsync(credentials.RefreshToken, cancellationToken);
                if (!refreshResult.IsSuccess)
                {
                    _authFailed = true; // stop poll loop from retrying
                    UpdateState(state => state with
                    {
                        ConnectionStatus = ConnectionStatus.TokenExpired,
                        OAuthState = OAuthState.Unauthenticated,
                        LastAttempted = now,
                    });
                    PollFailed?.Invoke(this, new PollFailedEventArgs("Token refresh failed - sign in again"));
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
                DataSource = Models.UsageSource.Api,
                CacheAgeSeconds = null,
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
            _retryAfterOverride = null;

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
            _retryAfterOverride = ex.RetryAfter > TimeSpan.Zero ? ex.RetryAfter : TimeSpan.FromSeconds(30);

            // Fallback: try local cache — records to DB and fires PollCompleted if successful
            var cacheApplied = await TryApplyLocalCacheFallbackAsync(now);
            if (cacheApplied)
            {
                // Cache provided data — reset backoff entirely
                _consecutiveFailures = 0;
                _retryAfterOverride = null;
            }
            else
            {
                _consecutiveFailures++;
                PollFailed?.Invoke(this, new PollFailedEventArgs("Rate limited"));
            }
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

    /// <summary>
    /// Computes the next polling interval with exponential backoff on consecutive failures.
    /// Retry-After from 429 acts as a floor. Capped at 3600 seconds (1 hour).
    /// </summary>
    private TimeSpan ComputeNextInterval()
    {
        int baseSeconds;
        try
        {
            baseSeconds = _preferences.AdaptivePolling
                ? SessionDetectionService.GetAdaptiveInterval(_preferences.PollIntervalSeconds)
                : _preferences.PollIntervalSeconds;
        }
        catch
        {
            baseSeconds = _preferences.PollIntervalSeconds;
        }

        var retryFloor = _retryAfterOverride ?? TimeSpan.Zero;

        if (_consecutiveFailures <= 1)
        {
            var interval = Math.Max(baseSeconds, retryFloor.TotalSeconds);
            return TimeSpan.FromSeconds(Math.Min(Math.Max(interval, 10), 3600));
        }

        // Exponential backoff: base * 2^(failures-1), capped at 1 hour
        var exponent = Math.Min(_consecutiveFailures - 1, 10);
        var backoff = baseSeconds * Math.Pow(2, exponent);
        var final = Math.Min(Math.Max(backoff, retryFloor.TotalSeconds), 3600);
        return TimeSpan.FromSeconds(Math.Max(final, 10));
    }

    private void UpdateState(Func<AppState, AppState> updater)
    {
        lock (_lock)
        {
            _currentState = updater(_currentState);
        }
    }

    /// <summary>
    /// When API fails, try to update state from Claude Code's local statusline cache.
    /// This provides fresher data than the last successful poll.
    /// </summary>
    /// <summary>
    /// When API fails, try local cache. Records to DB and fires PollCompleted if data found.
    /// Returns true if cache was applied (caller should skip PollFailed).
    /// </summary>
    private async Task<bool> TryApplyLocalCacheFallbackAsync(DateTimeOffset now)
    {
        try
        {
            var cache = LocalCacheService.ReadCache();
            if (cache?.FiveHourUtilization is null) return false;

            var fhReset = ParseTimestamp(cache.FiveHourResetsAt);
            var sdReset = ParseTimestamp(cache.SevenDayResetsAt);
            var cacheAge = cache.FetchedAt.HasValue
                ? (int)(DateTimeOffset.UtcNow - cache.FetchedAt.Value).TotalSeconds
                : (int?)null;

            UpdateState(state => state with
            {
                DataSource = Models.UsageSource.Cached,
                CacheAgeSeconds = cacheAge,
                // Keep existing ConnectionStatus — we're rate-limited, not truly connected
                OAuthState = OAuthState.Authenticated,
                FiveHour = new Models.WindowState(cache.FiveHourUtilization.Value, fhReset),
                SevenDay = cache.SevenDayUtilization is not null
                    ? new Models.WindowState(cache.SevenDayUtilization.Value, sdReset)
                    : state.SevenDay,
                LastUpdated = now, // use current time, not stale cache time
                ExtraUsageEnabled = cache.ExtraUsageEnabled ?? state.ExtraUsageEnabled,
                ExtraUsageUtilization = cache.ExtraUsageUtilization ?? state.ExtraUsageUtilization,
            });

            // PollCompleted handler will record to DB — don't insert here (avoids double-recording)
            PollCompleted?.Invoke(this, new PollCompletedEventArgs(now));
            AppLogger.Log("Poll", $"Cache fallback OK ({cacheAge}s old, 5h={cache.FiveHourUtilization:F0}%)");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PollingEngine] Local cache fallback failed: {ex.Message}");
            return false;
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
