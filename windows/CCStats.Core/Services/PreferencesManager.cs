using System.Text.Json;
using System.Text.Json.Serialization;

namespace CCStats.Core.Services;

public sealed class PreferencesManager
{
    private static readonly int[] ValidPollIntervals = [10, 15, 30, 60, 120, 300, 600, 900, 1800];
    private static readonly int[] ValidRetentionDays = [30, 90, 180, 365, 730, 1825];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly object _lock = new();
    private PreferencesData _data = new();

    public event EventHandler? PreferencesChanged;

    public PreferencesManager()
        : this(GetDefaultFilePath())
    {
    }

    public PreferencesManager(string filePath)
    {
        _filePath = filePath;
    }

    public int WarningThreshold
    {
        get { lock (_lock) return _data.WarningThreshold; }
        set { lock (_lock) { _data.WarningThreshold = Math.Clamp(value, 6, 50); Validate(); Save(); } }
    }

    public int CriticalThreshold
    {
        get { lock (_lock) return _data.CriticalThreshold; }
        set { lock (_lock) { _data.CriticalThreshold = Math.Clamp(value, 1, 49); Validate(); Save(); } }
    }

    public int PollIntervalSeconds
    {
        get { lock (_lock) return _data.PollIntervalSeconds; }
        set { lock (_lock) { _data.PollIntervalSeconds = ValidPollIntervals.Contains(value) ? value : 120; Save(); } }
    }

    public int DataRetentionDays
    {
        get { lock (_lock) return _data.DataRetentionDays; }
        set { lock (_lock) { _data.DataRetentionDays = ValidRetentionDays.Contains(value) ? value : 365; Save(); } }
    }

    public int? CustomFiveHourCredits
    {
        get { lock (_lock) return _data.CustomFiveHourCredits; }
        set { lock (_lock) { _data.CustomFiveHourCredits = value; Save(); } }
    }

    public int? CustomSevenDayCredits
    {
        get { lock (_lock) return _data.CustomSevenDayCredits; }
        set { lock (_lock) { _data.CustomSevenDayCredits = value; Save(); } }
    }

    public double? CustomMonthlyPrice
    {
        get { lock (_lock) return _data.CustomMonthlyPrice; }
        set { lock (_lock) { _data.CustomMonthlyPrice = value; Save(); } }
    }

    public int BillingCycleDay
    {
        get { lock (_lock) return _data.BillingCycleDay; }
        set { lock (_lock) { _data.BillingCycleDay = Math.Clamp(value, 0, 28); Save(); } }
    }

    public bool LaunchAtLogin
    {
        get { lock (_lock) return _data.LaunchAtLogin; }
        set { lock (_lock) { _data.LaunchAtLogin = value; Save(); } }
    }

    public bool ApiStatusAlerts
    {
        get { lock (_lock) return _data.ApiStatusAlerts; }
        set { lock (_lock) { _data.ApiStatusAlerts = value; Save(); } }
    }

    public bool ExtraUsageAlerts
    {
        get { lock (_lock) return _data.ExtraUsageAlerts; }
        set { lock (_lock) { _data.ExtraUsageAlerts = value; Save(); } }
    }

    public bool ExtraUsageAlert50
    {
        get { lock (_lock) return _data.ExtraUsageAlert50; }
        set { lock (_lock) { _data.ExtraUsageAlert50 = value; Save(); } }
    }

    public bool ExtraUsageAlert75
    {
        get { lock (_lock) return _data.ExtraUsageAlert75; }
        set { lock (_lock) { _data.ExtraUsageAlert75 = value; Save(); } }
    }

    public bool ExtraUsageAlert90
    {
        get { lock (_lock) return _data.ExtraUsageAlert90; }
        set { lock (_lock) { _data.ExtraUsageAlert90 = value; Save(); } }
    }

    public bool AdaptivePolling
    {
        get { lock (_lock) return _data.AdaptivePolling; }
        set { lock (_lock) { _data.AdaptivePolling = value; Save(); } }
    }

    public bool HasCompletedOnboarding
    {
        get { lock (_lock) return _data.HasCompletedOnboarding; }
        set { lock (_lock) { _data.HasCompletedOnboarding = value; Save(); } }
    }

    public string? DismissedVersion
    {
        get { lock (_lock) return _data.DismissedVersion; }
        set { lock (_lock) { _data.DismissedVersion = value; Save(); } }
    }

    public string? LastExtraUsagePeriodKey
    {
        get { lock (_lock) return _data.LastExtraUsagePeriodKey; }
        set { lock (_lock) { _data.LastExtraUsagePeriodKey = value; Save(); } }
    }

    public bool ExtraUsage50Fired
    {
        get { lock (_lock) return _data.ExtraUsage50Fired; }
        set { lock (_lock) { _data.ExtraUsage50Fired = value; Save(); } }
    }

    public bool ExtraUsage75Fired
    {
        get { lock (_lock) return _data.ExtraUsage75Fired; }
        set { lock (_lock) { _data.ExtraUsage75Fired = value; Save(); } }
    }

    public bool ExtraUsage90Fired
    {
        get { lock (_lock) return _data.ExtraUsage90Fired; }
        set { lock (_lock) { _data.ExtraUsage90Fired = value; Save(); } }
    }

    public bool ExtraUsageEnteredFired
    {
        get { lock (_lock) return _data.ExtraUsageEnteredFired; }
        set { lock (_lock) { _data.ExtraUsageEnteredFired = value; Save(); } }
    }

    public List<string> DismissedPatternFindings
    {
        get { lock (_lock) return _data.DismissedPatternFindings; }
        set { lock (_lock) { _data.DismissedPatternFindings = value; Save(); } }
    }

    public string? DismissedTierRecommendation
    {
        get { lock (_lock) return _data.DismissedTierRecommendation; }
        set { lock (_lock) { _data.DismissedTierRecommendation = value; Save(); } }
    }

    public bool PromoClockEnabled
    {
        get { lock (_lock) return _data.PromoClockEnabled; }
        set { lock (_lock) { _data.PromoClockEnabled = value; Save(); } }
    }

    public string? PromoClockApiKey
    {
        get { lock (_lock) return _data.PromoClockApiKey; }
        set { lock (_lock) { _data.PromoClockApiKey = value; Save(); } }
    }

    public string? PromoClockTeamId
    {
        get { lock (_lock) return _data.PromoClockTeamId; }
        set { lock (_lock) { _data.PromoClockTeamId = value; Save(); } }
    }

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                _data = new PreferencesData();
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                _data = JsonSerializer.Deserialize<PreferencesData>(json, JsonOptions) ?? new PreferencesData();
                Validate();
            }
            catch (Exception ex)
            {
                AppLogger.Error("PreferencesManager", $"Failed to load preferences: {ex.Message}", ex);

                try
                {
                    var backupPath = _filePath + ".backup";
                    File.Copy(_filePath, backupPath, overwrite: true);
                    AppLogger.Log("PreferencesManager", $"Corrupted preferences backed up to {backupPath}");
                }
                catch (Exception backupEx)
                {
                    AppLogger.Error("PreferencesManager", $"Failed to backup corrupted preferences: {backupEx.Message}", backupEx);
                }

                _data = new PreferencesData();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_data, JsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("PreferencesManager", $"Failed to save preferences: {ex.Message}", ex);
            }
        }

        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Validate()
    {
        _data.WarningThreshold = Math.Clamp(_data.WarningThreshold, 6, 50);
        _data.CriticalThreshold = Math.Clamp(_data.CriticalThreshold, 1, 49);

        if (_data.CriticalThreshold >= _data.WarningThreshold)
        {
            _data.CriticalThreshold = _data.WarningThreshold - 1;
        }

        if (!ValidPollIntervals.Contains(_data.PollIntervalSeconds))
        {
            _data.PollIntervalSeconds = 120;
        }

        if (!ValidRetentionDays.Contains(_data.DataRetentionDays))
        {
            _data.DataRetentionDays = 365;
        }

        _data.BillingCycleDay = Math.Clamp(_data.BillingCycleDay, 0, 28);
    }

    private static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CCStats", "preferences.json");
    }

    private sealed class PreferencesData
    {
        public int WarningThreshold { get; set; } = 20;
        public int CriticalThreshold { get; set; } = 5;
        public int PollIntervalSeconds { get; set; } = 120;
        public int DataRetentionDays { get; set; } = 365;
        public int? CustomFiveHourCredits { get; set; }
        public int? CustomSevenDayCredits { get; set; }
        public double? CustomMonthlyPrice { get; set; }
        public int BillingCycleDay { get; set; }
        public bool LaunchAtLogin { get; set; }
        public bool ApiStatusAlerts { get; set; } = true;
        public bool ExtraUsageAlerts { get; set; } = true;
        public bool ExtraUsageAlert50 { get; set; } = true;
        public bool ExtraUsageAlert75 { get; set; } = true;
        public bool ExtraUsageAlert90 { get; set; } = true;
        public bool AdaptivePolling { get; set; } = true;
        public bool HasCompletedOnboarding { get; set; }
        public string? DismissedVersion { get; set; }
        public string? LastExtraUsagePeriodKey { get; set; }
        public bool ExtraUsage50Fired { get; set; }
        public bool ExtraUsage75Fired { get; set; }
        public bool ExtraUsage90Fired { get; set; }
        public bool ExtraUsageEnteredFired { get; set; }
        public List<string> DismissedPatternFindings { get; set; } = new();
        public string? DismissedTierRecommendation { get; set; }
        public bool PromoClockEnabled { get; set; }
        public string? PromoClockApiKey { get; set; }
        public string? PromoClockTeamId { get; set; }
    }
}
