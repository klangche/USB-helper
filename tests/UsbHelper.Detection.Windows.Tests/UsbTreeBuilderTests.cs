using UsbHelper.Core.Detection;

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
}
