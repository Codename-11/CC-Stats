using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ReactiveUI;

namespace CCStats.Desktop.ViewModels;

/// <summary>
/// Converts a boolean IsActive flag to a green (active) or gray (inactive) brush.
/// </summary>
public sealed class AccountActiveColorConverter : IValueConverter
{
    public static readonly AccountActiveColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isActive = value is true;
        return new SolidColorBrush(isActive ? Color.Parse("#66B866") : Color.Parse("#4A5568"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class AccountItemViewModel : ViewModelBase
{
    private string _displayName = "";

    public string AccountId { get; init; } = "";

    /// <summary>Fires when the display name is changed by the user.</summary>
    public event EventHandler<string>? NameChanged;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _displayName, value) is not null)
            {
                NameChanged?.Invoke(this, value);
            }
        }
    }

    public string TierLabel { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class SettingsViewModel : ViewModelBase
{
    private int _warningThreshold = 20;
    private int _criticalThreshold = 5;
    private bool _apiStatusAlerts = true;
    private bool _extraUsageAlerts;
    private bool _extraUsageAlert50;
    private bool _extraUsageAlert75;
    private bool _extraUsageAlert90;
    private string _selectedPollInterval = "30s";
    private string _selectedDataRetention = "90d";
    private string _fiveHourCreditLimit = string.Empty;
    private string _sevenDayCreditLimit = string.Empty;
    private string _selectedBillingCycleDay = "Not set";
    private bool _adaptivePolling = true;
    private bool _launchAtLogin;
    private string _monthlyPrice = string.Empty;
    private string _databaseSize = "Not available"; // TODO: Wire DatabaseManager.GetDatabaseSizeAsync() when available
    private bool _showClearDatabase;
    private bool _clearDatabaseConfirmPending;
    private bool _promoClockEnabled;
    private string _promoClockApiKey = "";
    private string _promoClockTeamId = "";

    // --- Version ---
    public string VersionText
    {
        get
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly()
                   ?? System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            return ver is not null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v0.1.0";
        }
    }

    public SettingsViewModel()
    {
        ClearDatabaseCommand = ReactiveCommand.Create(OnClearDatabase);
        ExportDatabaseCommand = ReactiveCommand.Create(OnExportDatabase);
        PruneDatabaseCommand = ReactiveCommand.Create(OnPruneDatabase);
        SwitchAccountCommand = ReactiveCommand.Create<string>(OnSwitchAccount);
        RemoveAccountCommand = ReactiveCommand.Create<string>(OnRemoveAccount);
        ResetToDefaultsCommand = ReactiveCommand.Create(OnResetToDefaults);
        OpenPromoClockCommand = ReactiveCommand.Create(() =>
        {
            Process.Start(new ProcessStartInfo("https://promoclock.co/en") { UseShellExecute = true });
        });
    }

    // --- Multi-account ---

    public event EventHandler<string>? AccountSwitchRequested;
    public event EventHandler<string>? AccountRemoveRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? PruneRequested;

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

    // Alert thresholds
    public int WarningThreshold
    {
        get => _warningThreshold;
        set => this.RaiseAndSetIfChanged(ref _warningThreshold, Math.Clamp(value, 6, 50));
    }

    public int CriticalThreshold
    {
        get => _criticalThreshold;
        set => this.RaiseAndSetIfChanged(ref _criticalThreshold, Math.Clamp(value, 1, 49));
    }

    // API status alerts
    public bool ApiStatusAlerts
    {
        get => _apiStatusAlerts;
        set => this.RaiseAndSetIfChanged(ref _apiStatusAlerts, value);
    }

    // Extra usage alerts
    public bool ExtraUsageAlerts
    {
        get => _extraUsageAlerts;
        set => this.RaiseAndSetIfChanged(ref _extraUsageAlerts, value);
    }

    public bool ExtraUsageAlert50
    {
        get => _extraUsageAlert50;
        set => this.RaiseAndSetIfChanged(ref _extraUsageAlert50, value);
    }

    public bool ExtraUsageAlert75
    {
        get => _extraUsageAlert75;
        set => this.RaiseAndSetIfChanged(ref _extraUsageAlert75, value);
    }

    public bool ExtraUsageAlert90
    {
        get => _extraUsageAlert90;
        set => this.RaiseAndSetIfChanged(ref _extraUsageAlert90, value);
    }

    // Adaptive polling
    public bool AdaptivePolling
    {
        get => _adaptivePolling;
        set => this.RaiseAndSetIfChanged(ref _adaptivePolling, value);
    }

    // Poll interval
    public List<string> PollIntervalOptions { get; } =
        ["10s", "15s", "30s", "1m", "2m", "5m", "10m", "15m", "30m"];

    public string SelectedPollInterval
    {
        get => _selectedPollInterval;
        set => this.RaiseAndSetIfChanged(ref _selectedPollInterval, value);
    }

    // Data retention
    public List<string> DataRetentionOptions { get; } =
        ["30d", "90d", "180d", "1y", "2y", "5y"];

    public string SelectedDataRetention
    {
        get => _selectedDataRetention;
        set => this.RaiseAndSetIfChanged(ref _selectedDataRetention, value);
    }

    // Custom credit limits
    public string FiveHourCreditLimit
    {
        get => _fiveHourCreditLimit;
        set => this.RaiseAndSetIfChanged(ref _fiveHourCreditLimit, value);
    }

    public string SevenDayCreditLimit
    {
        get => _sevenDayCreditLimit;
        set => this.RaiseAndSetIfChanged(ref _sevenDayCreditLimit, value);
    }

    // Monthly price
    public string MonthlyPrice
    {
        get => _monthlyPrice;
        set => this.RaiseAndSetIfChanged(ref _monthlyPrice, value);
    }

    // Billing cycle day
    public List<string> BillingCycleDayOptions { get; } = CreateBillingCycleDayOptions();

    public string SelectedBillingCycleDay
    {
        get => _selectedBillingCycleDay;
        set => this.RaiseAndSetIfChanged(ref _selectedBillingCycleDay, value);
    }

    // Launch at login
    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set => this.RaiseAndSetIfChanged(ref _launchAtLogin, value);
    }

    // PromoClock Integration
    public bool PromoClockEnabled
    {
        get => _promoClockEnabled;
        set => this.RaiseAndSetIfChanged(ref _promoClockEnabled, value);
    }

    public string PromoClockApiKey
    {
        get => _promoClockApiKey;
        set => this.RaiseAndSetIfChanged(ref _promoClockApiKey, value);
    }

    public string PromoClockTeamId
    {
        get => _promoClockTeamId;
        set => this.RaiseAndSetIfChanged(ref _promoClockTeamId, value);
    }

    public ReactiveCommand<Unit, Unit> OpenPromoClockCommand { get; }

    // Database
    public string DatabaseSize
    {
        get => _databaseSize;
        set => this.RaiseAndSetIfChanged(ref _databaseSize, value);
    }

    public bool ShowClearDatabase
    {
        get => _showClearDatabase;
        set => this.RaiseAndSetIfChanged(ref _showClearDatabase, value);
    }

    public bool ClearDatabaseConfirmPending
    {
        get => _clearDatabaseConfirmPending;
        set
        {
            this.RaiseAndSetIfChanged(ref _clearDatabaseConfirmPending, value);
            this.RaisePropertyChanged(nameof(ClearButtonText));
        }
    }

    public string ClearButtonText => ClearDatabaseConfirmPending ? "Confirm?" : "Clear";

    public ReactiveCommand<Unit, Unit> ClearDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> PruneDatabaseCommand { get; }
    public ReactiveCommand<string, Unit> SwitchAccountCommand { get; }
    public ReactiveCommand<string, Unit> RemoveAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }

    private void OnClearDatabase()
    {
        if (!ClearDatabaseConfirmPending)
        {
            ClearDatabaseConfirmPending = true;
            DatabaseSize = "Click again to confirm";
            return;
        }
        // Actually clear
        ClearDatabaseConfirmPending = false;
        DatabaseSize = "Cleared";
        ShowClearDatabase = false;
    }

    private void OnExportDatabase()
    {
        ExportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPruneDatabase()
    {
        PruneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSwitchAccount(string accountId)
    {
        AccountSwitchRequested?.Invoke(this, accountId);
    }

    private void OnRemoveAccount(string accountId)
    {
        AccountRemoveRequested?.Invoke(this, accountId);
    }

    private void OnResetToDefaults()
    {
        WarningThreshold = 20;
        CriticalThreshold = 5;
        ApiStatusAlerts = true;
        ExtraUsageAlerts = false;
        ExtraUsageAlert50 = true;
        ExtraUsageAlert75 = true;
        ExtraUsageAlert90 = true;
        AdaptivePolling = true;
        SelectedPollInterval = "1m";
        SelectedDataRetention = "1y";
        FiveHourCreditLimit = "";
        SevenDayCreditLimit = "";
        MonthlyPrice = "";
        SelectedBillingCycleDay = "Not set";
        LaunchAtLogin = false;
    }

    private static List<string> CreateBillingCycleDayOptions()
    {
        var options = new List<string> { "Not set" };
        for (var i = 1; i <= 28; i++)
        {
            options.Add(i.ToString());
        }
        return options;
    }
}
