using Avalonia.Controls;
using Avalonia.Input;

namespace CCStats.Desktop.Views;

public partial class AnalyticsWindow : Window
{
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
    }
}
