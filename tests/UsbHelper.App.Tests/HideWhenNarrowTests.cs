using Avalonia;
using Avalonia.Controls;

namespace UsbHelper.App.Tests;

/// <summary>The top bar's company name (#229): narrowed with the window, hidden below its minimum width.</summary>
public class HideWhenNarrowTests
{
    private static (HideWhenNarrow Box, Border Content) Laid(double available)
    {
        // No width of its own, like the name's text block: 300 wide when it has the room.
        var content = new Border { Child = new Border { Width = 300, Height = 16 } };
        var box = new HideWhenNarrow { MinShownWidth = 80, Child = content };
        box.Measure(new Size(available, 40));
        box.Arrange(new Rect(0, 0, Math.Min(available, box.DesiredSize.Width), 40));
        return (box, content);
    }

    [Fact]
    public void WithRoomTheWholeContentShows()
    {
        var (box, content) = Laid(1000);
        Assert.Equal(new Size(300, 16), box.DesiredSize);
        Assert.Equal(300, content.Bounds.Width);
    }

    [Fact]
    public void ItIsNarrowedDownToTheMinimum()
    {
        Assert.Equal(120, Laid(120).Content.Bounds.Width);
        Assert.Equal(80, Laid(80).Content.Bounds.Width);
    }

    [Fact]
    public void BelowTheMinimumItTakesNoSpace()
    {
        var (box, content) = Laid(79);
        Assert.Equal(default, box.DesiredSize);
        Assert.Equal(default, content.Bounds.Size);
    }
}
