using Avalonia;
using Avalonia.Controls;

namespace UsbHelper.App;

/// <summary>
/// Gives its child the width there is, down to <see cref="MinShownWidth"/>; with less it takes no space and the child is
/// not shown. The top bar's company name (#229) uses it to hide before anything else has to shrink.
/// </summary>
public sealed class HideWhenNarrow : Decorator
{
    private bool shown;

    public double MinShownWidth { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        shown = availableSize.Width >= MinShownWidth;
        return shown ? base.MeasureOverride(availableSize) : default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (shown)
        {
            return base.ArrangeOverride(finalSize);
        }
        Child?.Arrange(default);
        return default;
    }
}
