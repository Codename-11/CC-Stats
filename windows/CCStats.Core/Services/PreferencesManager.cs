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
        get => _data.WarningThreshold;
        set { _data.WarningThreshold = Math.Clamp(value, 6, 50); Validate(); Save(); }
    }

    public int CriticalThreshold
    {
        get => _data.CriticalThreshold;
        set { _data.CriticalThreshold = Math.Clamp(value, 1, 49); Validate(); Save(); }
    }

    public int PollIntervalSeconds
    {
        get => _data.PollIntervalSeconds;
        set
        {
            _data.PollIntervalSeconds = ValidPollIntervals.Contains(value) ? value : 120;
            Save();
        }
    }

    public int DataRetentionDays
    {
        get => _data.DataRetentionDays;
        set
        {
            _data.DataRetentionDays = ValidRetentionDays.Contains(value) ? value : 365;
            Save();
        }
    }

    public int? CustomFiveHourCredits
    {
        get => _data.CustomFiveHourCredits;
        set { _data.CustomFiveHourCredits = value; Save(); }
    }

    public int? CustomSevenDayCredits
    {
        get => _data.CustomSevenDayCredits;
        set { _data.CustomSevenDayCredits = value; Save(); }
    }

    public double? CustomMonthlyPrice
    {
        get => _data.CustomMonthlyPrice;
        set { _data.CustomMonthlyPrice = value; Save(); }
    }

    public int BillingCycleDay
    {
        get => _data.BillingCycleDay;
        set { _data.BillingCycleDay = Math.Clamp(value, 0, 28); Save(); }
    }

    public bool LaunchAtLogin
    {
        get => _data.LaunchAtLogin;
        set { _data.LaunchAtLogin = value; Save(); }
    }

    public bool ApiStatusAlerts
    {
        get => _data.ApiStatusAlerts;
        set { _data.ApiStatusAlerts = value; Save(); }
    }

    public bool ExtraUsageAlerts
    {
        get => _data.ExtraUsageAlerts;
        set { _data.ExtraUsageAlerts = value; Save(); }
    }

    public bool ExtraUsageAlert50
    {
        get => _data.ExtraUsageAlert50;
        set { _data.ExtraUsageAlert50 = value; Save(); }
    }

    public bool ExtraUsageAlert75
    {
        get => _data.ExtraUsageAlert75;
        set { _data.ExtraUsageAlert75 = value; Save(); }
    }

    public bool ExtraUsageAlert90
    {
        get => _data.ExtraUsageAlert90;
        set { _data.ExtraUsageAlert90 = value; Save(); }
    }

    public bool AdaptivePolling
    {
        get => _data.AdaptivePolling;
        set { _data.AdaptivePolling = value; Save(); }
    }

    public bool HasCompletedOnboarding
    {
        get => _data.HasCompletedOnboarding;
        set { _data.HasCompletedOnboarding = value; Save(); }
    }

    public string? DismissedVersion
    {
        get => _data.DismissedVersion;
        set { _data.DismissedVersion = value; Save(); }
    }

    public string? LastExtraUsagePeriodKey
    {
        get => _data.LastExtraUsagePeriodKey;
        set { _data.LastExtraUsagePeriodKey = value; }
    }

    public bool ExtraUsage50Fired
    {
        get => _data.ExtraUsage50Fired;
        set { _data.ExtraUsage50Fired = value; }
    }

    public bool ExtraUsage75Fired
    {
        get => _data.ExtraUsage75Fired;
        set { _data.ExtraUsage75Fired = value; }
    }

    public bool ExtraUsage90Fired
    {
        get => _data.ExtraUsage90Fired;
        set { _data.ExtraUsage90Fired = value; }
    }

    public bool ExtraUsageEnteredFired
    {
        get => _data.ExtraUsageEnteredFired;
        set { _data.ExtraUsageEnteredFired = value; }
    }

    public List<string> DismissedPatternFindings
    {
        get => _data.DismissedPatternFindings;
        set { _data.DismissedPatternFindings = value; }
    }

    public string? DismissedTierRecommendation
    {
        get => _data.DismissedTierRecommendation;
        set { _data.DismissedTierRecommendation = value; }
    }

    public bool PromoClockEnabled
    {
        get => _data.PromoClockEnabled;
        set { _data.PromoClockEnabled = value; Save(); }
    }

    public string? PromoClockApiKey
    {
        get => _data.PromoClockApiKey;
        set { _data.PromoClockApiKey = value; Save(); }
    }

    public string? PromoClockTeamId
    {
        get => _data.PromoClockTeamId;
        set { _data.PromoClockTeamId = value; Save(); }
    }

    public void Load()
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
            System.Diagnostics.Debug.WriteLine($"[PreferencesManager] Failed to load preferences: {ex.Message}");
            _data = new PreferencesData();
        }
    }

    public void Save()
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
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PreferencesManager] Failed to save preferences: {ex.Message}");
        }
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
