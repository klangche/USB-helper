using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Topology;

public class TopologyCountsTests
{
    private static IReadOnlyList<UsbNode> Machine(params UsbNode?[] rootPorts) => [Controller("C1", Hub("ROOT", rootPorts))];

    [Fact]
    public void DeviceOnRootPortIsTierTwo()
    {
        var tree = Machine(Device("MOUSE"));

        Assert.Equal(new ConnectionDepth(2), TopologyCounts.Depths(tree)["MOUSE"]);
        Assert.Equal(0, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void OwnersExampleDockMonitorKeyboard()
    {
        // Root port, dock hub, monitor hub, keyboard: tier 4, 2 hubs (decision in #2).
        var tree = Machine(Hub("DOCK", Hub("MONITOR", Device("KEYBOARD"))));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(4), depths["KEYBOARD"]);
        Assert.Equal(new ConnectionDepth(3), depths["MONITOR"]);
        Assert.Equal(new ConnectionDepth(2), depths["DOCK"]);
        Assert.Equal(2, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void LongestChainAllowedByUsb2ReachesTierSeven()
    {
        var tree = Machine(Hub("H1", Hub("H2", Hub("H3", Hub("H4", Hub("H5", Device("DEVICE")))))));

        Assert.Equal(new ConnectionDepth(7), TopologyCounts.Depths(tree)["DEVICE"]);
        Assert.Equal(5, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void DockWithInternalHubsCountsEachHub()
    {
        // A dock that Windows reports as a USB 3 hub with a USB 2 hub behind it, plus the monitor's hub.
        var tree = Machine(Hub("DOCK-USB3", Hub("DOCK-USB2", Device("CAMERA"), Hub("MONITOR", Device("HEADSET")))));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(4), depths["CAMERA"]);
        Assert.Equal(new ConnectionDepth(5), depths["HEADSET"]);
        Assert.Equal(3, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void BranchesAreCountedIndependently()
    {
        var tree = Machine(Hub("HUB-A", Device("A1"), Hub("HUB-A2", Device("A2"))), Device("B"), Hub("HUB-C", Device("C1")));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(3), depths["A1"]);
        Assert.Equal(new ConnectionDepth(4), depths["A2"]);
        Assert.Equal(new ConnectionDepth(2), depths["B"]);
        Assert.Equal(new ConnectionDepth(3), depths["C1"]);
        Assert.Equal(3, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void HubsUnderEveryControllerCountTowardTheTotal()
    {
        IReadOnlyList<UsbNode> tree =
        [
            Controller("C1", Hub("ROOT1", Hub("HUB1", Device("D1")))),
            Controller("C2", Hub("ROOT2", Hub("HUB2", Device("D2")))),
        ];

        Assert.Equal(2, TopologyCounts.HubCount(tree));
        Assert.Equal(new ConnectionDepth(3), TopologyCounts.Depths(tree)["D2"]);
    }

    // A laptop with a built-in hub (webcam, Bluetooth) on root port 1; its own sockets are ports 2 and 3 of that hub.
    private static IReadOnlyList<UsbNode> Laptop(UsbNode? socket2 = null, UsbNode? socket3 = null) =>
        [Controller("C1", Hub("ROOT", Hub("INTERNAL", Device("WEBCAM"), socket2, socket3).Connectable(false, true, true)).Connectable(false))];

    [Fact]
    public void DeviceInLaptopPortBehindBuiltInHubIsTierTwo()
    {
        var tree = Laptop(Device("MOUSE"));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(2), depths["MOUSE"]);
        Assert.Equal(new ConnectionDepth(2), depths["WEBCAM"]);
        Assert.DoesNotContain("INTERNAL", depths.Keys);
        Assert.Equal(0, TopologyCounts.HubCount(tree));
        Assert.Empty(TopologyCounts.PossiblyBuiltIn(tree));
    }

    [Fact]
    public void OwnersExampleCountsFromTheLaptopPort()
    {
        var tree = Laptop(Hub("DOCK", Hub("MONITOR", Device("KEYBOARD"))), Device("STICK"));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(4), depths["KEYBOARD"]);
        Assert.Equal(new ConnectionDepth(2), depths["DOCK"]);
        Assert.Equal(new ConnectionDepth(2), depths["STICK"]);
        Assert.Equal(2, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void HubOnFixedPortInsideADockStillCounts()
    {
        // Only hubs before the first user-connectable port are built into the computer; a dock's own hubs count.
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Hub("DOCK-USB3", Hub("DOCK-USB2", Device("CAMERA"))).Connectable(false)).Connectable(true))];

        Assert.Equal(new ConnectionDepth(4), TopologyCounts.Depths(tree)["CAMERA"]);
        Assert.Equal(2, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void HubOnPortWindowsSaysNothingAboutCountsAsBeforeAndIsListed()
    {
        // Root port 1: Windows does not say; root port 2: a laptop socket. Hubs behind a counted hub are not listed.
        var rootHub = Hub("ROOT", Hub("UNSURE", Device("CAMERA")), Hub("DOCK", Hub("MONITOR", Device("KEYBOARD"))));
        IReadOnlyList<UsbNode> unmarked = [Controller("C1", rootHub)];
        IReadOnlyList<UsbNode> tree = [Controller("C1", rootHub.Connectable(null, true))];

        Assert.Equal(new ConnectionDepth(3), TopologyCounts.Depths(tree)["CAMERA"]);
        Assert.Equal(3, TopologyCounts.HubCount(tree));
        Assert.Equal(["UNSURE"], TopologyCounts.PossiblyBuiltIn(tree).Select(hub => hub.Id));
        Assert.Equal(["UNSURE", "DOCK"], TopologyCounts.PossiblyBuiltIn(unmarked).Select(hub => hub.Id));
    }

    [Fact]
    public void MarkAddsTiersToEverythingBehindThePortAndHubs()
    {
        // Root port 1: an invisible active cable (+2), then a dock with a keyboard; root port 2: a mouse.
        var tree = Machine(Hub("DOCK", Device("KEYBOARD")), Device("MOUSE"));
        var marks = new Dictionary<string, int> { ["ROOT|port1"] = 2 };

        var depths = TopologyCounts.Depths(tree, marks);

        Assert.Equal(new ConnectionDepth(4), depths["DOCK"]);
        Assert.Equal(new ConnectionDepth(5), depths["KEYBOARD"]);
        Assert.Equal(new ConnectionDepth(2), depths["MOUSE"]);
        Assert.Equal(1, TopologyCounts.HubCount(tree));
        Assert.Equal(3, TopologyCounts.HubCount(tree, marks)); // The dock and the 2 hubs the mark adds (#297).
    }

    [Fact]
    public void MarksAlongThePathAddUp()
    {
        var tree = Machine(Hub("DOCK", Hub("MONITOR", Device("KEYBOARD")), Device("CAMERA")));
        var marks = new Dictionary<string, int> { ["ROOT|port1"] = 1, ["DOCK|port1"] = 2 };

        var depths = TopologyCounts.Depths(tree, marks);

        Assert.Equal(new ConnectionDepth(3), depths["DOCK"]);
        Assert.Equal(new ConnectionDepth(6), depths["MONITOR"]);
        Assert.Equal(new ConnectionDepth(7), depths["KEYBOARD"]);
        Assert.Equal(new ConnectionDepth(4), depths["CAMERA"]);
        Assert.Equal(5, TopologyCounts.HubCount(tree, marks)); // 2 hubs, 1 and 2 from the marks.
    }

    [Fact]
    public void MarkOnLaptopSocketBehindBuiltInHubCounts()
    {
        var tree = Laptop(Device("MOUSE"), Device("STICK"));
        var marks = new Dictionary<string, int> { ["INTERNAL|port2"] = 1 };

        var depths = TopologyCounts.Depths(tree, marks);

        Assert.Equal(new ConnectionDepth(3), depths["MOUSE"]);
        Assert.Equal(new ConnectionDepth(2), depths["STICK"]);
        Assert.Equal(new ConnectionDepth(2), depths["WEBCAM"]);
    }

    [Fact]
    public void MarkBeforeBuiltInHubCountsForEverythingBehindIt()
    {
        var tree = Laptop(Device("MOUSE"));
        var marks = new Dictionary<string, int> { ["ROOT|port1"] = 1 };

        var depths = TopologyCounts.Depths(tree, marks);

        Assert.Equal(new ConnectionDepth(3), depths["MOUSE"]);
        Assert.Equal(new ConnectionDepth(3), depths["WEBCAM"]);
        Assert.DoesNotContain("INTERNAL", depths.Keys);
    }

    [Fact]
    public void UnreadableHubCountsAsOneHubWithNothingBelow()
    {
        var unreadable = new UsbNode("HUB", UsbNodeKind.Hub, "HUB", []) { Problem = "Could not read the hub." };
        var tree = Machine(unreadable);

        Assert.Equal(1, TopologyCounts.HubCount(tree));
        Assert.Equal(["HUB"], TopologyCounts.Depths(tree).Keys);
        Assert.Equal(new ConnectionDepth(2), TopologyCounts.Depths(tree)["HUB"]);
        Assert.Equal(["HUB"], TopologyCounts.Unreadable(tree).Select(node => node.Id));
    }

    [Fact]
    public void ControllerWithoutRootHubAndFailedEmptyPortAreUnreadable()
    {
        var failedPort = Port("ROOT", 2, null) with { Problem = "Could not read the port." };
        IReadOnlyList<UsbNode> tree =
        [
            new UsbNode("C1", UsbNodeKind.Controller, "C1", [new UsbNode("ROOT", UsbNodeKind.Hub, "ROOT", [Port("ROOT", 1, Device("MOUSE")), failedPort])]),
            new UsbNode("C2", UsbNodeKind.Controller, "C2", []),
        ];

        Assert.Equal(["ROOT|port2", "C2"], TopologyCounts.Unreadable(tree).Select(node => node.Id));
        Assert.Empty(TopologyCounts.Unreadable(Machine(Device("MOUSE"), null)));
    }

    // #185: Windows could not identify the device on a port that failed enumeration, so it may be a hub.
    [Fact]
    public void UnidentifiedDeviceMakesTheCountsMinimums()
    {
        var failed = Device(@"USB\VID_0000&PID_0002\5&1", problem: "Windows stopped this device because it reported problems (code 43).");
        var placeholder = Device("ROOT|port2|device");

        Assert.Equal([failed.Id], TopologyCounts.Unreadable(Machine(failed)).Select(node => node.Id));
        Assert.Equal([placeholder.Id], TopologyCounts.Unreadable(Machine(placeholder)).Select(node => node.Id));
    }

    [Fact]
    public void IdentifiedDeviceWithAProblemIsReadable()
    {
        var known = Device("CAMERA", problem: "The drivers for this device are not installed (code 28).") with { VendorId = 0x2E1A, ProductId = 0x4C06 };

        Assert.Empty(TopologyCounts.Unreadable(Machine(known, Device("MOUSE"))));
    }

    [Fact]
    public void RootHubsAndControllersHaveNoDepth()
    {
        var depths = TopologyCounts.Depths(Machine(Device("MOUSE")));

        Assert.DoesNotContain("ROOT", depths.Keys);
        Assert.DoesNotContain("C1", depths.Keys);
    }

    [Fact]
    public void AddingDeviceChangesOnlyItsBranch()
    {
        var before = Machine(Hub("HUB", Device("KEYBOARD"), null), Device("MOUSE"));
        var change = new UsbChange(UsbChangeKind.Added, "HUB", Port("HUB", 2, Hub("SECOND", Device("STICK"))));

        var after = UsbTree.Apply(before, [change]);
        var oldDepths = TopologyCounts.Depths(before);
        var newDepths = TopologyCounts.Depths(after);

        Assert.Equal(["SECOND", "STICK"], newDepths.Keys.Except(oldDepths.Keys).Order());
        Assert.All(oldDepths, entry => Assert.Equal(entry.Value, newDepths[entry.Key]));
        Assert.Same(UsbTree.Find(before, "MOUSE"), UsbTree.Find(after, "MOUSE"));
        Assert.Equal(new ConnectionDepth(4), newDepths["STICK"]);
        Assert.Equal(2, TopologyCounts.HubCount(after));
    }

    [Fact]
    public void RemovingHubRemovesOnlyItsBranch()
    {
        var before = Machine(Hub("HUB", Device("KEYBOARD")), Device("MOUSE"));
        var change = new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 1, null));

        var after = UsbTree.Apply(before, [change]);

        Assert.Equal(["MOUSE"], TopologyCounts.Depths(after).Keys);
        Assert.Equal(0, TopologyCounts.HubCount(after));
    }

    [Fact]
    public void TesterMacIsCountedPerBus()
    {
        // #195 check: the Mac16,13 tester log's USB 2 side has 6 hubs, tier 5; its USB 3 side 2 hubs, tier 3.
        var counts = TopologyCounts.PerBus(TesterMac());

        Assert.Equal(new BusCounts(6, 5), counts.Usb2);
        Assert.Equal(new BusCounts(2, 3), counts.Usb3);
        Assert.False(counts.BusUnknown);
        Assert.True(counts.AnyBusKnown);
        Assert.Equal(8, TopologyCounts.HubCount(TesterMac()));
        Assert.Equal(UsbBus.Usb3, TopologyCounts.Buses(TesterMac())["S2"]);
        Assert.Equal(UsbBus.Usb2, TopologyCounts.Buses(TesterMac())["HEADSET"]);
    }

    [Fact]
    public void BothHalvesOfOneUsb3HubCountOncePerBus()
    {
        // A USB 3 hub is a USB 2 hub on the USB 2 port and a SuperSpeed hub on the USB 3 port of one connector.
        IReadOnlyList<UsbNode> tree = [Controller("C1", RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK"))))];

        var counts = TopologyCounts.PerBus(tree);

        Assert.Equal(new BusCounts(1, 3), counts.Usb2);
        Assert.Equal(new BusCounts(1, 3), counts.Usb3);
    }

    [Fact]
    public void HubOnPortWithUnknownBusIsInNeitherBusAndMakesCountsMinimums()
    {
        var root = RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK")));
        root = root with { Children = [.. root.Children, Port("ROOT", 3, Hub("UNKNOWN-BUS", Device("MOUSE")))] };
        IReadOnlyList<UsbNode> tree = [Controller("C1", root)];

        var counts = TopologyCounts.PerBus(tree);

        Assert.Equal(1, counts.Usb2.Hubs);
        Assert.Equal(1, counts.Usb3.Hubs);
        Assert.True(counts.BusUnknown);
        Assert.Null(TopologyCounts.Buses(tree)["UNKNOWN-BUS"]);
    }

    [Fact]
    public void WithoutAnyKnownBusTheHubLimitCountsAllHubsAsBefore()
    {
        var tree = Machine(Hub("DOCK", Hub("MONITOR", Device("KEYBOARD"))));

        Assert.False(TopologyCounts.PerBus(tree).AnyBusKnown);
        Assert.True(TopologyCounts.PerBus(tree).BusUnknown);
    }

    [Fact]
    public void CableMarkDeepensAndAddsHubsOnlyOnItsOwnBus()
    {
        var tree = TesterMac();
        var usb3RootPort = tree[0].Children[0].Children[1].Id;
        var marks = new Dictionary<string, int> { [usb3RootPort] = 2 };

        var counts = TopologyCounts.PerBus(tree, marks);

        Assert.Equal(new BusCounts(6, 5), counts.Usb2);
        Assert.Equal(new BusCounts(4, 5), counts.Usb3);
        Assert.Equal(10, TopologyCounts.HubCount(tree, marks));
    }

    // #297: a mark of N adds N tiers and min(N, 2) hubs on its port's bus.
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 3, 2)]
    [InlineData(5, 5, 2)]
    public void MarkAddsItsTiersAndAtMostTwoHubsOnItsBus(int mark, int tiers, int hubs)
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK"))))];
        var marks = new Dictionary<string, int> { ["ROOT|port1"] = mark };

        var counts = TopologyCounts.PerBus(tree, marks);

        Assert.Equal(new BusCounts(1 + hubs, 3 + tiers), counts.Usb2);
        Assert.Equal(new BusCounts(1, 3), counts.Usb3);
        Assert.Equal(new ConnectionDepth(3 + tiers), TopologyCounts.Depths(tree, marks)["KEYBOARD"]);
        Assert.Equal(2 + hubs, TopologyCounts.HubCount(tree, marks));
        Assert.Equal(hubs, TopologyCounts.MarkHubs(mark));
    }

    [Fact]
    public void MarkOnPortWithUnknownBusAddsHubsToTheTotalOnly()
    {
        var root = RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK")));
        root = root with { Children = [.. root.Children, Port("ROOT", 3, Device("MOUSE"))] };
        IReadOnlyList<UsbNode> tree = [Controller("C1", root)];
        var marks = new Dictionary<string, int> { ["ROOT|port3"] = 3 };

        var counts = TopologyCounts.PerBus(tree, marks);

        Assert.Equal(1, counts.Usb2.Hubs);
        Assert.Equal(1, counts.Usb3.Hubs);
        Assert.True(counts.BusUnknown);
        Assert.Equal(4, TopologyCounts.HubCount(tree, marks));
        Assert.Equal(new ConnectionDepth(5), TopologyCounts.Depths(tree, marks)["MOUSE"]);
    }

    [Fact]
    public void MarkOnPortWithNothingOnItAddsNothing()
    {
        var tree = Machine(Hub("HUB", Device("KEYBOARD"), null));
        var marks = new Dictionary<string, int> { ["HUB|port2"] = 2 };

        Assert.Equal(1, TopologyCounts.HubCount(tree, marks));
        Assert.Equal(new BusCounts(0, 0), TopologyCounts.PerBus(tree, marks).Usb2);
    }

    // #298: the hubs shown are the sum over the buses, "Hubs 2 (1+1+0)" for one USB 3 hub: USB 2, USB 3, USB 4.
    [Fact]
    public void UsbThreeHubShowsOneHubOnEachOfItsBuses()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK"))))];

        var counts = TopologyCounts.PerBus(tree);

        Assert.Equal(2, counts.Hubs);
        Assert.Equal("2 (1+1+0)", counts.HubsText);
        Assert.Equal(new BusCounts(0, 0), counts.Usb4); // Not detected yet (#267).
        Assert.Equal(counts.Hubs, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void UsbTwoOnlyHubShowsOnUsbTwoOnly()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Device("DISK")))];

        Assert.Equal("1 (1+0+0)", TopologyCounts.PerBus(tree).HubsText);
    }

    [Fact]
    public void HubOnUnknownBusIsInTheTotalOnly()
    {
        var root = RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK")));
        root = root with { Children = [.. root.Children, Port("ROOT", 3, Hub("UNKNOWN-BUS", Device("MOUSE")))] };
        IReadOnlyList<UsbNode> tree = [Controller("C1", root)];

        var counts = TopologyCounts.PerBus(tree);

        Assert.Equal(1, counts.UnknownBusHubs);
        Assert.Equal("3 (1+1+0)", counts.HubsText);
        Assert.Equal(3, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void WithoutAnyKnownBusOnlyTheTotalIsShown()
    {
        var tree = Machine(Hub("DOCK", Hub("MONITOR", Device("KEYBOARD"))));

        Assert.Equal("2", TopologyCounts.PerBus(tree).HubsText);
    }

    [Fact]
    public void CableMarkOfThreeOnUsbThreePortAddsTwoHubsOnUsbThree()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK"))))];
        var marks = new Dictionary<string, int> { ["ROOT|port2"] = 3 };

        var counts = TopologyCounts.PerBus(tree, marks);

        Assert.Equal(new BusCounts(3, 6), counts.Usb3);
        Assert.Equal("4 (1+3+0)", counts.HubsText);
    }
}
