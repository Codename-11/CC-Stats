using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CCStats.Desktop.ViewModels;

namespace CCStats.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _lastViewModel;
    private IDisposable? _boundsSubscription;

    public MainWindow()
    {
        InitializeComponent();

        var sparkline = this.FindControl<Border>("SparklineBorder");
        if (sparkline != null)
        {
            sparkline.PointerPressed += (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.ToggleInlineAnalyticsCommand.Execute().Subscribe();
            };
        }

        var dragRegion = this.FindControl<Border>("DragRegion");
        if (dragRegion != null)
        {
            dragRegion.PointerPressed += OnDragRegionPressed;
        }

        // ✕ button minimizes to tray instead of quitting
        var minimizeBtn = this.FindControl<Button>("MinimizeButton");
        if (minimizeBtn != null)
        {
            minimizeBtn.Click += (_, _) => Hide();
        }

        DataContextChanged += OnDataContextChanged;
        Opened += OnFirstOpened;

        // Re-snap when window size changes (settings open/close makes it taller/shorter)
        // This keeps the bottom edge anchored above the taskbar.
        _boundsSubscription = this.GetObservable(BoundsProperty).Subscribe(_ =>
        {
            if (DataContext is MainWindowViewModel vm && vm.IsDocked && Bounds.Height > 10)
            {
                SnapToTaskbar();
            }
        });
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from previous ViewModel to prevent event handler leaks
        if (_lastViewModel is not null)
        {
            _lastViewModel.DockStateChanged -= OnDockStateChanged;
            _lastViewModel.InlineAnalyticsChanged -= OnInlineAnalyticsChanged;
            _lastViewModel.CopyStatusRequested -= OnCopyStatusRequested;
            _lastViewModel = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.DockStateChanged += OnDockStateChanged;
            vm.InlineAnalyticsChanged += OnInlineAnalyticsChanged;
            vm.CopyStatusRequested += OnCopyStatusRequested;
            _lastViewModel = vm;
        }
    }

    private async void OnCopyStatusRequested(object? sender, string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnInlineAnalyticsChanged(object? sender, bool expanded)
    {
        Width = expanded ? 420 : 320;

        if (DataContext is MainWindowViewModel vm && vm.IsDocked)
        {
            SnapToTaskbar();
        }
    }

    private void OnFirstOpened(object? sender, EventArgs e)
    {
        Opened -= OnFirstOpened;
        // Defer initial snap until after first layout pass
        DispatcherTimer.RunOnce(() => SnapToTaskbar(), TimeSpan.FromMilliseconds(50));
    }

    private void OnDockStateChanged(object? sender, bool isDocked)
    {
        if (isDocked) SnapToTaskbar();
    }

    /// <summary>
    /// Positions the window bottom-right, just above the taskbar.
    /// Bottom edge stays anchored — window grows upward.
    /// </summary>
    public void SnapToTaskbar()
    {
        var screen = Screens.Primary;
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;
        var gap = (int)(12 * scaling);

        var winWidth = (int)(Width * scaling);
        var winHeight = (int)(Bounds.Height * scaling);
        if (winHeight < 10) winHeight = (int)(420 * scaling); // fallback before layout

        var x = Math.Max(workArea.X, workArea.Right - winWidth - gap);
        var y = Math.Max(workArea.Y, workArea.Bottom - winHeight - gap);

        Position = new PixelPoint(x, y);
    }

    private void OnDragRegionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.IsDocked)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _boundsSubscription?.Dispose();
        _boundsSubscription = null;

        if (_lastViewModel is not null)
        {
            _lastViewModel.DockStateChanged -= OnDockStateChanged;
            _lastViewModel.InlineAnalyticsChanged -= OnInlineAnalyticsChanged;
            _lastViewModel.CopyStatusRequested -= OnCopyStatusRequested;
            _lastViewModel = null;
        }

        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.F5 && DataContext is MainWindowViewModel vm)
        {
            vm.CyclePreview();
            e.Handled = true;
        }

        if (e.Key == Key.Escape && DataContext is MainWindowViewModel vmEsc)
        {
            if (vmEsc.ShowSettings)
            {
                vmEsc.ShowSettings = false;
                e.Handled = true;
            }
        }
    }
}
