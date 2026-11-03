using UsbHelper.Core.Detection;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Detection;

// #143: a monitor is shown under the USB node Windows puts in the same container; never by guessing.
public class MonitorPlacementTests
{
    private static readonly Guid Samsung = new("6f1d2a6e-0000-4000-8000-000000000001");
    private static readonly Guid Other = new("6f1d2a6e-0000-4000-8000-000000000002");

    // A USB-C monitor's built-in hub and the webcam inside it share the monitor's container.
    private static IReadOnlyList<UsbNode> Desk() =>
        [Controller("C1", Hub("ROOT",
            Hub("MONITOR-HUB", Device("MONITOR-CAM") with { ContainerId = Samsung }) with { ContainerId = Samsung },
            Device("BUILTIN-CAM") with { ContainerId = MonitorPlacement.ThisComputer }))];

    [Fact]
    public void AMonitorWithTheSameContainerIdGoesUnderTheHighestUsbNodeInIt()
    {
        DisplayMonitor monitor = new("M1", "LS27A600U", MonitorOutput.DisplayPort) { ContainerId = Samsung };

        var placement = MonitorPlacement.Place(Desk(), [monitor]);

        Assert.Equal([monitor], placement.Linked["MONITOR-HUB"]);
        Assert.Single(placement.Linked);
        Assert.Empty(placement.Unlinked);
    }

    [Fact]
    public void ADifferentOrMissingContainerIdIsNotLinked()
    {
        DisplayMonitor other = new("M1", "Other", MonitorOutput.Hdmi) { ContainerId = Other };
        DisplayMonitor unknown = new("M2", "Unknown", MonitorOutput.Hdmi);

        var placement = MonitorPlacement.Place(Desk(), [other, unknown]);

        Assert.Empty(placement.Linked);
        Assert.Equal([other, unknown], placement.Unlinked);
    }

    // Windows gives every built-in device the computer's own container id: the laptop panel and the built-in webcam
    // share it, but the panel is not inside the webcam.
    [Fact]
    public void TheComputersOwnContainerIdLinksNothing()
    {
        DisplayMonitor panel = new("PANEL", null, MonitorOutput.Internal) { ContainerId = MonitorPlacement.ThisComputer };

        var placement = MonitorPlacement.Place(Desk(), [panel]);

        Assert.Empty(placement.Linked);
        Assert.Equal([panel], placement.Unlinked);
    }
}
