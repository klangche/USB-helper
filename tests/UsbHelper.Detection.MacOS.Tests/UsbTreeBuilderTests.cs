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
