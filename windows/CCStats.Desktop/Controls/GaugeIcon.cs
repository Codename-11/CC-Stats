using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCStats.Core.Models;

namespace CCStats.Desktop.Controls;

public class GaugeIcon : Control
{
    public static readonly StyledProperty<double> PercentageProperty =
        AvaloniaProperty.Register<GaugeIcon, double>(nameof(Percentage), 0);

    public static readonly StyledProperty<HeadroomState> HeadroomStateProperty =
        AvaloniaProperty.Register<GaugeIcon, HeadroomState>(nameof(HeadroomState), HeadroomState.Normal);

    public static readonly StyledProperty<bool> ShowSevenDayDotProperty =
        AvaloniaProperty.Register<GaugeIcon, bool>(nameof(ShowSevenDayDot), false);

    public static readonly StyledProperty<HeadroomState?> SevenDayStateProperty =
        AvaloniaProperty.Register<GaugeIcon, HeadroomState?>(nameof(SevenDayState));

    static GaugeIcon()
    {
        AffectsRender<GaugeIcon>(
            PercentageProperty,
            HeadroomStateProperty,
            ShowSevenDayDotProperty,
            SevenDayStateProperty);
    }

    public double Percentage
    {
        get => GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public HeadroomState HeadroomState
    {
        get => GetValue(HeadroomStateProperty);
        set => SetValue(HeadroomStateProperty, value);
    }

    public bool ShowSevenDayDot
    {
        get => GetValue(ShowSevenDayDotProperty);
        set => SetValue(ShowSevenDayDotProperty, value);
    }

    public HeadroomState? SevenDayState
    {
        get => GetValue(SevenDayStateProperty);
        set => SetValue(SevenDayStateProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(18, 18);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var state = HeadroomState;
        var color = HeadroomColors.ForHeadroomState(state);

        const double centerX = 9;
        const double centerY = 13;
        const double radius = 7;
        const double strokeWidth = 2;
        const double needleLength = 5;

        if (state == HeadroomState.Disconnected)
        {
            // Draw X lines in light gray
            var grayPen = new Pen(new SolidColorBrush(Color.Parse("#7B8798")), 2,
                lineCap: PenLineCap.Round);
            context.DrawLine(grayPen, new Point(4, 5), new Point(14, 13));
            context.DrawLine(grayPen, new Point(14, 5), new Point(4, 13));
            return;
        }

        // Track arc at 25% opacity
        var trackColor = Color.FromArgb(64, color.R, color.G, color.B);
        var trackPen = new Pen(new SolidColorBrush(trackColor), strokeWidth,
            lineCap: PenLineCap.Round);
        DrawSemicircle(context, centerX, centerY, radius, trackPen);

        // Filled arc
        var fillPen = new Pen(new SolidColorBrush(color), strokeWidth,
            lineCap: PenLineCap.Round);
        var percentage = Math.Clamp(Percentage, 0, 100);
        if (percentage > 0)
        {
            DrawArc(context, centerX, centerY, radius, percentage, fillPen);
        }

        // Center dot
        var dotBrush = new SolidColorBrush(color);
        context.DrawEllipse(dotBrush, null, new Point(centerX, centerY), 1.5, 1.5);

        // Needle pointing toward arc position
        var needleAngle = Math.PI - (percentage / 100.0) * Math.PI;
        var needleEndX = centerX + needleLength * Math.Cos(needleAngle);
        var needleEndY = centerY - needleLength * Math.Sin(needleAngle);
        var needlePen = new Pen(new SolidColorBrush(color), 1.5,
            lineCap: PenLineCap.Round);
        context.DrawLine(needlePen, new Point(centerX, centerY),
            new Point(needleEndX, needleEndY));

        // 7-day overlay dot
        if (ShowSevenDayDot && SevenDayState.HasValue)
        {
            var dotColor = HeadroomColors.ForHeadroomState(SevenDayState.Value);
            var sevenDayBrush = new SolidColorBrush(dotColor);
            context.DrawEllipse(sevenDayBrush, null,
                new Point(14.5, 3.5), 2, 2);
        }
    }

    private static void DrawSemicircle(DrawingContext context, double cx, double cy,
        double radius, Pen pen)
    {
        DrawArc(context, cx, cy, radius, 100, pen);
    }

    private static void DrawArc(DrawingContext context, double cx, double cy,
        double radius, double percentage, Pen pen)
    {
        // Arc from PI (left, 0%) to target angle
        var startAngle = Math.PI; // left side
        var endAngle = Math.PI - (percentage / 100.0) * Math.PI;

        var startX = cx + radius * Math.Cos(startAngle);
        var startY = cy - radius * Math.Sin(startAngle);
        var endX = cx + radius * Math.Cos(endAngle);
        var endY = cy - radius * Math.Sin(endAngle);

        var isLargeArc = percentage > 50;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(startX, startY), false);
            ctx.ArcTo(
                new Point(endX, endY),
                new Size(radius, radius),
                0,
                isLargeArc,
                SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
