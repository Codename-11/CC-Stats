using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CCStats.Desktop.Controls;

public class CountdownLabel : UserControl
{
    public static readonly StyledProperty<DateTimeOffset?> TargetTimeProperty =
        AvaloniaProperty.Register<CountdownLabel, DateTimeOffset?>(nameof(TargetTime));

    public static readonly StyledProperty<Color> PrimaryColorProperty =
        AvaloniaProperty.Register<CountdownLabel, Color>(nameof(PrimaryColor), HeadroomColors.TextPrimary);

    public static readonly StyledProperty<bool> IsExhaustedProperty =
        AvaloniaProperty.Register<CountdownLabel, bool>(nameof(IsExhausted), false);

    private readonly TextBlock _countdownText;
    private readonly TextBlock _resetTimeText;
    private readonly DispatcherTimer _timer;

    static CountdownLabel()
    {
        TargetTimeProperty.Changed.AddClassHandler<CountdownLabel>((c, _) => c.UpdateDisplay());
        IsExhaustedProperty.Changed.AddClassHandler<CountdownLabel>((c, _) => c.UpdateDisplay());
        PrimaryColorProperty.Changed.AddClassHandler<CountdownLabel>((c, _) => c.UpdateDisplay());
    }

    public CountdownLabel()
    {
        _countdownText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(HeadroomColors.TextSecondary),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };

        _resetTimeText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(HeadroomColors.TextTertiary),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Children = { _countdownText, _resetTimeText }
        };

        Content = panel;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60),
        };
        _timer.Tick += (_, _) => UpdateDisplay();
    }

    public DateTimeOffset? TargetTime
    {
        get => GetValue(TargetTimeProperty);
        set => SetValue(TargetTimeProperty, value);
    }

    public Color PrimaryColor
    {
        get => GetValue(PrimaryColorProperty);
        set => SetValue(PrimaryColorProperty, value);
    }

    public bool IsExhausted
    {
        get => GetValue(IsExhaustedProperty);
        set => SetValue(IsExhaustedProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateDisplay();
        if (IsVisible)
            _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void UpdateDisplay()
    {
        var target = TargetTime;
        if (target is null)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        var now = DateTimeOffset.Now;
        var remaining = target.Value - now;

        string countdownStr;
        if (remaining.TotalSeconds <= 0)
        {
            countdownStr = "resetting...";
        }
        else if (remaining.TotalMinutes < 1)
        {
            countdownStr = "resets in <1m";
        }
        else if (remaining.TotalHours < 1)
        {
            countdownStr = $"resets in {(int)remaining.TotalMinutes}m";
        }
        else if (remaining.TotalDays < 1)
        {
            var hours = (int)remaining.TotalHours;
            var minutes = remaining.Minutes;
            countdownStr = minutes > 0 ? $"resets in {hours}h {minutes}m" : $"resets in {hours}h";
        }
        else
        {
            var days = (int)remaining.TotalDays;
            var hours = remaining.Hours;
            countdownStr = hours > 0 ? $"resets in {days}d {hours}h" : $"resets in {days}d";
        }

        _countdownText.Text = countdownStr;
        _countdownText.Foreground = new SolidColorBrush(
            IsExhausted ? PrimaryColor : HeadroomColors.TextSecondary);

        _resetTimeText.Text = $"at {target.Value.LocalDateTime:h:mm tt}";
        _resetTimeText.Foreground = new SolidColorBrush(HeadroomColors.TextTertiary);
    }
}
