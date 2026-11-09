using UsbHelper.Core.Detection;
using UsbHelper.Detection.MacOS;

namespace UsbHelper.Detection.MacOS.Tests;

public class UsbTreeBuilderTests
{
    private static RegistryEntry Port(string name, int number, params RegistryEntry[] children) =>
        new("AppleUSB20XHCIARMPort", name, false, new Dictionary<string, object> { ["PortNumber"] = (long)number }, children);

    private static RegistryEntry Device(long location, long vendor, long product, string? name, long deviceClass = 0, string? error = null)
    {
        var properties = new Dictionary<string, object>
        {
            ["locationID"] = location,
            ["idVendor"] = vendor,
            ["idProduct"] = product,
            ["bDeviceClass"] = deviceClass,
        };
        if (name != null)
        {
            properties["USB Product Name"] = name;
        }

        return new RegistryEntry("IOUSBHostDevice", "dev", true, properties, [], error);
    }

    private static RegistryEntry Controller(long location, params RegistryEntry[] children) =>
        new("AppleT6000USBXHCI", $"AppleT6000USBXHCI@{location:x8}", false,
            new Dictionary<string, object> { ["locationID"] = location }, children);

    [Fact]
    public void Spike_capture_without_peripherals_gives_three_controllers_with_empty_ports()
    {
        var tree = UsbTreeBuilder.Build(
        [
            Controller(0, Port("hs", 1), Port("ss", 1)),
            Controller(0x01000000, Port("hs", 1), Port("ss", 1)),
            Controller(0x02000000, Port("hs", 1), Port("ss", 1)),
        ]);

        Assert.Equal(["usb-controller-00000000", "usb-controller-01000000", "usb-controller-02000000"], tree.Select(c => c.Id));
        foreach (var controller in tree)
        {
            var rootHub = Assert.Single(controller.Children);
            Assert.Equal(UsbNodeKind.Hub, rootHub.Kind);
            var port = Assert.Single(rootHub.Children);
            Assert.Equal(1, port.PortNumber);
            Assert.Empty(port.Children);
            Assert.Null(port.UsbC);
            Assert.Null(port.UserConnectable);
        }
    }

    [Fact]
    public void Device_on_a_port_gets_ids_and_name()
    {
        var tree = UsbTreeBuilder.Build([Controller(0x01000000, Port("hs", 1, Device(0x01100000, 0x05AC, 0x1234, "Keyboard")))]);

        var port = tree[0].Children[0].Children[0];
        var device = Assert.Single(port.Children);
        Assert.Equal(UsbNodeKind.Device, device.Kind);
        Assert.Equal("Keyboard", device.Name);
        Assert.Equal((ushort)0x05AC, device.VendorId);
        Assert.Equal((ushort)0x1234, device.ProductId);
        Assert.Equal("usb-device-01100000", device.Id);
        Assert.Equal("usb-controller-01000000|roothub|port1", port.Id);
    }

    [Fact]
    public void Hub_with_nothing_connected_keeps_its_ports_and_is_readable()
    {
        var hubPort = new RegistryEntry("AppleUSB20HubPort", "port", false, new Dictionary<string, object> { ["port"] = new byte[] { 1, 0, 0, 0 } }, []);
        var hubPort2 = new RegistryEntry("AppleUSB20HubPort", "port", false, new Dictionary<string, object> { ["port"] = new byte[] { 2, 0, 0, 0 } }, []);
        var driver = new RegistryEntry("AppleUSB20Hub", "driver", false, new Dictionary<string, object>(), [hubPort, hubPort2]);
        var hub = Device(0x01100000, 0x2109, 0x2812, "USB 2.0 Hub", deviceClass: 9) with { Children = [driver] };
        var tree = UsbTreeBuilder.Build([Controller(0x01000000, Port("hs", 1, hub))]);

        var hubNode = tree[0].Children[0].Children[0].Children[0];
        Assert.Equal([1, 2], hubNode.Children.Select(p => p.PortNumber));
        Assert.Empty(UsbHelper.Core.Topology.TopologyCounts.Unreadable(tree));
        Assert.Single(tree[0].Children[0].Children);
    }

    [Fact]
    public void Hub_gets_ports_for_its_devices_from_the_location_path()
    {
        var hub = Device(0x01100000, 0x2109, 0x2812, "USB3.0 Hub", deviceClass: 9);
        var tree = UsbTreeBuilder.Build(
        [
            Controller(0x01000000, Port("hs", 1, hub), Device(0x01120000, 0x046D, 0xC52B, "Receiver"), Device(0x01140000, 1, 2, null)),
        ]);

        var hubNode = tree[0].Children[0].Children[0].Children[0];
        Assert.Equal(UsbNodeKind.Hub, hubNode.Kind);
        Assert.Equal([2, 4], hubNode.Children.Select(p => p.PortNumber));
        Assert.Equal("Receiver", hubNode.Children[0].Children[0].Name);
        Assert.Equal("Unknown USB device", hubNode.Children[1].Children[0].Name);
    }

    private static RegistryEntry HubPort(long location) =>
        new("AppleUSB30HubPort", "port", false, new Dictionary<string, object> { ["locationID"] = location }, []);

    [Fact]
    public void Hub_keeps_its_empty_ports_so_an_unplugged_device_has_a_port_to_show_on()
    {
        var hub = Device(0x01100000, 0x2109, 0x2812, "Hub", deviceClass: 9);
        var stick = Device(0x01120000, 1, 2, "Stick");

        var plugged = UsbTreeBuilder.Build([Controller(0x01000000, Port("hs", 1, hub), HubPort(0x01110000), HubPort(0x01120000), stick)]);
        var unplugged = UsbTreeBuilder.Build([Controller(0x01000000, Port("hs", 1, hub), HubPort(0x01110000), HubPort(0x01120000))]);

        var hubNode = unplugged[0].Children[0].Children[0].Children[0];
        Assert.Equal([1, 2], hubNode.Children.Select(p => p.PortNumber));
        Assert.All(hubNode.Children, p => Assert.Empty(p.Children));
        Assert.Equal("usb-device-01100000|port2", hubNode.Children[1].Id);
        Assert.Equal("usb-device-01100000|port2", plugged[0].Children[0].Children[0].Children[0].Children[1].Id);
    }

    // #187, from the owner's capture on a MacBookPro18,2: "PortNumber" is the USB-C receptacle (1-3), the same on its
    // hs and ss entry; the place on the root hub is the locationID nibble. "port" is CFData, which the reader does not
    // decode, so the entry holds only what IoRegistryReader really returns.
    private static RegistryEntry RootPort(string className, long receptacle, long location, params RegistryEntry[] children) =>
        new(className, "port", false, new Dictionary<string, object> { ["PortNumber"] = receptacle, ["locationID"] = location }, children);

    [Fact]
    public void Every_apple_silicon_controller_has_root_ports_1_and_2_whatever_its_receptacle()
    {
        var tree = UsbTreeBuilder.Build(
        [
            Controller(0, RootPort("AppleUSB20XHCIARMPort", 1, 0x00100000), RootPort("AppleT8103USB30XHCIPort", 1, 0x00200000)),
            Controller(0x02000000,
                RootPort("AppleUSB20XHCIARMPort", 3, 0x02100000, Device(0x02100000, 0x0BDA, 0x5411, "USB2.1 Hub", deviceClass: 9)),
                RootPort("AppleT8103USB30XHCIPort", 3, 0x02200000, Device(0x02200000, 0x0BDA, 0x0411, "USB3.2 Hub", deviceClass: 9))),
            Controller(0x01000000, RootPort("AppleUSB20XHCIARMPort", 2, 0x01100000), RootPort("AppleT8103USB30XHCIPort", 2, 0x01200000)),
        ]);

        Assert.All(tree, controller => Assert.Equal([1, 2], controller.Children[0].Children.Select(p => p.PortNumber)));
        Assert.Equal(["USB2.1 Hub", "USB3.2 Hub"], tree[1].Children[0].Children.Select(p => p.Children[0].Name));
    }

    // #194, from the owner's capture on an M1 Pro/Max (AppleT6000USBXHCI, comment on #194, 2026-11-09): the class name
    // of a port entry gives its bus.
    private static RegistryEntry Entry(string className, long location, params RegistryEntry[] children) =>
        new(className, className, false, new Dictionary<string, object> { ["locationID"] = location }, children);

    private static RegistryEntry Hub(long location, string name, string driver, string portClass, long[] ports, params RegistryEntry[] devices) =>
        Device(location, 0x0BDA, 0x5411, name, deviceClass: 9) with
        {
            Children = [new RegistryEntry(driver, driver, false, new Dictionary<string, object>(), [.. ports.Select(port => Entry(portClass, port)), .. devices])],
        };

    [Fact]
    public void Apple_silicon_ports_get_their_bus_from_the_class_name()
    {
        var usb2 = Hub(0x00100000, "USB2.1 Hub", "AppleUSB20Hub", "AppleUSB20HubPort", [0x00110000, 0x00120000, 0x00130000, 0x00140000],
            Hub(0x00110000, "USB 2.0 Hub", "AppleUSB20Hub", "AppleUSB20HubPort", [0x00111000, 0x00112000, 0x00113000, 0x00114000]));
        var usb3 = Hub(0x00200000, "USB3.2 Hub", "AppleUSB30Hub", "AppleUSB30HubPort", [0x00210000, 0x00220000, 0x00230000, 0x00240000],
            Device(0x00240000, 0x0BDA, 0x8153, "USB 10/100/1000 LAN"));
        var tree = UsbTreeBuilder.Build(
        [
            Controller(0, RootPort("AppleUSB20XHCIARMPort", 1, 0x00100000, usb2), RootPort("AppleT8103USB30XHCIPort", 1, 0x00200000, usb3)),
        ]);

        var root = tree[0].Children[0].Children;
        Assert.Equal([UsbBus.Usb2, UsbBus.Usb3], root.Select(port => port.Bus));
        var hub2 = root[0].Children[0];
        var hub3 = root[1].Children[0];
        Assert.Equal("USB2.1 Hub", hub2.Name);
        Assert.Equal("USB3.2 Hub", hub3.Name);
        Assert.All(hub2.Children, port => Assert.Equal(UsbBus.Usb2, port.Bus));
        Assert.All(hub2.Children[0].Children[0].Children, port => Assert.Equal(UsbBus.Usb2, port.Bus));
        Assert.Equal(4, hub2.Children[0].Children[0].Children.Count);
        Assert.All(hub3.Children, port => Assert.Equal(UsbBus.Usb3, port.Bus));
        Assert.Equal("USB 10/100/1000 LAN", hub3.Children[3].Children[0].Name);
    }

    [Fact]
    public void A_port_class_the_app_does_not_know_has_an_unknown_bus()
    {
        var tree = UsbTreeBuilder.Build(
        [
            Controller(0, RootPort("AppleT9999USB40XHCIPort", 1, 0x00100000), RootPort("AppleUSB20XHCIARMPort", 1, 0x00200000)),
        ]);

        Assert.Equal([null, UsbBus.Usb2], tree[0].Children[0].Children.Select(port => port.Bus));
    }

    [Fact]
    public void Ids_are_the_same_on_repeated_reads()
    {
        RegistryEntry[] Snapshot() => [Controller(0x01000000, Port("hs", 1, Device(0x01100000, 1, 2, "A")))];

        Assert.Equal(UsbTreeBuilder.Build(Snapshot()).Select(Ids), UsbTreeBuilder.Build(Snapshot()).Select(Ids));

        static IEnumerable<string> Ids(UsbNode node) => node.Children.SelectMany(Ids).Prepend(node.Id);
    }

    [Fact]
    public void Missing_properties_are_unknown()
    {
        var bare = new RegistryEntry("IOUSBHostDevice", "dev", true, new Dictionary<string, object> { ["locationID"] = 0x01100000L }, []);

        var device = UsbTreeBuilder.Build([Controller(0x01000000, bare)])[0].Children[0].Children[0].Children[0];

        Assert.Null(device.VendorId);
        Assert.Null(device.ProductId);
        Assert.Equal("Unknown USB device", device.Name);
    }

    [Fact]
    public void Incomplete_reads_are_reported_as_problems()
    {
        var noLocation = new RegistryEntry("IOUSBHostDevice", "dev", true, new Dictionary<string, object>(), []);
        var broken = new RegistryEntry("AppleT6000USBXHCI", "c", false, new Dictionary<string, object> { ["locationID"] = 0L }, [], "boom");
        var tree = UsbTreeBuilder.Build([broken, Controller(0x01000000, noLocation, Port("hs", 1, Device(0x01100000, 1, 2, "A", error: "no properties")))]);

        Assert.Contains("boom", tree[0].Problem);
        Assert.Contains("1 USB device(s)", tree[1].Problem);
        Assert.Contains("no properties", tree[1].Children[0].Children[0].Children[0].Problem);
    }

    [Fact]
    public void Controller_without_location_gets_an_index_id()
    {
        var controller = new RegistryEntry("X", "c", false, new Dictionary<string, object>(), []);

        Assert.Equal("usb-controller-unknown0", UsbTreeBuilder.Build([controller])[0].Id);
    }

    [Fact]
    public void Detector_enumerates_through_the_reader()
    {
        var detector = new MacUsbDetector(new FakeReader([Controller(0, Port("hs", 1))]));

        Assert.Single(detector.Enumerate());
    }

    [Fact]
    public void Live_registry_has_controllers_on_a_mac()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var tree = new MacUsbDetector().Enumerate();

        Assert.NotEmpty(tree);
        Assert.All(tree, c => Assert.Equal(UsbNodeKind.Controller, c.Kind));
    }

    private sealed class FakeReader(IReadOnlyList<RegistryEntry> controllers) : IIoRegistryReader
    {
        public IReadOnlyList<RegistryEntry> ReadControllers() => controllers;
    }
}
