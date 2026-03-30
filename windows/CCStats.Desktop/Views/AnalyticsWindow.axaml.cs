using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CCStats.Desktop.Views;

public partial class AnalyticsWindow : Window
{
    private const int ResizeGrip = 6;
    private WindowEdge? _lastEdge; // cache to avoid cursor re-creation

    public AnalyticsWindow()
    {
        InitializeComponent();

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null)
        {
            closeButton.Click += (_, _) => Close();
        }

        var dragRegion = this.FindControl<Border>("AnalyticsDragRegion");
        if (dragRegion != null)
        {
            dragRegion.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                    e.Handled = true;
                }
            };
        }

        // Edge resize for borderless window
        PointerPressed += OnWindowPointerPressed;
        PointerMoved += OnWindowPointerMoved;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var edge = GetResizeEdge(pos);
        if (edge == _lastEdge) return; // no change, skip cursor creation
        _lastEdge = edge;
        Cursor = edge switch
        {
            WindowEdge.NorthWest or WindowEdge.SouthEast => new Cursor(StandardCursorType.BottomRightCorner),
            WindowEdge.NorthEast or WindowEdge.SouthWest => new Cursor(StandardCursorType.BottomLeftCorner),
            WindowEdge.North or WindowEdge.South => new Cursor(StandardCursorType.SizeNorthSouth),
            WindowEdge.East or WindowEdge.West => new Cursor(StandardCursorType.SizeWestEast),
            _ => Cursor.Default,
        };
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        var edge = GetResizeEdge(pos);
        if (edge is not null)
        {
            BeginResizeDrag(edge.Value, e);
            e.Handled = true;
        }
    }

    private WindowEdge? GetResizeEdge(Point pos)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var left = pos.X < ResizeGrip;
        var right = pos.X > w - ResizeGrip;
        var top = pos.Y < ResizeGrip;
        var bottom = pos.Y > h - ResizeGrip;

        if (top && left) return WindowEdge.NorthWest;
        if (top && right) return WindowEdge.NorthEast;
        if (bottom && left) return WindowEdge.SouthWest;
        if (bottom && right) return WindowEdge.SouthEast;
        if (top) return WindowEdge.North;
        if (bottom) return WindowEdge.South;
        if (left) return WindowEdge.West;
        if (right) return WindowEdge.East;
        return null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
