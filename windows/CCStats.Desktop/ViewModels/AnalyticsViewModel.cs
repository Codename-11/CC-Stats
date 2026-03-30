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
    private HashSet<string> _dismissedPatterns = new();

    // Stable series objects — created once, values updated on refresh (prevents chart flash)
    private readonly StepLineSeries<double> _fiveHourStepSeries;
    private readonly LineSeries<double> _sevenDayLineSeries;
    private readonly ColumnSeries<double> _fiveHourBarSeries;
    private readonly LineSeries<double> _projectionSeries;

    public event EventHandler<string>? PatternDismissed;

    public AnalyticsViewModel()
    {
        SelectTimeRangeCommand = ReactiveCommand.Create<string>(OnSelectTimeRange);

        _fiveHourStepSeries = new StepLineSeries<double>
        {
            Name = "5h utilization",
            Fill = new SolidColorPaint(SKColor.Parse("#3366B866")),
            Stroke = new SolidColorPaint(SKColor.Parse("#66B866")) { StrokeThickness = 2 },
            GeometrySize = 0, GeometryFill = null, GeometryStroke = null,
            AnimationsSpeed = TimeSpan.FromMilliseconds(300),
        };
        _sevenDayLineSeries = new LineSeries<double>
        {
            Name = "7d weekly limit",
            Fill = null,
            Stroke = new SolidColorPaint(SKColor.Parse("#4A90D9"))
            {
                StrokeThickness = 1.5f,
                PathEffect = new DashEffect(new float[] { 6, 4 }),
            },
            GeometrySize = 0, GeometryFill = null, GeometryStroke = null,
            LineSmoothness = 1, // smooth the line to avoid zigzag in bar mode
            AnimationsSpeed = TimeSpan.FromMilliseconds(300),
        };
        _fiveHourBarSeries = new ColumnSeries<double>
        {
            Name = "5h avg",
            Fill = new SolidColorPaint(SKColor.Parse("#66B866")),
            MaxBarWidth = 12, Padding = 2,
            AnimationsSpeed = TimeSpan.FromMilliseconds(300),
        };
        _projectionSeries = new LineSeries<double>
        {
            Name = "Projected exhaustion",
            Fill = null,
            Stroke = new SolidColorPaint(SKColor.Parse("#F0645B"))
            {
                StrokeThickness = 1.5f,
                PathEffect = new DashEffect(new float[] { 4, 4 }),
            },
            GeometrySize = 0, GeometryFill = null, GeometryStroke = null,
            LineSmoothness = 0,
            AnimationsSpeed = TimeSpan.FromMilliseconds(300),
        };

        // Stable axis arrays — created once, labels updated in-place
        AnalyticsXAxes = new[] { _xAxis };
        AnalyticsYAxes = new[] { _yAxis };
        CycleSeries = new ISeries[] { _cycleBarSeries };
        CycleXAxes = new[] { _cycleXAxis };
        CycleYAxes = new[] { _cycleYAxis };
    }

    public void SetDismissedPatterns(IEnumerable<string> dismissed)
    {
        _dismissedPatterns = new HashSet<string>(dismissed);
    }

    /// <summary>
    /// Updates data and refreshes all chart/insight state.
    /// Called from MainWindowViewModel when poll data changes.
    /// </summary>
    private IReadOnlyList<CCStats.Core.Models.ResetEvent> _resetEvents = Array.Empty<CCStats.Core.Models.ResetEvent>();

    public void UpdateData(IReadOnlyList<double> sparklineData, AppState state,
        IReadOnlyList<CCStats.Core.Models.ResetEvent>? resetEvents = null)
    {
        _sparklineData = sparklineData;
        _appState = state;
        _resetEvents = resetEvents ?? Array.Empty<CCStats.Core.Models.ResetEvent>();
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

    // Reset colors by window type
    private static readonly SKColor ResetColor5h = SKColor.Parse("#60F39A4B");   // orange
    private static readonly SKColor ResetColor7d = SKColor.Parse("#604A90D9");   // blue
    private static readonly SKColor ResetColorOther = SKColor.Parse("#60E6C15A"); // yellow

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
                        Fill = new SolidColorPaint(SKColor.Parse("#14808080")),
                    });
                }
            }

            // Use real reset events from DB when available, color-coded by window type
            if (_resetEvents.Count > 0)
            {
                var totalHours = SelectedTimeRange switch
                {
                    "24h" => 24.0, "7d" => 168.0, "30d" => 720.0, _ => 720.0,
                };
                var now = DateTimeOffset.UtcNow;
                // Use aggregated bar count for non-24h modes so markers align with bars
                var dataPoints = SelectedTimeRange == "24h"
                    ? _sparklineData.Count
                    : (_sparklineData.Count > 0
                        ? AggregateToBarData(_sparklineData, SelectedTimeRange).Length
                        : _sparklineData.Count);

                foreach (var evt in _resetEvents)
                {
                    // Map reset timestamp to x-axis position
                    var hoursAgo = (now - evt.Timestamp).TotalHours;
                    if (hoursAgo < 0 || hoursAgo > totalHours) continue;
                    var xPos = dataPoints * (1.0 - hoursAgo / totalHours);

                    var color = evt.WindowType switch
                    {
                        "five_hour" => ResetColor5h,
                        "seven_day" => ResetColor7d,
                        _ => ResetColorOther,
                    };

                    sections.Add(new RectangularSection
                    {
                        Xi = xPos - 0.4,
                        Xj = xPos + 0.4,
                        Fill = new SolidColorPaint(color),
                    });
                }
            }
            else
            {
                // Fallback: detect from sparkline data (no DB events yet)
                for (int i = 1; i < _sparklineData.Count; i++)
                {
                    if (_sparklineData[i - 1] - _sparklineData[i] > 10)
                    {
                        sections.Add(new RectangularSection
                        {
                            Xi = i - 0.4,
                            Xj = i + 0.4,
                            Fill = new SolidColorPaint(ResetColor5h),
                        });
                    }
                }
            }
        }

        // In bar mode, add 7d as a horizontal reference line (not a LineSeries — avoids zigzag)
        if (SelectedTimeRange != "24h" && _showSevenDay && _appState?.SevenDay is not null)
        {
            var util = _appState.SevenDay.Utilization;
            sections.Add(new RectangularSection
            {
                Yi = util - 0.5,
                Yj = util + 0.5,
                Fill = new SolidColorPaint(SKColor.Parse("#4A90D9")),
            });
        }

        ChartSections = sections.ToArray();
        this.RaisePropertyChanged(nameof(ChartSections));
    }

    // --- LiveCharts2 Series (stored, not computed) ---

    private ISeries[] _analyticsSeries = Array.Empty<ISeries>();
    private string _lastSeriesKey = ""; // tracks structure to avoid unnecessary array replacement

    public ISeries[] AnalyticsSeries
    {
        get => _analyticsSeries;
        private set => this.RaiseAndSetIfChanged(ref _analyticsSeries, value);
    }

    // Stable axis objects — created once, labels updated in-place
    private readonly Axis _xAxis = new()
    {
        Labels = Array.Empty<string>(),
        TextSize = 9,
        LabelsPaint = new SolidColorPaint(SKColor.Parse("#6B7A8D")),
        ShowSeparatorLines = false,
        LabelsRotation = 0,
    };

    private readonly Axis _yAxis = new()
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
        ForceStepToMin = true,
        MinStep = 20, // 0, 20, 40, 60, 80, 100
    };

    /// <summary>
    /// Updates series values in-place. Only replaces the AnalyticsSeries array
    /// when the series structure changes (time range or toggle), not on data-only updates.
    /// </summary>
    private void RefreshChart()
    {
        var seriesList = new List<ISeries>();
        var hasProjection = false;

        if (SelectedTimeRange == "24h")
        {
            if (_showFiveHour && _sparklineData.Count > 0)
            {
                _fiveHourStepSeries.Values = _sparklineData.ToArray();
                seriesList.Add(_fiveHourStepSeries);
            }
            if (_showSevenDay && _appState?.SevenDay is not null)
            {
                _sevenDayLineSeries.Values = Enumerable.Repeat(
                    _appState.SevenDay.Utilization,
                    Math.Max(_sparklineData.Count, 2)).ToArray();
                seriesList.Add(_sevenDayLineSeries);
            }
        }
        else
        {
            if (_showFiveHour && _sparklineData.Count > 0)
            {
                _fiveHourBarSeries.Values = AggregateToBarData(_sparklineData, SelectedTimeRange);
                seriesList.Add(_fiveHourBarSeries);
            }
            // 7d in bar mode is rendered as a RectangularSection reference line
            // (see RefreshChartSections) — no LineSeries needed here
        }

        // Add projection line if slope is rising (only for 24h view)
        if (SelectedTimeRange == "24h" && _showFiveHour && _sparklineData.Count > 0)
        {
            var currentUtil = _appState?.FiveHour?.Utilization ?? 0;
            var slopeRate = _appState?.FiveHourSlopeRate ?? 0;

            if (currentUtil > 0 && slopeRate > 0.3)
            {
                var remainingPercent = 100.0 - currentUtil;
                var pointsToExhaustion = (int)(remainingPercent / slopeRate);
                var projectionPoints = Math.Min(pointsToExhaustion, _sparklineData.Count / 2);

                if (projectionPoints > 1)
                {
                    var projectionValues = new List<double>();
                    for (int i = 0; i < _sparklineData.Count; i++)
                        projectionValues.Add(double.NaN);
                    for (int i = 0; i < projectionPoints; i++)
                    {
                        var projected = currentUtil + (slopeRate * (i + 1));
                        projectionValues.Add(Math.Min(100, projected));
                    }
                    _projectionSeries.Values = projectionValues.ToArray();
                    seriesList.Add(_projectionSeries);
                    hasProjection = true;
                }
            }
        }

        // Only replace the series array when structure changes (avoids chart reset)
        var seriesKey = $"{SelectedTimeRange}|{_showFiveHour}|{_showSevenDay}|{hasProjection}|{seriesList.Count}";
        if (seriesKey != _lastSeriesKey)
        {
            _lastSeriesKey = seriesKey;
            AnalyticsSeries = seriesList.ToArray();
        }

        // Update X axis labels in-place (no new Axis object)
        var labelCount = SelectedTimeRange == "24h" ? _sparklineData.Count
            : (_sparklineData.Count > 0 ? AggregateToBarData(_sparklineData, SelectedTimeRange).Length : 0);
        _xAxis.Labels = GenerateTimeLabels(labelCount, SelectedTimeRange);
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

    public Axis[] AnalyticsXAxes { get; }
    public Axis[] AnalyticsYAxes { get; }

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
                // Show every 2nd point for denser time labels (every ~2 hours)
                labels[i] = (i % 2 == 0) ? pointTime.ToString("h tt") : "";
            }
            else if (totalHours <= 168)
            {
                // Every day label + time for better granularity
                var step = Math.Max(1, dataPointCount / 14);
                labels[i] = (i % step == 0) ? pointTime.ToString("ddd h tt") : "";
            }
            else
            {
                // More date labels for 30d+
                var step = Math.Max(1, dataPointCount / 10);
                labels[i] = (i % step == 0) ? pointTime.ToString("MMM d") : "";
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
        if (_dismissedPatterns.Contains(title)) return; // skip dismissed

        var card = new PatternCardViewModel
        {
            Title = title,
            Summary = summary,
        };
        card.DismissCommand = ReactiveCommand.Create(() =>
        {
            _dismissedPatterns.Add(title);
            PatternCards.Remove(card);
            PatternDismissed?.Invoke(this, title);
        });
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

    // Stable cycle chart objects
    private readonly ColumnSeries<double> _cycleBarSeries = new()
    {
        Name = "Avg utilization",
        Fill = new SolidColorPaint(SKColor.Parse("#66B866")),
        MaxBarWidth = 32,
        Padding = 12,
        DataLabelsPaint = new SolidColorPaint(SKColor.Parse("#F5F7FB")),
        DataLabelsSize = 10,
        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
        DataLabelsFormatter = point => $"{point.Model:F0}%",
    };
    private readonly Axis _cycleXAxis = new()
    {
        Labels = Array.Empty<string>(),
        TextSize = 10,
        LabelsPaint = new SolidColorPaint(SKColor.Parse("#6B7A8D")),
        ShowSeparatorLines = false,
    };
    private readonly Axis _cycleYAxis = new()
    {
        MinLimit = 0,
        MaxLimit = 100,
        ShowSeparatorLines = false,
        IsVisible = false,
    };

    public ISeries[] CycleSeries { get; }
    public Axis[] CycleXAxes { get; }
    public Axis[] CycleYAxes { get; }

    private void RefreshCycleChart()
    {
        // Only show for 30d or All time ranges
        if (SelectedTimeRange is not ("30d" or "All"))
        {
            ShowCycleComparison = false;
            return;
        }

        if (_sparklineData.Count < 3)
        {
            ShowCycleComparison = false;
            return;
        }

        var third = _sparklineData.Count / 3;
        var period1Avg = _sparklineData.Take(third).Average();
        var period2Avg = _sparklineData.Skip(third).Take(third).Average();
        var period3Avg = _sparklineData.Skip(third * 2).Average();

        // Generate date-range labels for each third
        var totalDays = SelectedTimeRange == "30d" ? 30 : 90;
        var thirdDays = totalDays / 3;
        var now = DateTime.Now;
        var label1 = $"{now.AddDays(-totalDays):MMM d}-{now.AddDays(-totalDays + thirdDays):MMM d}";
        var label2 = $"{now.AddDays(-totalDays + thirdDays):MMM d}-{now.AddDays(-thirdDays):MMM d}";
        var label3 = $"{now.AddDays(-thirdDays):MMM d}-Today";

        _cycleXAxis.Labels = new[] { label1, label2, label3 };
        var values = new[] { period1Avg, period2Avg, period3Avg };
        _cycleData = new List<double>(values);
        _cycleBarSeries.Values = values;

        ShowCycleComparison = true;
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
