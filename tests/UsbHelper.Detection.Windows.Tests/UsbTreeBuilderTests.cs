using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Detection.Windows.Tests;

public class UsbTreeBuilderTests
{
    private static readonly RawDevNode ControllerNode = new(@"PCI\VEN_8086&DEV_A0ED\3&11583659&0&A0", "USB 3.1 eXtensible Host Controller", null);
    private static readonly RawDevNode RootHubNode = new(@"USB\ROOT_HUB30\4&1A2B&0&0", "USB Root Hub (USB 3.0)", null);

    private static RawPort EmptyPort(int number) => new(number, 0, false, 0, 0, null, null);

    private static RawPort DevicePort(int number, RawDevNode device) => new(number, 1, false, 0x046D, 0xC52B, device, null);

    private static UsbNode BuildSingle(params RawPort[] rootPorts) =>
        Assert.Single(UsbTreeBuilder.Build([new RawController(ControllerNode, new RawHub(RootHubNode, rootPorts, null), null)]));

    [Fact]
    public void ControllerHasRootHubWithAllPorts()
    {
        var controller = BuildSingle(EmptyPort(1), EmptyPort(2));

        Assert.Equal(UsbNodeKind.Controller, controller.Kind);
        Assert.Equal(ControllerNode.InstanceId, controller.Id);
        Assert.Equal("USB 3.1 eXtensible Host Controller", controller.Name);
        var rootHub = Assert.Single(controller.Children);
        Assert.Equal(UsbNodeKind.Hub, rootHub.Kind);
        Assert.Equal(RootHubNode.InstanceId, rootHub.Id);
        Assert.Equal([1, 2], rootHub.Children.Select(port => port.PortNumber));
        Assert.All(rootHub.Children, port => Assert.Equal(UsbNodeKind.Port, port.Kind));
        Assert.All(rootHub.Children, port => Assert.Empty(port.Children));
    }

    // #193: the bus and the companion port come from the port; the companion's id is its hub's instance id and port.
    [Fact]
    public void PortHasItsBusAndCompanion()
    {
        var usb2 = EmptyPort(1) with { Bus = UsbBus.Usb2, CompanionPortNumber = 13, CompanionHubName = "USB#ROOT_HUB30#4&1a2b&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}" };
        var unknown = EmptyPort(2);

        var ports = BuildSingle(usb2, unknown).Children[0].Children;

        Assert.Equal(UsbBus.Usb2, ports[0].Bus);
        Assert.Equal(RootHubNode.InstanceId + "|port13", ports[0].CompanionPortId);
        Assert.Null(ports[1].Bus);
        Assert.Null(ports[1].CompanionPortId);
    }

    [Fact]
    public void PortIdIsHubIdAndPortNumber()
    {
        var rootHub = BuildSingle(EmptyPort(3)).Children[0];

        Assert.Equal(RootHubNode.InstanceId + "|port3", rootHub.Children[0].Id);
        Assert.Equal("Port 3", rootHub.Children[0].Name);
    }

    [Fact]
    public void ConnectedDeviceUsesItsDeviceNode()
    {
        var mouse = new RawDevNode(@"USB\VID_046D&PID_C52B\5&2B0E5E8&0&1", "USB Composite Device", null);

        var device = Assert.Single(BuildSingle(DevicePort(1, mouse)).Children[0].Children[0].Children);

        Assert.Equal(UsbNodeKind.Device, device.Kind);
        Assert.Equal(mouse.InstanceId, device.Id);
        Assert.Equal("USB Composite Device", device.Name);
        Assert.Equal((ushort)0x046D, device.VendorId);
        Assert.Equal((ushort)0xC52B, device.ProductId);
        Assert.Null(device.Problem);
    }

    [Fact]
    public void DeviceWithTheDisplayLinkVendorIdIsMarkedDisplayLink()
    {
        var adapter = new RawDevNode(@"USB\VID_17E9&PID_6006\5&2B0E5E8&0&2", "DisplayLink USB Device", null);
        var mouse = new RawDevNode(@"USB\VID_046D&PID_C52B\5&2B0E5E8&0&1", "USB Composite Device", null);

        // The vendor id comes from the device descriptor read on the port.
        var ports = BuildSingle(DevicePort(1, mouse), new RawPort(2, 1, false, 0x17E9, 0x6006, adapter, null)).Children[0].Children;

        Assert.False(Assert.Single(ports[0].Children).IsDisplayLink);
        Assert.True(Assert.Single(ports[1].Children).IsDisplayLink);
    }

    // #274: the kind comes from the interface classes in the device's configuration descriptor.
    [Fact]
    public void DeviceKindComesFromItsInterfaceClasses()
    {
        var webcam = new RawDevNode(@"USB\VID_046D&PID_085C\5&2B0E5E8&0&1", "C922 Pro Stream Webcam", null);
        var headset = new RawDevNode(@"USB\VID_046D&PID_0A87\5&2B0E5E8&0&2", "Logitech USB Headset", null);
        var keyboard = new RawDevNode(@"USB\VID_046D&PID_C31C\5&2B0E5E8&0&3", "USB Input Device", null);
        var unreadable = new RawDevNode(@"USB\VID_1234&PID_5678\5&2B0E5E8&0&4", "USB Device", null);

        var ports = BuildSingle(
            DevicePort(1, webcam) with { InterfaceClasses = [0x0E, 0x0E, 0x01, 0x01] },
            DevicePort(2, headset) with { InterfaceClasses = [0x01, 0x01, 0x03] },
            DevicePort(3, keyboard) with { InterfaceClasses = [0x03] },
            DevicePort(4, unreadable)).Children[0].Children;

        Assert.Equal([UsbDeviceKind.CameraAndAudio, UsbDeviceKind.Audio, UsbDeviceKind.Neither, UsbDeviceKind.Unknown],
            ports.Select(port => Assert.Single(port.Children).DeviceKind));
    }

    [Fact]
    public void ADeviceThatFailedEnumerationHasAnUnknownKind()
    {
        var device = Assert.Single(BuildSingle(new RawPort(1, 2, false, 0, 0, null, null)).Children[0].Children[0].Children);

        Assert.Equal(UsbDeviceKind.Unknown, device.DeviceKind);
    }

    [Fact]
    public void HubsPortsAndControllersHaveNoKind()
    {
        var hubNode = new RawDevNode(@"USB\VID_05E3&PID_0610\6&1&0&2", "Generic USB Hub", null);
        var hubPort = new RawPort(2, 1, true, 0x05E3, 0x0610, hubNode, new RawHub(hubNode, [EmptyPort(1)], null)) { InterfaceClasses = [0x09] };
        var unreadHub = new RawPort(3, 1, true, 0x05E3, 0x0610, null, null);

        var controller = BuildSingle(EmptyPort(1), hubPort, unreadHub);

        Assert.Null(controller.DeviceKind);
        Assert.Null(controller.Children[0].DeviceKind);
        Assert.All(controller.Children[0].Children, port => Assert.Null(port.DeviceKind));
        Assert.Null(controller.Children[0].Children[1].Children[0].DeviceKind);
        Assert.Null(controller.Children[0].Children[2].Children[0].DeviceKind);
    }

    [Fact]
    public void PortKeepsWhetherItIsUserConnectable()
    {
        var ports = BuildSingle(EmptyPort(1) with { UserConnectable = true }, EmptyPort(2) with { UserConnectable = false }, EmptyPort(3)).Children[0].Children;

        Assert.Equal([true, false, null], ports.Select(port => port.UserConnectable));
    }

    [Fact]
    public void PortKeepsWhetherItIsUsbC()
    {
        var ports = BuildSingle(EmptyPort(1) with { UsbC = true }, EmptyPort(2) with { UsbC = false }, EmptyPort(3)).Children[0].Children;

        Assert.Equal([true, false, null], ports.Select(port => port.UsbC));
    }

    [Fact]
    public void ExternalHubBecomesHubNodeWithItsPorts()
    {
        var hubNode = new RawDevNode(@"USB\VID_05E3&PID_0610\6&1&0&2", "Generic USB Hub", null);
        var mouse = new RawDevNode(@"USB\VID_046D&PID_C52B\7&1&0&1", "USB Composite Device", null);
        var hubPort = new RawPort(2, 1, true, 0x05E3, 0x0610, hubNode, new RawHub(hubNode, [DevicePort(1, mouse), EmptyPort(2)], null));

        var hub = Assert.Single(BuildSingle(hubPort).Children[0].Children[0].Children);

        Assert.Equal(UsbNodeKind.Hub, hub.Kind);
        Assert.Equal(hubNode.InstanceId, hub.Id);
        Assert.Equal((ushort)0x05E3, hub.VendorId);
        Assert.Equal([hubNode.InstanceId + "|port1", hubNode.InstanceId + "|port2"], hub.Children.Select(port => port.Id));
        Assert.Equal(mouse.InstanceId, hub.Children[0].Children[0].Id);
    }

    [Fact]
    public void HubThatCouldNotBeReadKeepsItsIdentityAndReportsTheError()
    {
        var hubNode = new RawDevNode(@"USB\VID_05E3&PID_0610\6&1&0&2", "Generic USB Hub", null);
        var hubPort = new RawPort(2, 1, true, 0x05E3, 0x0610, hubNode, new RawHub(hubNode, [], "Access is denied."));

        var hub = BuildSingle(hubPort).Children[0].Children[0].Children[0];

        Assert.Equal(hubNode.InstanceId, hub.Id);
        Assert.Empty(hub.Children);
        Assert.Equal("Could not read the hub: Access is denied.", hub.Problem);
    }

    [Fact]
    public void UnreadablePortReportsTheErrorAndKeepsTheOtherPorts()
    {
        var unreadable = new RawPort(1, 0, false, 0, 0, null, null) { Error = "The device is not ready." };
        var mouse = new RawDevNode(@"USB\VID_046D&PID_C52B\5&2B0E5E8&0&2", "USB Composite Device", null);

        var ports = BuildSingle(unreadable, DevicePort(2, mouse)).Children[0].Children;

        Assert.Equal("Could not read the port: The device is not ready.", ports[0].Problem);
        Assert.Empty(ports[0].Children);
        Assert.Equal(mouse.InstanceId, ports[1].Children[0].Id);
    }

    [Fact]
    public void HubReadAgainMatchesTheHubInTheTree()
    {
        var hubNode = new RawDevNode(@"USB\VID_05E3&PID_0610\6&1&0&2", "Generic USB Hub", null);
        var rawHub = new RawHub(hubNode, [EmptyPort(1)], null);
        var inTree = BuildSingle(new RawPort(2, 1, true, 0x05E3, 0x0610, hubNode, rawHub)).Children[0].Children[0].Children[0];

        var again = UsbTreeBuilder.BuildHub(rawHub, inTree);

        Assert.Equal((ushort)0x05E3, again.VendorId);
        Assert.Empty(UsbTreeDiff.CompareHub(inTree, again)!);
    }

    [Fact]
    public void HubReadAgainWithoutDeviceNodeKeepsItsName()
    {
        var inTree = new UsbNode(@"USB\VID_05E3&PID_0610\6&1&0&2", UsbNodeKind.Hub, "Generic USB Hub", [])
        {
            VendorId = 0x05E3,
            ProductId = 0x0610,
        };

        var again = UsbTreeBuilder.BuildHub(new RawHub(null, [], null), inTree);

        Assert.Equal("Generic USB Hub", again.Name);
        Assert.Empty(UsbTreeDiff.CompareHub(inTree, again)!);
    }

    [Theory]
    [InlineData(2, "The device failed enumeration.")]
    [InlineData(3, "The port reports a general failure.")]
    [InlineData(4, "The device caused an overcurrent condition.")]
    [InlineData(5, "Not enough power for the device.")]
    [InlineData(7, "Hubs are nested too deeply.")]
    [InlineData(42, "Unknown connection status 42.")]
    public void ConnectionErrorIsReportedOnThePort(int status, string problem)
    {
        var port = BuildSingle(new RawPort(1, status, false, 0, 0, null, null)).Children[0].Children[0];

        Assert.Equal(problem, port.Problem);
        var device = Assert.Single(port.Children);
        Assert.Equal(RootHubNode.InstanceId + "|port1|device", device.Id);
        Assert.Equal("Unknown USB device", device.Name);
        Assert.Null(device.VendorId);
        Assert.Null(device.ProductId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    public void NormalAndTransientConnectionStatesAreNoProblem(int status)
    {
        var port = BuildSingle(new RawPort(1, status, false, 0, 0, null, null)).Children[0].Children[0];

        Assert.Null(port.Problem);
    }

    [Theory]
    [InlineData(43, "Windows stopped this device because it reported problems (code 43).")]
    [InlineData(28, "The drivers for this device are not installed (code 28).")]
    [InlineData(31, "Windows reports a problem with this device (code 31).")]
    public void DeviceProblemCodeIsReportedOnTheDevice(int code, string problem)
    {
        var device = BuildSingle(DevicePort(1, new RawDevNode(@"USB\VID_1234&PID_5678\1", "Broken", code))).Children[0].Children[0].Children[0];

        Assert.Equal(problem, device.Problem);
    }

    [Fact]
    public void ControllerWithUnreadableRootHubReportsTheError()
    {
        var controller = Assert.Single(UsbTreeBuilder.Build([new RawController(ControllerNode, null, "Access is denied.")]));

        Assert.Empty(controller.Children);
        Assert.Equal("Could not read the root hub: Access is denied.", controller.Problem);
    }

    [Fact]
    public void TreeHasNoSpeedOrBandwidthFields()
    {
        var properties = typeof(UsbNode).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(properties, name => name.Contains("Speed") || name.Contains("Bandwidth"));
    }

    // #298: Windows reports a USB 3 hub as two hubs, the USB 2 half on a USB 2 port and the SuperSpeed half on a USB 3
    // port, each with its port's bus, so it counts once on each bus: "2 (1+1+0)", as on macOS.
    [Fact]
    public void AUsbThreeHubIsOneHubOnEachBus()
    {
        var usb2Node = new RawDevNode(@"USB\VID_0BDA&PID_5411\1", "Generic USB Hub", null);
        var usb3Node = new RawDevNode(@"USB\VID_0BDA&PID_0411\2", "Generic SuperSpeed USB Hub", null);
        var usb2Half = new RawPort(1, 1, true, 0x0BDA, 0x5411, usb2Node, new RawHub(usb2Node, [EmptyPort(1) with { Bus = UsbBus.Usb2 }], null)) { Bus = UsbBus.Usb2 };
        var usb3Half = new RawPort(13, 1, true, 0x0BDA, 0x0411, usb3Node, new RawHub(usb3Node, [EmptyPort(1) with { Bus = UsbBus.Usb3 }], null)) { Bus = UsbBus.Usb3 };

        var controller = BuildSingle(usb2Half, usb3Half);

        Assert.Equal([UsbBus.Usb2, UsbBus.Usb3], controller.Children[0].Children.Select(port => port.Bus));
        Assert.All(controller.Children[0].Children, port => Assert.Equal(UsbNodeKind.Hub, Assert.Single(port.Children).Kind));
        Assert.Equal("2 (1+1+0)", TopologyCounts.PerBus([controller]).HubsText);
    }

    // #342: the device's physical location (_PLD) reaches its node, so the two halves of a USB-C port can be paired.
    [Fact]
    public void ThePhysicalLocationOfADeviceIsOnItsNode()
    {
        var dock = new RawDevNode(@"USB\VID_1DA0&PID_901B\5&242A2F40&0&2", "USB2.0 Hub", null) { PhysicalLocation = "01000000080003000104000100000000FFFFFFFF" };

        var root = BuildSingle(DevicePort(2, dock));

        Assert.Equal("01000000080003000104000100000000FFFFFFFF", root.Children[0].Children[0].Children[0].PhysicalLocation);
    }

    // #330: InLocalMachineContainer reaches a hub's node and a device's node, and stays null where Windows gives none.
    [Fact]
    public void TheLocalMachineContainerOfANodeIsOnItsNode()
    {
        var hubNode = new RawDevNode(@"USB\VID_8087&PID_0033\5&1&0&3", "Internal hub", null) { InLocalMachine = true };
        var inside = new RawHub(hubNode, [DevicePort(1, new RawDevNode(@"USB\VID_1DA0&PID_901B\6&1&0&1", "Dock hub", null) { InLocalMachine = false })], null);
        var unknown = new RawDevNode(@"USB\VID_046D&PID_C52B\5&1&0&4", "Receiver", null);

        var root = BuildSingle(new RawPort(3, 1, true, 0x8087, 0x0033, hubNode, inside), DevicePort(4, unknown));

        var hub = root.Children[0].Children[0].Children[0];
        Assert.True(hub.InLocalMachine);
        Assert.False(hub.Children[0].Children[0].InLocalMachine);
        Assert.Null(root.Children[0].Children[1].Children[0].InLocalMachine);
    }
}
