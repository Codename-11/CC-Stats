using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCStats.Core.Models;
using CCStats.Desktop.Controls;

namespace CCStats.Desktop.Services;

/// <summary>
/// Manages the system tray icon with a dynamically rendered gauge that reflects
/// the current headroom state. All tray-related logic (icon rendering, context menu,
/// window toggling, and positioning) lives here so App.axaml.cs stays clean.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;
    private Window? _window;
    private bool _disposed;
    private NativeMenuItem? _statusItem;

    private Action? _onAnalyticsRequested;
    private Action? _onSettingsRequested;
    private Action? _onSignOutRequested;

    // Gauge geometry for 32x32 icon
    private const int IconSize = 32;
    private const double CenterX = 16;
    private const double CenterY = 22;
    private const double ArcRadius = 10;
    private const double StrokeWidth = 3;
    private const double NeedleLength = 7;
    private const double DotRadius = 1.5;

    /// <summary>
    /// Creates the tray icon, registers it with the application, and wires up
    /// the context menu. Call this once from OnFrameworkInitializationCompleted.
    /// </summary>
    public static TrayIconService Setup(
        Application app,
        Window mainWindow,
        Action? onAnalyticsRequested = null,
        Action? onSettingsRequested = null,
        Action? onSignOutRequested = null)
    {
        var service = new TrayIconService();
        service._window = mainWindow;
        service._onAnalyticsRequested = onAnalyticsRequested;
        service._onSettingsRequested = onSettingsRequested;
        service._onSignOutRequested = onSignOutRequested;

        service._trayIcon = new TrayIcon
        {
            ToolTipText = "CC-Stats (Windows)",
            Icon = service.RenderGaugeIcon(0, HeadroomState.Disconnected),
            IsVisible = true,
        };

        service._trayIcon.Clicked += (_, _) => service.ToggleWindow();

        // Context menu
        var menu = new NativeMenu();

        // Quick status (non-clickable header)
        var statusItem = new NativeMenuItem("CC-Stats") { IsEnabled = false };
        service._statusItem = statusItem;
        menu.Add(statusItem);
        menu.Add(new NativeMenuItemSeparator());

        // Show/Hide main window
        var showItem = new NativeMenuItem("Show/Hide");
        showItem.Click += (_, _) => service.ToggleWindow();
        menu.Add(showItem);

        // Open Analytics
        var analyticsItem = new NativeMenuItem("Usage Analytics");
        analyticsItem.Click += (_, _) =>
        {
            service._onAnalyticsRequested?.Invoke();
        };
        menu.Add(analyticsItem);

        // Settings
        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) =>
        {
            service._onSettingsRequested?.Invoke();
        };
        menu.Add(settingsItem);

        menu.Add(new NativeMenuItemSeparator());

        // Sign Out
        var signOutItem = new NativeMenuItem("Sign Out");
        signOutItem.Click += (_, _) =>
        {
            service._onSignOutRequested?.Invoke();
        };
        menu.Add(signOutItem);

        menu.Add(new NativeMenuItemSeparator());

        // Quit
        var quitItem = new NativeMenuItem("Quit CC-Stats");
        quitItem.Click += (_, _) =>
        {
            service._trayIcon.IsVisible = false;
            if (app.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };
        menu.Add(quitItem);

        service._trayIcon.Menu = menu;

        // Register in the application-level tray icon collection
        var icons = new TrayIcons { service._trayIcon };
        TrayIcon.SetIcons(app, icons);

        return service;
    }

    /// <summary>
    /// Updates the tray icon bitmap and tooltip to reflect the latest poll data.
    /// Call this whenever the headroom percentage or state changes.
    /// </summary>
    public void UpdateIcon(double percentage, HeadroomState state, string tooltip)
    {
        if (_trayIcon is null) return;

        _trayIcon.Icon = RenderGaugeIcon(percentage, state);
        _trayIcon.ToolTipText = tooltip;
        UpdateStatusText(tooltip);
    }

    /// <summary>
    /// Updates the status text shown in the context menu header item.
    /// </summary>
    public void UpdateStatusText(string text)
    {
        if (_statusItem is not null)
            _statusItem.Header = text;
    }

    /// <summary>
    /// Renders a 32x32 gauge icon as a <see cref="WindowIcon"/>.
    /// The gauge mirrors the geometry of <see cref="GaugeIcon"/> but is scaled
    /// for the system tray.
    /// </summary>
    public WindowIcon RenderGaugeIcon(double percentage, HeadroomState state)
    {
        var pixelSize = new PixelSize(IconSize, IconSize);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));

        using (var ctx = bitmap.CreateDrawingContext())
        {
            DrawGauge(ctx, percentage, state);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    /// <summary>
    /// Toggles the main window between visible and hidden. When showing,
    /// positions it near the system tray (bottom-right of the working area).
    /// </summary>
    public void ToggleWindow()
    {
        if (_window is null) return;

        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            PositionWindowNearTray(_window);
            _window.Show();
            _window.Activate();
        }
    }

    /// <summary>
    /// Places the window at the bottom-right of the primary screen's working
    /// area, just above the taskbar with a small margin.
    /// </summary>
    public static void PositionWindowNearTray(Window window)
    {
        var screen = window.Screens.Primary;
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;

        // WorkingArea and Position are both in device pixels on Windows
        var winWidth = (int)(window.Width * scaling);
        var winHeight = (int)((window.Bounds.Height > 0 ? window.Bounds.Height : 400) * scaling);
        var gap = (int)(12 * scaling);

        var x = workArea.Right - winWidth - gap;
        var y = workArea.Bottom - winHeight - gap;

        window.Position = new PixelPoint(x, y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _window = null;
    }

    // ----------------------------------------------------------------
    //  Private drawing helpers (mirror GaugeIcon geometry at 32x32)
    // ----------------------------------------------------------------

    private static void DrawGauge(DrawingContext ctx, double percentage, HeadroomState state)
    {
        var color = HeadroomColors.ForHeadroomState(state);
        percentage = Math.Clamp(percentage, 0, 100);

        if (state == HeadroomState.Disconnected)
        {
            // Draw an X to indicate disconnected
            var grayPen = new Pen(new SolidColorBrush(Color.Parse("#7B8798")), StrokeWidth,
                lineCap: PenLineCap.Round);
            ctx.DrawLine(grayPen, new Point(8, 8), new Point(24, 24));
            ctx.DrawLine(grayPen, new Point(24, 8), new Point(8, 24));
            return;
        }

        // Track arc at 25% opacity
        var trackColor = Color.FromArgb(64, color.R, color.G, color.B);
        var trackPen = new Pen(new SolidColorBrush(trackColor), StrokeWidth,
            lineCap: PenLineCap.Round);
        DrawArc(ctx, CenterX, CenterY, ArcRadius, 100, trackPen);

        // Filled arc
        if (percentage > 0)
        {
            var fillPen = new Pen(new SolidColorBrush(color), StrokeWidth,
                lineCap: PenLineCap.Round);
            DrawArc(ctx, CenterX, CenterY, ArcRadius, percentage, fillPen);
        }

        // Center dot
        var dotBrush = new SolidColorBrush(color);
        ctx.DrawEllipse(dotBrush, null, new Point(CenterX, CenterY), DotRadius, DotRadius);

        // Needle
        var needleAngle = Math.PI - (percentage / 100.0) * Math.PI;
        var needleEndX = CenterX + NeedleLength * Math.Cos(needleAngle);
        var needleEndY = CenterY - NeedleLength * Math.Sin(needleAngle);
        var needlePen = new Pen(new SolidColorBrush(color), 2,
            lineCap: PenLineCap.Round);
        ctx.DrawLine(needlePen, new Point(CenterX, CenterY),
            new Point(needleEndX, needleEndY));
    }

    private static void DrawArc(DrawingContext ctx, double cx, double cy,
        double radius, double percentage, Pen pen)
    {
        var startAngle = Math.PI; // left side (0%)
        var endAngle = Math.PI - (percentage / 100.0) * Math.PI;

        var startX = cx + radius * Math.Cos(startAngle);
        var startY = cy - radius * Math.Sin(startAngle);
        var endX = cx + radius * Math.Cos(endAngle);
        var endY = cy - radius * Math.Sin(endAngle);

        var geometry = new StreamGeometry();
        using (var sgCtx = geometry.Open())
        {
            sgCtx.BeginFigure(new Point(startX, startY), false);
            sgCtx.ArcTo(
                new Point(endX, endY),
                new Size(radius, radius),
                0,
                false, // semicircle arcs are always ≤ 180°, never "large"
                SweepDirection.Clockwise);
            sgCtx.EndFigure(false);
        }

        ctx.DrawGeometry(null, pen, geometry);
    }
}
