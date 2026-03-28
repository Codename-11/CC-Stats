using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace CCStats.Desktop.Controls;

public class HeadroomRingGauge : Control
{
    public static readonly StyledProperty<double> PercentageProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, double>(nameof(Percentage), 0);

    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, double>(nameof(Diameter), 96);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, double>(nameof(StrokeWidth), 7);

    public static readonly StyledProperty<Color> FillColorProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, Color>(nameof(FillColor), HeadroomColors.Normal);

    public static readonly StyledProperty<Color> TrackColorProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, Color>(nameof(TrackColor),
            Color.FromArgb(77, 128, 128, 128)); // gray at ~30% opacity

    public static readonly StyledProperty<bool> ShowPercentageTextProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, bool>(nameof(ShowPercentageText), true);

    public static readonly StyledProperty<string> SlopeArrowProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, string>(nameof(SlopeArrow), "");

    public static readonly StyledProperty<Color> SlopeColorProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, Color>(nameof(SlopeColor), HeadroomColors.TextSecondary);

    // Animated percentage for smooth transitions
    public static readonly StyledProperty<double> AnimatedPercentageProperty =
        AvaloniaProperty.Register<HeadroomRingGauge, double>(nameof(AnimatedPercentage), 0);

    static HeadroomRingGauge()
    {
        AffectsRender<HeadroomRingGauge>(
            AnimatedPercentageProperty,
            DiameterProperty,
            StrokeWidthProperty,
            FillColorProperty,
            TrackColorProperty,
            ShowPercentageTextProperty,
            SlopeArrowProperty,
            SlopeColorProperty);

        PercentageProperty.Changed.AddClassHandler<HeadroomRingGauge>((gauge, _) =>
            gauge.AnimateToPercentage());
    }

    public double Percentage
    {
        get => GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public double Diameter
    {
        get => GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public Color FillColor
    {
        get => GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public Color TrackColor
    {
        get => GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public bool ShowPercentageText
    {
        get => GetValue(ShowPercentageTextProperty);
        set => SetValue(ShowPercentageTextProperty, value);
    }

    public string SlopeArrow
    {
        get => GetValue(SlopeArrowProperty);
        set => SetValue(SlopeArrowProperty, value);
    }

    public Color SlopeColor
    {
        get => GetValue(SlopeColorProperty);
        set => SetValue(SlopeColorProperty, value);
    }

    public double AnimatedPercentage
    {
        get => GetValue(AnimatedPercentageProperty);
        set => SetValue(AnimatedPercentageProperty, value);
    }

    private void AnimateToPercentage()
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(AnimatedPercentageProperty, AnimatedPercentage) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(AnimatedPercentageProperty, Percentage) }
                }
            }
        };

        animation.RunAsync(this);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var d = Diameter;
        var sw = StrokeWidth;
        var radius = (d - sw) / 2;
        // The semicircle arc extends from centerY upward by radius + half stroke.
        // We want generous padding above and a small amount below the center for the dot.
        var topClearance = sw + 2; // space above outermost stroke edge
        var totalHeight = topClearance + sw / 2 + radius + 4; // arc height + bottom pad
        return new Size(d, totalHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var diameter = Diameter;
        var strokeWidth = StrokeWidth;
        var percentage = Math.Clamp(AnimatedPercentage, 0, 100);
        var radius = (diameter - strokeWidth) / 2;
        var centerX = diameter / 2;
        // centerY is placed so that the arc top (centerY - radius - strokeWidth/2) = topClearance
        var topClearance = strokeWidth + 2;
        var centerY = topClearance + strokeWidth / 2 + radius;

        // Track pen (background arc)
        var trackPen = new Pen(new SolidColorBrush(TrackColor), strokeWidth,
            lineCap: PenLineCap.Round);

        // Fill pen (active arc)
        var fillPen = new Pen(new SolidColorBrush(FillColor), strokeWidth,
            lineCap: PenLineCap.Round);

        // Draw track arc (full semicircle from left to right, 180 degrees)
        DrawArc(context, centerX, centerY, radius, 0, 100, trackPen);

        // Draw filled arc proportional to percentage
        if (percentage > 0)
        {
            DrawArc(context, centerX, centerY, radius, 0, percentage, fillPen);
        }

        // Center dot
        var dotBrush = new SolidColorBrush(FillColor);
        context.DrawEllipse(dotBrush, null, new Point(centerX, centerY), 1.5, 1.5);

        // Center text - scale font size with diameter
        if (ShowPercentageText)
        {
            var percentText = $"{(int)Math.Round(Percentage)}%";
            var textBrush = new SolidColorBrush(FillColor);
            // Scale: 96px diameter → 18px font, 56px → 12px font
            var fontSize = Math.Max(10, diameter * 0.19);

            var formattedText = new FormattedText(
                percentText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold),
                fontSize,
                textBrush);

            var textX = centerX - formattedText.Width / 2;
            // Position text centered vertically in the arc area
            var textY = centerY - formattedText.Height - 4;
            context.DrawText(formattedText, new Point(textX, textY));

            // Slope arrow is now rendered outside the gauge (in AXAML) for readability
        }
    }

    private static void DrawArc(DrawingContext context, double cx, double cy,
        double radius, double fromPercent, double toPercent, Pen pen)
    {
        // Arc goes from PI (left, 0%) to 0 (right, 100%) — a semicircle.
        // The gauge sweeps clockwise in screen coords: left → top → right.
        var startAngle = Math.PI - (fromPercent / 100.0) * Math.PI;
        var endAngle = Math.PI - (toPercent / 100.0) * Math.PI;

        var startX = cx + radius * Math.Cos(startAngle);
        var startY = cy - radius * Math.Sin(startAngle);
        var endX = cx + radius * Math.Cos(endAngle);
        var endY = cy - radius * Math.Sin(endAngle);

        // Our arcs are at most 180° (a semicircle), so isLargeArc is always false.
        // Setting it true for >50% fill was sending the arc the wrong way (through the bottom).
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(startX, startY), false);
            ctx.ArcTo(
                new Point(endX, endY),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
