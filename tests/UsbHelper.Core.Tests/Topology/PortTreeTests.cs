using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Tests.Topology;

// #370: the tree by the computer's physical ports.
public class PortTreeTests
{
    private static UsbNode Port(string id, int number, UsbBus? bus, params UsbNode[] children) =>
        new(id, UsbNodeKind.Port, $"Port {number}", children) { PortNumber = number, Bus = bus, UserConnectable = true };

    private static UsbNode Hub(string id, params UsbNode[] ports) => new(id, UsbNodeKind.Hub, id, ports);

    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static IReadOnlyList<UsbNode> Tree(params UsbNode[] rootPorts) =>
        [new("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", rootPorts)])];

    // The two halves of one connector, with a hub on each: the USB 3 half and the USB 2 half by the companion Windows names.
    private static IReadOnlyList<UsbNode> Dock(Guid? usb3Box, Guid? usb2Box, UsbNode? usb2Child = null)
    {
        var usb3 = Hub("HUB3", Port("HUB3|p1", 1, UsbBus.Usb3, Device("DISK")) with { CompanionPortId = "HUB2|p3" }) with { ContainerId = usb3Box };
        var usb2 = Hub("HUB2", Port("HUB2|p3", 3, UsbBus.Usb2, usb2Child ?? Device("MOUSE"))) with { ContainerId = usb2Box };
        return Tree(
            Port("ROOT|ss1", 1, UsbBus.Usb3, usb3) with { CompanionPortId = "ROOT|hs2" },
            Port("ROOT|hs2", 2, UsbBus.Usb2, usb2) with { CompanionPortId = "ROOT|ss1" });
    }

    [Fact]
    public void TheHalvesOfAHubAreOneHubOnTheUsb3HalfWithThePortsOfBoth()
    {
        var box = Guid.NewGuid();
        var layout = PortTree.Build(Dock(box, box));

        var port = Assert.Single(layout.Groups);
        Assert.Equal(PortGroupKind.Port, port.Kind);
        Assert.Equal(["ROOT|ss1", "ROOT|hs2"], port.Nodes.Select(node => node.Id));
        var hub = Assert.Single(port.Nodes[0].Children);
        Assert.Equal("HUB3 + HUB2", hub.Name);
        Assert.Empty(port.Nodes[1].Children); // The USB 2 half is in the hub above.
        Assert.Equal(["HUB3|p1", "HUB2|p3"], hub.Children.Select(child => child.Id)); // By port number, USB 3 first.
        Assert.Equal(("HUB2", "ROOT|hs2"), (layout.OtherHalves["HUB3"].Hub.Id, layout.OtherHalves["HUB3"].PortId));
        Assert.Equal(UsbBus.Usb3, layout.Buses["DISK"]);
        Assert.Equal(UsbBus.Usb2, layout.Buses["MOUSE"]);
        Assert.False(layout.Buses.ContainsKey("HUB3"));
        Assert.True(layout.PortHalves.SetEquals(["ROOT|ss1", "ROOT|hs2"]));
    }

    // Two hubs in different containers are two boxes, whatever ports they are on; a hub without a container may be either.
    [Fact]
    public void HubsInDifferentContainersStayTwo()
    {
        var layout = PortTree.Build(Dock(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Empty(layout.OtherHalves);
        Assert.Equal(["HUB3", "HUB2"], Assert.Single(layout.Groups).Nodes.SelectMany(node => node.Children).Select(node => node.Id));
        Assert.Single(PortTree.Build(Dock(Guid.NewGuid(), null)).OtherHalves);
    }

    // A hub behind the companion USB 2 port is one with the hub on the USB 3 port, as at the top.
    [Fact]
    public void AHubBelowIsMergedOnTheCompanionPort()
    {
        var usb3 = Hub("HUB3", Port("HUB3|p1", 1, UsbBus.Usb3, Hub("INNER3", Port("INNER3|p1", 1, UsbBus.Usb3, Device("LAN")))) with { CompanionPortId = "HUB2|p3" });
        var usb2 = Hub("HUB2", Port("HUB2|p3", 3, UsbBus.Usb2, Hub("INNER2", Port("INNER2|p1", 1, UsbBus.Usb2, Device("WPP20")))));
        var layout = PortTree.Build(Tree(
            Port("ROOT|ss1", 1, UsbBus.Usb3, usb3) with { CompanionPortId = "ROOT|hs2" },
            Port("ROOT|hs2", 2, UsbBus.Usb2, usb2)));

        Assert.Equal(["HUB3", "INNER3"], layout.OtherHalves.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("HUB2|p3", layout.OtherHalves["INNER3"].PortId);
        var inner = layout.Groups[0].Nodes[0].Children[0].Children.Single(port => port.Id == "HUB3|p1").Children.Single();
        Assert.Equal("INNER3 + INNER2", inner.Name);
        Assert.Equal(["LAN", "WPP20"], inner.Children.SelectMany(port => port.Children).Select(node => node.Id));
    }

    // A device in the computer's own container is built in, even on a port Windows calls user-connectable.
    [Fact]
    public void ADeviceInTheComputersContainerIsBuiltIn()
    {
        var layout = PortTree.Build(Tree(Port("ROOT|p1", 1, UsbBus.Usb2, Device("WEBCAM") with { InLocalMachine = true }), Port("ROOT|p2", 2, UsbBus.Usb2, Device("MOUSE"))));

        Assert.Equal([PortGroupKind.Port, PortGroupKind.BuiltIn], layout.Groups.Select(group => group.Kind));
        Assert.Equal(["ROOT|p1"], layout.Groups[1].Nodes.Select(node => node.Id));
        Assert.Equal(["C1"], layout.Groups[1].ControllerIds);
    }

    // Nothing to place, nothing to show: no group for empty ports, built-in devices or unplaced parts.
    [Fact]
    public void AnEmptyComputerHasOnlyItsEmptyPorts()
    {
        var layout = PortTree.Build(Tree(Port("ROOT|p1", 1, UsbBus.Usb2), Port("ROOT|p2", 2, UsbBus.Usb3)));

        Assert.All(layout.Groups, group => Assert.Equal(PortGroupKind.Port, group.Kind));
        Assert.All(layout.Groups, group => Assert.All(group.Nodes, port => Assert.Empty(port.Children)));
    }
}
