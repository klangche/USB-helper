using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Topology;

public class TopologyCountsTests
{
    private static IReadOnlyList<UsbNode> Machine(params UsbNode?[] rootPorts) => [Controller("C1", Hub("ROOT", rootPorts))];

    [Fact]
    public void DeviceOnRootPortIsTierTwoWithNoHops()
    {
        var tree = Machine(Device("MOUSE"));

        Assert.Equal(new ConnectionDepth(2, 0), TopologyCounts.Depths(tree)["MOUSE"]);
        Assert.Equal(0, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void OwnersExampleDockMonitorKeyboard()
    {
        // Root port, dock hub, monitor hub, keyboard: tier 4, 2 hops, 2 hubs (decision in #2).
        var tree = Machine(Hub("DOCK", Hub("MONITOR", Device("KEYBOARD"))));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(4, 2), depths["KEYBOARD"]);
        Assert.Equal(new ConnectionDepth(3, 1), depths["MONITOR"]);
        Assert.Equal(new ConnectionDepth(2, 0), depths["DOCK"]);
        Assert.Equal(2, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void LongestChainAllowedByUsb2ReachesTierSeven()
    {
        var tree = Machine(Hub("H1", Hub("H2", Hub("H3", Hub("H4", Hub("H5", Device("DEVICE")))))));

        Assert.Equal(new ConnectionDepth(7, 5), TopologyCounts.Depths(tree)["DEVICE"]);
        Assert.Equal(5, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void DockWithInternalHubsCountsEachHub()
    {
        // A dock that Windows reports as a USB 3 hub with a USB 2 hub behind it, plus the monitor's hub.
        var tree = Machine(Hub("DOCK-USB3", Hub("DOCK-USB2", Device("CAMERA"), Hub("MONITOR", Device("HEADSET")))));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(4, 2), depths["CAMERA"]);
        Assert.Equal(new ConnectionDepth(5, 3), depths["HEADSET"]);
        Assert.Equal(3, TopologyCounts.HubCount(tree));
    }

    [Fact]
    public void BranchesAreCountedIndependently()
    {
        var tree = Machine(Hub("HUB-A", Device("A1"), Hub("HUB-A2", Device("A2"))), Device("B"), Hub("HUB-C", Device("C1")));

        var depths = TopologyCounts.Depths(tree);

        Assert.Equal(new ConnectionDepth(3, 1), depths["A1"]);
        Assert.Equal(new ConnectionDepth(4, 2), depths["A2"]);
        Assert.Equal(new ConnectionDepth(2, 0), depths["B"]);
        Assert.Equal(new ConnectionDepth(3, 1), depths["C1"]);
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
        Assert.Equal(new ConnectionDepth(3, 1), TopologyCounts.Depths(tree)["D2"]);
    }

    [Fact]
    public void UnreadableHubCountsAsOneHubWithNothingBelow()
    {
        var unreadable = new UsbNode("HUB", UsbNodeKind.Hub, "HUB", []) { Problem = "Could not read the hub." };
        var tree = Machine(unreadable);

        Assert.Equal(1, TopologyCounts.HubCount(tree));
        Assert.Equal(["HUB"], TopologyCounts.Depths(tree).Keys);
        Assert.Equal(new ConnectionDepth(2, 0), TopologyCounts.Depths(tree)["HUB"]);
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
        Assert.Equal(new ConnectionDepth(4, 2), newDepths["STICK"]);
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
}
