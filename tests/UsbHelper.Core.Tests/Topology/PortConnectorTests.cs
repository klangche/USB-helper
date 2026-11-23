using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Topology;

/// <summary>The port hover (#266): what is known about a connector, and nothing guessed.</summary>
public class PortConnectorTests
{
    // One USB-C connector as two root ports: port 1 the USB 2 half, port 2 the USB 3 half, companions; and port 3 with no bus.
    private static IReadOnlyList<UsbNode> Tree(UsbNode? onUsb2, UsbNode? onUsb3, UsbNode? onUnknown = null)
    {
        var root = Hub("ROOT", onUsb2, onUsb3, onUnknown);
        return [Controller("C1", root with
        {
            Children =
            [
                root.Children[0] with { Bus = UsbBus.Usb2, UsbC = true, UserConnectable = true, CompanionPortId = root.Children[1].Id },
                root.Children[1] with { Bus = UsbBus.Usb3, UsbC = true, UserConnectable = true, CompanionPortId = root.Children[0].Id },
                root.Children[2],
            ],
        })];
    }

    [Fact]
    public void AUsb2DeviceUsesThePairOnly()
    {
        var tree = Tree(Device("MOUSE"), null);
        var connector = PortConnector.Of(tree, tree[0].Children[0].Children[0].Id, null)!;

        Assert.True(connector.UsbC);
        Assert.Equal(true, connector.Usb2InUse);
        Assert.Equal(false, connector.Usb3InUse);
        Assert.Equal(tree[0].Children[0].Children[1].Id, connector.Partner!.Id); // The companion, the other half.
        Assert.Equal(PortPlace.ComputerPort, connector.Place);
    }

    [Fact]
    public void AUsb3HubUsesBothHalvesAndThePartnerComesFromThePairing()
    {
        var tree = Tree(Hub("HUB2"), Hub("HUB3"));
        var usb3 = tree[0].Children[0].Children[1].Id;
        var connector = PortConnector.Of(tree, usb3, tree[0].Children[0].Children[0].Id)!;

        Assert.Equal(true, connector.Usb2InUse);
        Assert.Equal(true, connector.Usb3InUse);
        Assert.Equal(UsbBus.Usb3, connector.Bus);
    }

    [Fact]
    public void APortWithAnUnknownBusIsNotDetectedNotGuessed()
    {
        var tree = Tree(null, null, Device("DISK"));
        var connector = PortConnector.Of(tree, tree[0].Children[0].Children[2].Id, null)!;

        Assert.Null(connector.UsbC);
        Assert.Null(connector.Usb2InUse);
        Assert.Null(connector.Usb3InUse);
        Assert.Null(connector.Partner);
        Assert.Equal(PortPlace.NotSaid, connector.Place);
    }

    [Fact]
    public void APortOnAHubIsOnAHubAndThePathRunsFromTheController()
    {
        var tree = Tree(Hub("HUB2", Device("KEYBOARD")), null);
        var hubPort = tree[0].Children[0].Children[0].Children[0].Children[0];

        Assert.Equal(PortPlace.OnHub, PortConnector.Of(tree, hubPort.Id, null)!.Place);
        Assert.Equal(["C1", "ROOT", tree[0].Children[0].Children[0].Id, "HUB2", hubPort.Id], PortConnector.PathTo(tree, hubPort.Id));
        Assert.Null(PortConnector.Of(tree, "NOPE", null));
    }
}
