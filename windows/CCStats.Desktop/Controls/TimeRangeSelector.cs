using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CCStats.Desktop.Controls;

public class TimeRangeSelector : UserControl
{
    private static readonly string[] Labels = ["24h", "7d", "30d", "All"];

    public static readonly StyledProperty<int> SelectedRangeProperty =
        AvaloniaProperty.Register<TimeRangeSelector, int>(nameof(SelectedRange), 0);

    public event EventHandler<int>? SelectionChanged;

    private readonly ToggleButton[] _buttons;
    private bool _updating;

    static TimeRangeSelector()
    {
        SelectedRangeProperty.Changed.AddClassHandler<TimeRangeSelector>((s, _) =>
            s.SyncSelection());
    }

    public TimeRangeSelector()
    {
        _buttons = new ToggleButton[Labels.Length];

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
        };

        for (var i = 0; i < Labels.Length; i++)
        {
            var index = i;
            var button = new ToggleButton
            {
                Content = Labels[i],
                FontSize = 12,
                MinWidth = 36,
                MinHeight = 24,
                Padding = new Thickness(8, 2),
                CornerRadius = new Avalonia.CornerRadius(6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };

            button.Click += (_, _) => OnButtonClicked(index);
            _buttons[i] = button;
            panel.Children.Add(button);
        }

        Content = panel;
        SyncSelection();
    }

    public int SelectedRange
    {
        get => GetValue(SelectedRangeProperty);
        set => SetValue(SelectedRangeProperty, value);
    }

    private void OnButtonClicked(int index)
    {
        if (_updating) return;
        SelectedRange = index;
        SelectionChanged?.Invoke(this, index);
    }

    private void SyncSelection()
    {
        _updating = true;
        try
        {
            var selected = SelectedRange;
            for (var i = 0; i < _buttons.Length; i++)
            {
                var isSelected = i == selected;
                _buttons[i].IsChecked = isSelected;

                if (isSelected)
                {
                    _buttons[i].Background = new SolidColorBrush(Color.Parse("#2A5BAA"));
                    _buttons[i].Foreground = new SolidColorBrush(Colors.White);
                    _buttons[i].FontWeight = FontWeight.SemiBold;
                    _buttons[i].BorderBrush = Brushes.Transparent;
                    _buttons[i].BorderThickness = new Thickness(0);
                }
                else
                {
                    _buttons[i].Background = Brushes.Transparent;
                    _buttons[i].Foreground = new SolidColorBrush(HeadroomColors.TextSecondary);
                    _buttons[i].FontWeight = FontWeight.Normal;
                    _buttons[i].BorderBrush = new SolidColorBrush(
                        Color.FromArgb(77, HeadroomColors.TextSecondary.R,
                            HeadroomColors.TextSecondary.G, HeadroomColors.TextSecondary.B));
                    _buttons[i].BorderThickness = new Thickness(1);
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }
}
