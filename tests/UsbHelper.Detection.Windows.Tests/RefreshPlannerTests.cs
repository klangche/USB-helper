using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows.Tests;

/// <summary>Event sequences as Windows reports them for plugging and unplugging (DEVICEINSTANCE notifications).</summary>
public class RefreshPlannerTests
{
    private const string RootHub = @"USB\ROOT_HUB30\4&1A2B&0&0";
    private const string ExternalHub = @"USB\VID_05E3&PID_0610\5&3C4D&0&2";
    private const string Keyboard = @"USB\VID_046D&PID_C31C\6&5E6F&0&1";
    private const string Mouse = @"USB\VID_046D&PID_C52B\5&7A8B&0&1";

    // Controller > root hub (port 1: mouse, port 2: external hub (port 1: keyboard, port 2: empty)).
    private static readonly IReadOnlyList<UsbNode> Tree =
    [
        Node(@"PCI\VEN_8086&DEV_A0ED\3&1", UsbNodeKind.Controller,
            Hub(RootHub,
                Node(Mouse, UsbNodeKind.Device),
                Hub(ExternalHub, Node(Keyboard, UsbNodeKind.Device), null))),
    ];

    private static UsbNode Node(string id, UsbNodeKind kind, params UsbNode[] children) => new(id, kind, id, children);

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) =>
            new UsbNode($"{id}|port{index + 1}", UsbNodeKind.Port, $"Port {index + 1}", child == null ? [] : [child])).ToList());

    private static DeviceEvent Arrived(string id, string? parent) => new(id, false, parent);

    private static DeviceEvent Removed(string id) => new(id, true, null);

    [Fact]
    public void UnpluggingDeviceReadsItsHub()
    {
        var plan = RefreshPlanner.Plan(Tree, [Removed(Mouse + "&MI_00"), Removed(Mouse + "&MI_01"), Removed(Mouse)]);

        Assert.False(plan.Full);
        Assert.Equal([RootHub], plan.HubIds);
    }

    [Fact]
    public void PluggingDeviceIntoHubReadsThatHubOnly()
    {
        const string stick = @"USB\VID_0781&PID_5581\4C530001";

        var plan = RefreshPlanner.Plan(Tree, [Arrived(stick, ExternalHub), Arrived(stick, ExternalHub), Arrived(@"USBSTOR\DISK&VEN_SANDISK\4C530001&0", stick)]);

        Assert.False(plan.Full);
        Assert.Equal([ExternalHub], plan.HubIds);
    }

    [Fact]
    public void InterfacesOfNewCompositeDeviceAreCoveredByTheDevice()
    {
        const string headset = @"USB\VID_0B0E&PID_245D\7&1";

        var plan = RefreshPlanner.Plan(Tree, [Arrived(headset, ExternalHub), Arrived(headset + "&MI_00", headset), Arrived(headset + "&MI_03", headset)]);

        Assert.Equal([ExternalHub], plan.HubIds);
    }

    [Fact]
    public void InterfaceOfKnownDeviceReadsTheDevicesHub()
    {
        var plan = RefreshPlanner.Plan(Tree, [Arrived(Keyboard + "&MI_01", Keyboard)]);

        Assert.Equal([ExternalHub], plan.HubIds);
    }

    [Fact]
    public void ProblemOnKnownDeviceReadsItsHub()
    {
        var plan = RefreshPlanner.Plan(Tree, [Arrived(Keyboard, ExternalHub)]);

        Assert.Equal([ExternalHub], plan.HubIds);
    }

    [Fact]
    public void UnpluggingHubReadsOnlyTheHubAbove()
    {
        var plan = RefreshPlanner.Plan(Tree, [Removed(Keyboard), Removed(ExternalHub)]);

        Assert.Equal([RootHub], plan.HubIds);
    }

    [Fact]
    public void RemovalOfUnknownDeviceNeedsNoRead()
    {
        var plan = RefreshPlanner.Plan(Tree, [Removed(@"USB\VID_1234&PID_5678\9&9")]);

        Assert.False(plan.Full);
        Assert.Empty(plan.HubIds);
    }

    [Fact]
    public void DeviceWithUnknownParentReadsEverything()
    {
        // For example a Thunderbolt dock: its new USB controller and root hub are not in the tree yet.
        var plan = RefreshPlanner.Plan(Tree, [Arrived(@"USB\VID_2109&PID_0817\8&1", @"USB\ROOT_HUB30\9&DOCK&0&0")]);

        Assert.True(plan.Full);
    }

    [Fact]
    public void RootHubEventReadsEverything()
    {
        Assert.True(RefreshPlanner.Plan(Tree, [Arrived(RootHub, @"PCI\VEN_8086&DEV_A0ED\3&1")]).Full);
    }
}
