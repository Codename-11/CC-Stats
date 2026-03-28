using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CCStats.Desktop.Controls;

public class SparklineControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> DataProperty =
        AvaloniaProperty.Register<SparklineControl, IReadOnlyList<double>?>(nameof(Data));

    public static readonly StyledProperty<Color> FillColorProperty =
        AvaloniaProperty.Register<SparklineControl, Color>(nameof(FillColor),
            Color.FromArgb(77, 102, 184, 102)); // green at ~30% opacity

    public static readonly StyledProperty<Color> StrokeColorProperty =
        AvaloniaProperty.Register<SparklineControl, Color>(nameof(StrokeColor),
            HeadroomColors.Normal);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<SparklineControl, double>(nameof(StrokeWidth), 1);

    public static readonly StyledProperty<TimeSpan> GapThresholdProperty =
        AvaloniaProperty.Register<SparklineControl, TimeSpan>(nameof(GapThreshold),
            TimeSpan.FromMinutes(60));

    public new static readonly StyledProperty<double> MinHeightProperty =
        AvaloniaProperty.Register<SparklineControl, double>(nameof(MinHeight), 40);

    public static readonly StyledProperty<bool> ShowAnalyticsDotProperty =
        AvaloniaProperty.Register<SparklineControl, bool>(nameof(ShowAnalyticsDot), true);

    static SparklineControl()
    {
        AffectsRender<SparklineControl>(
            DataProperty,
            FillColorProperty,
            StrokeColorProperty,
            StrokeWidthProperty,
            MinHeightProperty,
            ShowAnalyticsDotProperty);
    }

    public IReadOnlyList<double>? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Color FillColor
    {
        get => GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public Color StrokeColor
    {
        get => GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public TimeSpan GapThreshold
    {
        get => GetValue(GapThresholdProperty);
        set => SetValue(GapThresholdProperty, value);
    }

    public new double MinHeight
    {
        get => GetValue(MinHeightProperty);
        set => SetValue(MinHeightProperty, value);
    }

    public bool ShowAnalyticsDot
    {
        get => GetValue(ShowAnalyticsDotProperty);
        set => SetValue(ShowAnalyticsDotProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsInfinity(availableSize.Height) ? MinHeight : Math.Max(MinHeight, availableSize.Height);
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 200 : availableSize.Width;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var data = Data;
        var bounds = Bounds;
        var width = bounds.Width;
        var height = bounds.Height;

        if (data is null || data.Count < 2)
        {
            // Placeholder text
            var placeholderBrush = new SolidColorBrush(HeadroomColors.TextTertiary);
            var text = new FormattedText(
                "Building history...",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Italic),
                12,
                placeholderBrush);

            var x = (width - text.Width) / 2;
            var y = (height - text.Height) / 2;
            context.DrawText(text, new Point(x, y));
            return;
        }

        var count = data.Count;
        var stepWidth = width / count;
        var strokePen = new Pen(new SolidColorBrush(StrokeColor), StrokeWidth);
        var fillBrush = new SolidColorBrush(FillColor);

        // Build step-area path
        var strokeGeometry = new StreamGeometry();
        var fillGeometry = new StreamGeometry();

        using (var strokeCtx = strokeGeometry.Open())
        using (var fillCtx = fillGeometry.Open())
        {
            var firstY = height - (data[0] / 100.0 * height);

            // Fill starts at baseline
            fillCtx.BeginFigure(new Point(0, height), true);
            fillCtx.LineTo(new Point(0, firstY));

            // Stroke starts at first data point
            strokeCtx.BeginFigure(new Point(0, firstY), false);

            var prevY = firstY;

            for (var i = 1; i < count; i++)
            {
                var x = i * stepWidth;
                var y = height - (data[i] / 100.0 * height);

                // Noise suppression: extend horizontal if change < 1%
                if (Math.Abs(data[i] - data[i - 1]) < 1.0)
                {
                    y = prevY;
                }

                // Horizontal line to step boundary, then vertical step
                strokeCtx.LineTo(new Point(x, prevY));
                strokeCtx.LineTo(new Point(x, y));

                fillCtx.LineTo(new Point(x, prevY));
                fillCtx.LineTo(new Point(x, y));

                prevY = y;
            }

            // Extend to right edge
            strokeCtx.LineTo(new Point(width, prevY));
            strokeCtx.EndFigure(false);

            fillCtx.LineTo(new Point(width, prevY));
            fillCtx.LineTo(new Point(width, height));
            fillCtx.EndFigure(true);
        }

        // Draw fill then stroke
        context.DrawGeometry(fillBrush, null, fillGeometry);
        context.DrawGeometry(null, strokePen, strokeGeometry);

        // Analytics dot
        if (ShowAnalyticsDot)
        {
            var dotBrush = new SolidColorBrush(Color.Parse("#4A90D9"));
            context.DrawEllipse(dotBrush, null,
                new Point(width - 6, 6), 4, 4);
        }
    }
}
