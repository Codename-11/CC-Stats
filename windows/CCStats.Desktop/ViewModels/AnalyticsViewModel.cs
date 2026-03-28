using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Media;
using CCStats.Core.Models;
using CCStats.Core.State;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using ReactiveUI;
using SkiaSharp;

namespace CCStats.Desktop.ViewModels;

public sealed class AnalyticsViewModel : ViewModelBase
{
    private string _selectedTimeRange = "24h";
    private bool _showFiveHour = true;
    private bool _showSevenDay = true;
    private IReadOnlyList<double> _sparklineData = Array.Empty<double>();
    private AppState? _appState;

    public AnalyticsViewModel()
    {
        SelectTimeRangeCommand = ReactiveCommand.Create<string>(OnSelectTimeRange);
    }

    /// <summary>
    /// Updates data and refreshes all chart/insight state.
    /// Called from MainWindowViewModel when poll data changes.
    /// </summary>
    public void UpdateData(IReadOnlyList<double> sparklineData, AppState state)
    {
        _sparklineData = sparklineData;
        _appState = state;
        RefreshChart();
        RefreshBreakdown();
        RefreshInsights();
        RefreshPatterns();
        RefreshCycleChart();
        RefreshTierRecommendation();
        RefreshChartSections();
        this.RaisePropertyChanged(nameof(HasData));
        this.RaisePropertyChanged(nameof(ShowNoInsightsFallback));
    }

    // Time range selection
    public List<string> TimeRangeOptions { get; } = ["24h", "7d", "30d", "All"];

    public string SelectedTimeRange
    {
        get => _selectedTimeRange;
        set => this.RaiseAndSetIfChanged(ref _selectedTimeRange, value);
    }

    public bool Is24hSelected => SelectedTimeRange == "24h";
    public bool Is7dSelected => SelectedTimeRange == "7d";
    public bool Is30dSelected => SelectedTimeRange == "30d";
    public bool IsAllSelected => SelectedTimeRange == "All";

    // Tab button styling helpers
    private static readonly IBrush ActiveBg = new SolidColorBrush(Color.Parse("#263041"));
    private static readonly IBrush InactiveBg = new SolidColorBrush(Color.Parse("#1A1E26"));
    private static readonly IBrush ActiveFg = new SolidColorBrush(Color.Parse("#F5F7FB"));
    private static readonly IBrush InactiveFg = new SolidColorBrush(Color.Parse("#6B7A8D"));

    public IBrush Tab24hBg => Is24hSelected ? ActiveBg : InactiveBg;
    public IBrush Tab7dBg => Is7dSelected ? ActiveBg : InactiveBg;
    public IBrush Tab30dBg => Is30dSelected ? ActiveBg : InactiveBg;
    public IBrush TabAllBg => IsAllSelected ? ActiveBg : InactiveBg;
    public IBrush Tab24hFg => Is24hSelected ? ActiveFg : InactiveFg;
    public IBrush Tab7dFg => Is7dSelected ? ActiveFg : InactiveFg;
    public IBrush Tab30dFg => Is30dSelected ? ActiveFg : InactiveFg;
    public IBrush TabAllFg => IsAllSelected ? ActiveFg : InactiveFg;

    // Series toggles
    public bool ShowFiveHour
    {
        get => _showFiveHour;
        set
        {
            this.RaiseAndSetIfChanged(ref _showFiveHour, value);
            RefreshChart();
        }
    }

    public bool ShowSevenDay
    {
        get => _showSevenDay;
        set
        {
            this.RaiseAndSetIfChanged(ref _showSevenDay, value);
            RefreshChart();
        }
    }

    // Data
    public IReadOnlyList<double> SparklineData
    {
        get => _sparklineData;
        set => this.RaiseAndSetIfChanged(ref _sparklineData, value);
    }

    public bool HasData => _sparklineData.Count >= 2;

    // --- Gap/Outage Chart Sections ---

    public RectangularSection[] ChartSections { get; private set; } = Array.Empty<RectangularSection>();

    private void RefreshChartSections()
    {
        var sections = new List<RectangularSection>();

        if (_sparklineData.Count > 2)
        {
            // Detect zero-islands as gaps (gray bands)
            for (int i = 1; i < _sparklineData.Count - 1; i++)
            {
                if (_sparklineData[i] == 0 && _sparklineData[i - 1] > 0)
                {
                    int gapStart = i;
                    while (i < _sparklineData.Count && _sparklineData[i] == 0) i++;
                    int gapEnd = i;

                    sections.Add(new RectangularSection
                    {
                        Xi = gapStart,
                        Xj = gapEnd,
                        Fill = new SolidColorPaint(SKColor.Parse("#14808080")), // 8% gray
                    });
                }
            }

            // Detect reset boundaries: sharp drops in utilization (>30% drop between adjacent points)
            for (int i = 1; i < _sparklineData.Count; i++)
            {
                if (_sparklineData[i - 1] - _sparklineData[i] > 30)
                {
                    // Add a thin vertical orange line at the reset point
                    sections.Add(new RectangularSection
                    {
                        Xi = i - 0.5,
                        Xj = i + 0.5,
                        Fill = new SolidColorPaint(SKColor.Parse("#40F39A4B")), // orange at 25%
                    });
                }
            }
        }

        ChartSections = sections.ToArray();
        this.RaisePropertyChanged(nameof(ChartSections));
    }

    // --- LiveCharts2 Series (stored, not computed) ---

    private ISeries[] _analyticsSeries = Array.Empty<ISeries>();
    public ISeries[] AnalyticsSeries
    {
        get => _analyticsSeries;
        private set => this.RaiseAndSetIfChanged(ref _analyticsSeries, value);
    }

    private Axis[] _analyticsXAxesStored = Array.Empty<Axis>();

    /// <summary>
    /// Rebuilds AnalyticsSeries and AnalyticsXAxes from current data/settings.
    /// LiveCharts2 needs stable series objects that are replaced as a whole array,
    /// not computed fresh on every property read.
    /// </summary>
    private void RefreshChart()
    {
        var series = new List<ISeries>();

        if (SelectedTimeRange == "24h")
        {
            if (_showFiveHour && _sparklineData.Count > 0)
            {
                series.Add(new StepLineSeries<double>
                {
                    Values = _sparklineData.ToArray(),
                    Name = "5h",
                    Fill = new SolidColorPaint(SKColor.Parse("#3366B866")),
                    Stroke = new SolidColorPaint(SKColor.Parse("#66B866")) { StrokeThickness = 2 },
                    GeometrySize = 0,
                    GeometryFill = null,
                    GeometryStroke = null,
                });
            }
            if (_showSevenDay && _appState?.SevenDay is not null)
            {
                var sevenDayUtil = _appState.SevenDay.Utilization;
                series.Add(new LineSeries<double>
                {
                    Values = Enumerable.Repeat(sevenDayUtil, Math.Max(_sparklineData.Count, 2)).ToArray(),
                    Name = "7d",
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColor.Parse("#4A90D9"))
                    {
                        StrokeThickness = 1.5f,
                        PathEffect = new DashEffect(new float[] { 6, 4 }),
                    },
                    GeometrySize = 0,
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                });
            }
        }
        else
        {
            if (_showFiveHour && _sparklineData.Count > 0)
            {
                var bars = AggregateToBarData(_sparklineData, SelectedTimeRange);
                series.Add(new ColumnSeries<double>
                {
                    Values = bars,
                    Name = "5h avg",
                    Fill = new SolidColorPaint(SKColor.Parse("#66B866")),
                    MaxBarWidth = 12,
                    Padding = 2,
                });
            }
            if (_showSevenDay && _appState?.SevenDay is not null)
            {
                var barCount = _showFiveHour && _sparklineData.Count > 0
                    ? AggregateToBarData(_sparklineData, SelectedTimeRange).Length : 2;
                series.Add(new LineSeries<double>
                {
                    Values = Enumerable.Repeat(_appState.SevenDay.Utilization, Math.Max(barCount, 2)).ToArray(),
                    Name = "7d",
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColor.Parse("#4A90D9"))
                    {
                        StrokeThickness = 1.5f,
                        PathEffect = new DashEffect(new float[] { 6, 4 }),
                    },
                    GeometrySize = 0,
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                });
            }
        }

        // Add projection line if slope is rising (only for 24h view)
        if (SelectedTimeRange == "24h" && _showFiveHour && _sparklineData.Count > 0)
        {
            var currentUtil = _appState?.FiveHour?.Utilization ?? 0;
            var slopeRate = _appState?.FiveHourSlopeRate ?? 0;

            if (currentUtil > 0 && slopeRate > 0.3) // Rising or Steep
            {
                var remainingPercent = 100.0 - currentUtil;
                var pointsToExhaustion = (int)(remainingPercent / slopeRate);
                var projectionPoints = Math.Min(pointsToExhaustion, _sparklineData.Count / 2);

                if (projectionPoints > 1)
                {
                    var projectionValues = new List<double>();
                    // Pad with NaN up to current position
                    for (int i = 0; i < _sparklineData.Count; i++)
                        projectionValues.Add(double.NaN);
                    // Then project forward
                    for (int i = 0; i < projectionPoints; i++)
                    {
                        var projected = currentUtil + (slopeRate * (i + 1));
                        projectionValues.Add(Math.Min(100, projected));
                    }

                    series.Add(new LineSeries<double>
                    {
                        Values = projectionValues.ToArray(),
                        Name = "Projected",
                        Fill = null,
                        Stroke = new SolidColorPaint(SKColor.Parse("#F0645B"))
                        {
                            StrokeThickness = 1.5f,
                            PathEffect = new DashEffect(new float[] { 4, 4 }),
                        },
                        GeometrySize = 0,
                        GeometryFill = null,
                        GeometryStroke = null,
                        LineSmoothness = 0,
                    });
                }
            }
        }

        AnalyticsSeries = series.ToArray();

        // Update X axes based on time range and data count
        var labelCount = SelectedTimeRange == "24h" ? _sparklineData.Count
            : (_sparklineData.Count > 0 ? AggregateToBarData(_sparklineData, SelectedTimeRange).Length : 0);
        var xLabels = GenerateTimeLabels(labelCount, SelectedTimeRange);
        AnalyticsXAxes = new Axis[]
        {
            new Axis
            {
                Labels = xLabels,
                TextSize = 9,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#6B7A8D")),
                ShowSeparatorLines = false,
                LabelsRotation = 0,
            }
        };
    }

    private static double[] AggregateToBarData(IReadOnlyList<double> data, string timeRange)
    {
        var bucketCount = timeRange switch
        {
            "7d" => 7,
            "30d" => 15,
            _ => 12,
        };

        if (data.Count < bucketCount) return data.ToArray();

        var bucketSize = data.Count / bucketCount;
        var result = new double[bucketCount];
        for (int i = 0; i < bucketCount; i++)
        {
            var start = i * bucketSize;
            var end = (i == bucketCount - 1) ? data.Count : start + bucketSize;
            result[i] = data.Skip(start).Take(end - start).Average();
        }
        return result;
    }

    public Axis[] AnalyticsXAxes
    {
        get => _analyticsXAxesStored;
        private set => this.RaiseAndSetIfChanged(ref _analyticsXAxesStored, value);
    }

    public Axis[] AnalyticsYAxes => new Axis[]
    {
        new Axis
        {
            Name = "Utilization %",
            NamePaint = new SolidColorPaint(SKColor.Parse("#8FA0B8")),
            NameTextSize = 11,
            ShowSeparatorLines = true,
            SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#202A38")) { StrokeThickness = 1 },
            LabelsPaint = new SolidColorPaint(SKColor.Parse("#6B7A8D")),
            TextSize = 10,
            MinLimit = 0,
            MaxLimit = 100,
        }
    };

    private static string[]? GenerateTimeLabels(int dataPointCount, string timeRange)
    {
        if (dataPointCount < 2) return null;

        var now = DateTime.Now;
        var labels = new string[dataPointCount];

        var totalHours = timeRange switch
        {
            "24h" => 24.0,
            "7d" => 168.0,
            "30d" => 720.0,
            _ => 720.0,
        };

        var intervalHours = totalHours / dataPointCount;

        for (int i = 0; i < dataPointCount; i++)
        {
            var pointTime = now.AddHours(-totalHours + (i * intervalHours));

            if (totalHours <= 24)
            {
                labels[i] = (i % 4 == 0) ? pointTime.ToString("h tt") : "";
            }
            else if (totalHours <= 168)
            {
                labels[i] = (i % Math.Max(1, dataPointCount / 7) == 0) ? pointTime.ToString("ddd") : "";
            }
            else
            {
                labels[i] = (i % Math.Max(1, dataPointCount / 6) == 0) ? pointTime.ToString("MMM d") : "";
            }
        }

        return labels;
    }

    // ========== Feature 1: Headroom Breakdown Bar ==========

    private bool _showBreakdown;
    public bool ShowBreakdown
    {
        get => _showBreakdown;
        private set => this.RaiseAndSetIfChanged(ref _showBreakdown, value);
    }

    private string _breakdownUsedText = "";
    public string BreakdownUsedText
    {
        get => _breakdownUsedText;
        private set => this.RaiseAndSetIfChanged(ref _breakdownUsedText, value);
    }

    private string _breakdownTotalText = "";
    public string BreakdownTotalText
    {
        get => _breakdownTotalText;
        private set => this.RaiseAndSetIfChanged(ref _breakdownTotalText, value);
    }

    private double _breakdownUsedPercent;
    public double BreakdownUsedPercent
    {
        get => _breakdownUsedPercent;
        private set => this.RaiseAndSetIfChanged(ref _breakdownUsedPercent, value);
    }

    private string _breakdownLegend = "";
    public string BreakdownLegend
    {
        get => _breakdownLegend;
        private set => this.RaiseAndSetIfChanged(ref _breakdownLegend, value);
    }

    private IBrush _breakdownBarBrush = new SolidColorBrush(Color.Parse("#66B866"));
    public IBrush BreakdownBarBrush
    {
        get => _breakdownBarBrush;
        private set => this.RaiseAndSetIfChanged(ref _breakdownBarBrush, value);
    }

    private void RefreshBreakdown()
    {
        var utilization5h = _appState?.FiveHour?.Utilization;
        var utilization7d = _appState?.SevenDay?.Utilization;

        if (utilization5h is not null)
        {
            // Show utilization-based breakdown (not dollar — dollar requires
            // billing cycle data which we don't have reliably)
            ShowBreakdown = true;

            var headroom5h = 100.0 - utilization5h.Value;
            BreakdownUsedText = $"{utilization5h.Value:F0}% of 5h used";
            if (utilization7d is not null)
                BreakdownTotalText = $"· {utilization7d.Value:F0}% of 7d used";
            else
                BreakdownTotalText = "";
            BreakdownUsedPercent = utilization5h.Value;
            BreakdownLegend = $"{headroom5h:F0}% headroom remaining in current 5h window";

            // Pick bar color based on utilization
            var headroomState = _appState?.FiveHour?.HeadroomState ?? HeadroomState.Normal;
            var color = Controls.HeadroomColors.ForHeadroomState(headroomState);
            BreakdownBarBrush = new SolidColorBrush(color);
        }
        else
        {
            ShowBreakdown = false;
        }
    }

    // ========== Insights ==========

    public string PrimaryInsight { get; private set; } = "";
    public string SecondaryInsight { get; private set; } = "";
    public bool HasInsights => !string.IsNullOrEmpty(PrimaryInsight);

    /// <summary>
    /// Shows the "no insights" fallback only when there is truly nothing to display:
    /// no breakdown, no insights, no patterns, no cycle chart, and no tier recommendation.
    /// </summary>
    public bool ShowNoInsightsFallback =>
        !ShowBreakdown && !HasInsights && PatternCards.Count == 0
        && !ShowCycleComparison && !ShowTierRecommendation;

    private void RefreshInsights()
    {
        var util = _appState?.FiveHour?.Utilization ?? 0;
        var sevenDayUtil = _appState?.SevenDay?.Utilization;
        var slopeRate = _appState?.FiveHourSlopeRate ?? 0;

        // Primary insight: current state with actionable context
        if (util > 90)
        {
            PrimaryInsight = $"High pressure: {util:F0}% of 5h window consumed";
        }
        else if (util > 60)
        {
            var remaining = 100 - util;
            PrimaryInsight = $"{remaining:F0}% headroom left in 5h window";
        }
        else if (util > 0)
        {
            PrimaryInsight = $"Healthy: {100 - util:F0}% headroom available";
        }
        else
        {
            PrimaryInsight = "Full headroom \u2014 no usage in current window";
        }

        // Secondary insight: comparison or trend
        if (sevenDayUtil.HasValue && _sparklineData.Count > 1)
        {
            var avg = _sparklineData.Average();
            var trend = _sparklineData.Last() > avg + 5 ? "trending up" :
                        _sparklineData.Last() < avg - 5 ? "trending down" : "stable";
            SecondaryInsight = $"7d utilization: {sevenDayUtil:F0}% \u00B7 24h trend: {trend}";
        }
        else
        {
            SecondaryInsight = "";
        }

        this.RaisePropertyChanged(nameof(PrimaryInsight));
        this.RaisePropertyChanged(nameof(SecondaryInsight));
        this.RaisePropertyChanged(nameof(HasInsights));
    }

    // ========== Feature 2: Pattern Detection Cards ==========

    public ObservableCollection<PatternCardViewModel> PatternCards { get; } = new();

    private void RefreshPatterns()
    {
        PatternCards.Clear();

        var fiveHourUtil = _appState?.FiveHour?.Utilization;
        var sevenDayUtil = _appState?.SevenDay?.Utilization;

        if (fiveHourUtil is null) return;

        // High usage pattern
        if (fiveHourUtil > 80)
        {
            AddPatternCard(
                "High usage pattern",
                "Consider upgrading to a higher tier for more headroom.");
        }

        // Low usage pattern
        if (fiveHourUtil < 20)
        {
            AddPatternCard(
                "Low usage",
                "You may be overpaying for your current tier.");
        }

        // Sustained usage (7d much higher than 5h)
        if (sevenDayUtil is not null && sevenDayUtil > fiveHourUtil + 15)
        {
            AddPatternCard(
                "Sustained usage",
                "Your usage is spread evenly across the week.");
        }

        // Usage spike detection (steep increase in sparkline)
        if (_sparklineData.Count >= 4)
        {
            var recentHalf = _sparklineData.Skip(_sparklineData.Count / 2).Average();
            var earlierHalf = _sparklineData.Take(_sparklineData.Count / 2).Average();
            if (recentHalf > earlierHalf * 1.5 && recentHalf - earlierHalf > 10)
            {
                AddPatternCard(
                    "Usage spike detected",
                    "Burn rate has increased significantly in the recent period.");
            }
        }

        // Usage decay: sparkline is declining
        if (_sparklineData.Count >= 6)
        {
            var firstHalf = _sparklineData.Take(_sparklineData.Count / 2).Average();
            var secondHalf = _sparklineData.Skip(_sparklineData.Count / 2).Average();
            if (firstHalf - secondHalf > 15)
            {
                AddPatternCard(
                    "Usage declining",
                    $"Your usage has dropped from ~{firstHalf:F0}% to ~{secondHalf:F0}%. Your current tier may be more than you need.");
            }
        }

        // Extra usage overflow
        if (_appState is { ExtraUsageEnabled: true, ExtraUsageUtilization: > 0.5 })
        {
            AddPatternCard(
                "Extra usage active",
                $"You're using {_appState.ExtraUsageUtilization * 100:F0}% of your extra usage budget. Consider upgrading your tier to avoid overage charges.");
        }
    }

    private void AddPatternCard(string title, string summary)
    {
        var card = new PatternCardViewModel
        {
            Title = title,
            Summary = summary,
        };
        card.DismissCommand = ReactiveCommand.Create(() => { PatternCards.Remove(card); });
        PatternCards.Add(card);
    }

    // ========== Feature 3: Cycle-over-Cycle Bar Chart ==========

    private List<double> _cycleData = new();
    public List<double> CycleData => _cycleData;

    private bool _showCycleComparison;
    public bool ShowCycleComparison
    {
        get => _showCycleComparison;
        private set => this.RaiseAndSetIfChanged(ref _showCycleComparison, value);
    }

    private ISeries[] _cycleSeries = Array.Empty<ISeries>();
    public ISeries[] CycleSeries
    {
        get => _cycleSeries;
        private set => this.RaiseAndSetIfChanged(ref _cycleSeries, value);
    }

    private Axis[] _cycleXAxes = Array.Empty<Axis>();
    public Axis[] CycleXAxes
    {
        get => _cycleXAxes;
        private set => this.RaiseAndSetIfChanged(ref _cycleXAxes, value);
    }

    private Axis[] _cycleYAxes = Array.Empty<Axis>();
    public Axis[] CycleYAxes
    {
        get => _cycleYAxes;
        private set => this.RaiseAndSetIfChanged(ref _cycleYAxes, value);
    }

    private void RefreshCycleChart()
    {
        // Only show for 30d or All time ranges
        if (SelectedTimeRange is not ("30d" or "All"))
        {
            ShowCycleComparison = false;
            return;
        }

        // If we have sparkline data, create a simple breakdown into thirds
        if (_sparklineData.Count < 3)
        {
            ShowCycleComparison = false;
            return;
        }

        var third = _sparklineData.Count / 3;
        var period1Avg = _sparklineData.Take(third).Average();
        var period2Avg = _sparklineData.Skip(third).Take(third).Average();
        var period3Avg = _sparklineData.Skip(third * 2).Average();

        var labels = new[] { "Earlier", "Recent", "Now" };
        var values = new[] { period1Avg, period2Avg, period3Avg };
        _cycleData = new List<double>(values);

        CycleSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Values = values,
                Name = "Utilization",
                Fill = new SolidColorPaint(SKColor.Parse("#66B866")),
                MaxBarWidth = 24,
                Padding = 8,
            }
        };

        CycleXAxes = new Axis[]
        {
            new Axis
            {
                Labels = labels,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#6B7A8D")),
                ShowSeparatorLines = false,
            }
        };

        CycleYAxes = new Axis[]
        {
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                ShowSeparatorLines = false,
                IsVisible = false,
            }
        };

        ShowCycleComparison = true;
        this.RaisePropertyChanged(nameof(ShowCycleComparison));
        this.RaisePropertyChanged(nameof(CycleSeries));
        this.RaisePropertyChanged(nameof(CycleXAxes));
        this.RaisePropertyChanged(nameof(CycleYAxes));
    }

    // ========== Feature 4: Tier Recommendation Card ==========

    private bool _showTierRecommendation;
    public bool ShowTierRecommendation
    {
        get => _showTierRecommendation;
        private set => this.RaiseAndSetIfChanged(ref _showTierRecommendation, value);
    }

    private string _tierRecommendationTitle = "";
    public string TierRecommendationTitle
    {
        get => _tierRecommendationTitle;
        private set => this.RaiseAndSetIfChanged(ref _tierRecommendationTitle, value);
    }

    private string _tierRecommendationSummary = "";
    public string TierRecommendationSummary
    {
        get => _tierRecommendationSummary;
        private set => this.RaiseAndSetIfChanged(ref _tierRecommendationSummary, value);
    }

    private void RefreshTierRecommendation()
    {
        var shouldShow = _selectedTimeRange is "30d" or "All";
        var utilization = _appState?.FiveHour?.Utilization;

        if (!shouldShow || utilization is null)
        {
            ShowTierRecommendation = false;
            return;
        }

        ShowTierRecommendation = true;

        if (utilization > 85)
        {
            TierRecommendationTitle = "Consider upgrading";
            TierRecommendationSummary = "You're consistently using over 85% of your quota. A higher tier would give you more headroom.";
        }
        else if (utilization < 15)
        {
            TierRecommendationTitle = "Good fit or potential savings";
            TierRecommendationSummary = "Your usage is well within limits. You could consider a lower tier to save money.";
        }
        else
        {
            TierRecommendationTitle = "Your tier is a good fit";
            TierRecommendationSummary = "Your usage is balanced with adequate headroom.";
        }
    }

    // Commands
    public ReactiveCommand<string, Unit> SelectTimeRangeCommand { get; }

    private void OnSelectTimeRange(string range)
    {
        SelectedTimeRange = range;
        this.RaisePropertyChanged(nameof(Is24hSelected));
        this.RaisePropertyChanged(nameof(Is7dSelected));
        this.RaisePropertyChanged(nameof(Is30dSelected));
        this.RaisePropertyChanged(nameof(IsAllSelected));
        this.RaisePropertyChanged(nameof(Tab24hBg));
        this.RaisePropertyChanged(nameof(Tab7dBg));
        this.RaisePropertyChanged(nameof(Tab30dBg));
        this.RaisePropertyChanged(nameof(TabAllBg));
        this.RaisePropertyChanged(nameof(Tab24hFg));
        this.RaisePropertyChanged(nameof(Tab7dFg));
        this.RaisePropertyChanged(nameof(Tab30dFg));
        this.RaisePropertyChanged(nameof(TabAllFg));
        RefreshChart();
        RefreshInsights();
        RefreshCycleChart();
        RefreshTierRecommendation();
        RefreshChartSections();
        this.RaisePropertyChanged(nameof(ShowNoInsightsFallback));
    }
}

public sealed class PatternCardViewModel : ViewModelBase
{
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public ReactiveCommand<Unit, Unit>? DismissCommand { get; set; }
}
