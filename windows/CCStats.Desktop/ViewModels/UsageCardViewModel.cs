using Avalonia.Media;

namespace CCStats.Desktop.ViewModels;

/// <summary>
/// Retained for backwards compatibility but no longer used by the main flyout.
/// The new layout binds directly to MainWindowViewModel properties.
/// </summary>
public sealed class UsageCardViewModel
{
    public required string Label { get; init; }
    public required string HeadroomText { get; init; }
    public required string ResetText { get; init; }
    public required string SupportingText { get; init; }
    public required string SlopeArrow { get; init; }
    public required string StateText { get; init; }
    public required double ProgressValue { get; init; }
    public required IBrush AccentBrush { get; init; }
}
