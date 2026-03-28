using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CCStats.Desktop.Controls;

public class ExtraUsageBar : Control
{
    public static readonly StyledProperty<double> UtilizationProperty =
        AvaloniaProperty.Register<ExtraUsageBar, double>(nameof(Utilization), 0);

    public static readonly StyledProperty<double> BarHeightProperty =
        AvaloniaProperty.Register<ExtraUsageBar, double>(nameof(BarHeight), 6);

    public static readonly StyledProperty<double> BarCornerRadiusProperty =
        AvaloniaProperty.Register<ExtraUsageBar, double>(nameof(BarCornerRadius), 3);

    static ExtraUsageBar()
    {
        AffectsRender<ExtraUsageBar>(
            UtilizationProperty,
            BarHeightProperty,
            BarCornerRadiusProperty);
    }

    public double Utilization
    {
        get => GetValue(UtilizationProperty);
        set => SetValue(UtilizationProperty, value);
    }

    public double BarHeight
    {
        get => GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    public double BarCornerRadius
    {
        get => GetValue(BarCornerRadiusProperty);
        set => SetValue(BarCornerRadiusProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 100 : availableSize.Width;
        return new Size(width, BarHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = BarHeight;
        var cornerRadius = BarCornerRadius;
        var utilization = Math.Clamp(Utilization, 0, 100);

        // Background track (quaternary color)
        var trackBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        var trackRect = new Rect(0, 0, width, height);
        context.DrawRectangle(trackBrush, null, trackRect, cornerRadius, cornerRadius);

        // Fill bar
        if (utilization > 0)
        {
            var fillWidth = width * (utilization / 100.0);
            var fillColor = HeadroomColors.ForExtraUsage(utilization);
            var fillBrush = new SolidColorBrush(fillColor);
            var fillRect = new Rect(0, 0, fillWidth, height);
            context.DrawRectangle(fillBrush, null, fillRect, cornerRadius, cornerRadius);
        }
    }
}
