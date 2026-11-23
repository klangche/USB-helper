using UsbHelper.Detection.Windows;

namespace UsbHelper.Detection.Windows.Tests;

/// <summary>USB4 routers on Windows (#332), from the owner's Asus with a Parade PS9010 dock (#354).</summary>
public class Usb4RouterLinksTests
{
    private const string Host0 = @"PCI\VEN_8086&DEV_463E&SUBSYS_11112222&REV_02&USB4_MS_CM\3&11583659&0&6A";
    private const string Host1 = @"PCI\VEN_8086&DEV_466D&SUBSYS_11112222&REV_02&USB4_MS_CM\3&11583659&0&6B";
    private const string Root0 = @"USB4\ROOT_DEVICE_ROUTER&VID_8086&PID_463E\4&8469D8&0&0";
    private const string Dock = "USB4-router (1.0), Parade Technologies – PS9010";

    private static readonly IReadOnlyList<Usb4Node> Asus =
    [
        new(@"USB4\VIRTUAL_POWER_PDO\4&8469D8&0&0", Host0, ""),
        new(Root0, Host0, "USB4 rootrouter (1.0)"),
        new(@"USB4\ROOT_DEVICE_ROUTER&VID_8086&PID_466D\4&34C82E01&0&0", Host1, "USB4 rootrouter (1.0)"),
        new(@"USB4\VIRTUAL_POWER_PDO\4&34C82E01&0&0", Host1, ""),
        new(@"USB4\VID_1DA0&PID_9010\5&11B78259&0&1", Root0, Dock),
    ];

    [Fact]
    public void TheDockIsOneRouterAtDepthOneOnItsHostRouter()
    {
        var hosts = Usb4RouterLinks.Depths(Asus);

        Assert.Equal(1, hosts[Host0].Depth);
        Assert.Equal([Dock], hosts[Host0].Names);
        Assert.Equal(0, hosts[Host1].Depth); // The other port: a root router with nothing on it.
    }

    [Fact]
    public void ADockBehindADockIsDepthTwo()
    {
        IReadOnlyList<Usb4Node> chain = [.. Asus, new(@"USB4\VID_1DA0&PID_9010\6&22&0&1", @"USB4\VID_1DA0&PID_9010\5&11B78259&0&1", "Second dock")];

        Assert.Equal(2, Usb4RouterLinks.Depths(chain)[Host0].Depth);
    }

    // The host routers (PCI device 13, functions 2 and 3) share their PCI device with the Type-C xHCI (function 0),
    // which carries the dock's USB 3; the PCH controller is device 20.
    [Fact]
    public void AControllerIsLinkedToTheHostRoutersOnItsPciDevice()
    {
        var typeC = Usb4RouterLinks.Slot(["PCIROOT(0)#PCI(0D00)", "ACPI(_SB_)#ACPI(PC00)#ACPI(TXHC)"]);

        Assert.Equal("PCIROOT(0)#PCI(0D", typeC);
        Assert.Equal(typeC, Usb4RouterLinks.Slot(["PCIROOT(0)#PCI(0D02)", "ACPI(_SB_)#ACPI(PC00)#ACPI(TDM0)"]));
        Assert.NotEqual(typeC, Usb4RouterLinks.Slot(["PCIROOT(0)#PCI(1400)", "ACPI(_SB_)#ACPI(PC00)#ACPI(XHCI)"]));
        Assert.Null(Usb4RouterLinks.Slot(["ACPI(_SB_)#ACPI(PC00)"]));
        Assert.Null(Usb4RouterLinks.Slot([]));
    }

    [Fact]
    public void TheRoutersReachTheControllerNode()
    {
        var controller = new RawController(new RawDevNode(@"PCI\VEN_8086&DEV_461E\3&11583659&0&68", "Type-C controller", null), null, null)
        {
            Usb4Routers = 1,
            Usb4Link = $"USB4: {Dock}",
        };

        var node = Assert.Single(UsbTreeBuilder.Build([controller]));

        Assert.Equal(1, node.Usb4Routers);
        Assert.Equal($"USB4: {Dock}", node.Usb4Link);
    }
}
