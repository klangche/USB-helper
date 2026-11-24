using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
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

    private static MissingDevice Kept(UsbNode node, string portId) => new(node, portId, portId, []);

    // A dropped dock (#212): its two disconnected halves, kept at the two halves of the port, are one hub.
    [Fact]
    public void ADroppedUsb3HubIsStillOneHub()
    {
        var box = Guid.NewGuid();
        var tree = Tree(Port("ROOT|ss1", 1, UsbBus.Usb3) with { CompanionPortId = "ROOT|hs2" }, Port("ROOT|hs2", 2, UsbBus.Usb2));
        var layout = PortTree.Build(tree, missing: [Kept(Hub("HUB3") with { ContainerId = box }, "ROOT|ss1"), Kept(Hub("HUB2") with { ContainerId = box }, "ROOT|hs2")]);

        Assert.Equal("HUB3 + HUB2", layout.MissingNames["HUB3"]);
        Assert.Contains("HUB2", layout.HiddenMissing);
        Assert.Equal("ROOT|hs2", layout.OtherHalves["HUB3"].PortId);
    }

    // An internal port keeps its place in the built-in group with a disconnected device or a problem, and a built-in
    // device on a user-connectable port stays built in when it drops.
    [Fact]
    public void ABuiltInPortKeepsItsPlaceWhenItsDeviceDrops()
    {
        var tree = Tree(
            Port("ROOT|p7", 7, UsbBus.Usb2) with { UserConnectable = false },
            Port("ROOT|p9", 9, UsbBus.Usb2) with { UserConnectable = false, Problem = "Could not read the port." },
            Port("ROOT|p10", 10, UsbBus.Usb2));
        var layout = PortTree.Build(tree, missing:
            [Kept(Device("WEBCAM") with { InLocalMachine = true }, "ROOT|p7"), Kept(Device("BLUETOOTH") with { InLocalMachine = true }, "ROOT|p10")]);

        var builtIn = Assert.Single(layout.Groups);
        Assert.Equal(PortGroupKind.BuiltIn, builtIn.Kind);
        Assert.Equal(["ROOT|p7", "ROOT|p9", "ROOT|p10"], builtIn.Nodes.Select(node => node.Id));
        Assert.Equal(UsbBus.Usb2, layout.Buses["WEBCAM"]); // A disconnected device names its port's bus too.
    }

    // The Asus: the USB 2 halves on one controller, the USB 3 halves and the router on another. A mouse in the second
    // USB-C port's USB 2 half does not put that port on the router's controller.
    [Fact]
    public void TheRouterStaysOnItsPortWhenAnotherPortsOtherHalfIsInUse()
    {
        var dock3 = Hub("DOCK3", Port("DOCK3|p1", 1, UsbBus.Usb3));
        var dock2 = Hub("DOCK2", Port("DOCK2|p1", 1, UsbBus.Usb2, Device("HID")));
        IReadOnlyList<UsbNode> tree =
        [
            new("PCH", UsbNodeKind.Controller, "PCH", [Hub("ROOT2",
                Port("ROOT2|p2", 2, UsbBus.Usb2, dock2) with { CompanionPortId = "ROOT3|p1" },
                Port("ROOT2|p3", 3, UsbBus.Usb2, Device("MOUSE")) with { CompanionPortId = "ROOT3|p2" })]),
            new("TYPEC", UsbNodeKind.Controller, "Type-C", [Hub("ROOT3",
                Port("ROOT3|p1", 1, UsbBus.Usb3, dock3) with { CompanionPortId = "ROOT2|p2" },
                Port("ROOT3|p2", 2, UsbBus.Usb3) with { CompanionPortId = "ROOT2|p3" })]) { Usb4Routers = 1, Usb4Link = "USB4 link: PS9010" },
        ];

        var layout = PortTree.Build(tree);

        Assert.Equal(["usb4|TYPEC", null], layout.Groups.Select(group => group.Router?.Id));
        Assert.DoesNotContain(layout.Groups, group => group.Kind == PortGroupKind.Other);
    }

    // With companions named, hub ports pair only by them: the same port number is no pair then.
    [Fact]
    public void HubPortsPairByNumberOnlyWhereNoCompanionIsNamed()
    {
        var usb3 = Hub("HUB3", Port("HUB3|p1", 1, UsbBus.Usb3, Hub("A3", Port("A3|p1", 1, UsbBus.Usb3, Device("LAN")))) with { CompanionPortId = "HUB2|p4" });
        var usb2 = Hub("HUB2", Port("HUB2|p1", 1, UsbBus.Usb2, Hub("B2", Port("B2|p1", 1, UsbBus.Usb2, Device("KEYBOARD")))), Port("HUB2|p4", 4, UsbBus.Usb2));
        var layout = PortTree.Build(Tree(
            Port("ROOT|ss1", 1, UsbBus.Usb3, usb3) with { CompanionPortId = "ROOT|hs2" },
            Port("ROOT|hs2", 2, UsbBus.Usb2, usb2)));

        Assert.Equal(["HUB3"], layout.OtherHalves.Keys); // A3 and B2 are on different connectors.
    }
}
