using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests;

/// <summary>The window and the exported log when the setup moves to another port (#342).</summary>
public class PortTestingViewModelTests
{
    private static UsbNode Ids(UsbNode node, ushort vendor, ushort product) => node with { VendorId = vendor, ProductId = product };

    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hub, int number, UsbNode? child, UsbBus bus) =>
        new($"{hub}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number, Bus = bus, UserConnectable = true };

    private static UsbNode Hub(string id, UsbBus bus, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, [.. ports.Select((child, i) => Port(id, i + 1, child, bus))]);

    private static IReadOnlyList<UsbNode> Computer(int dockOn)
    {
        var bus = dockOn == 2 ? UsbBus.Usb2 : UsbBus.Usb3;
        var dock = Ids(Hub($"DOCK{dockOn}", bus, Ids(Device($"CAMERA{dockOn}") with { Name = "Camera" }, 0x046D, 0x0893)), 0x2109, 0x2817);
        var root = new UsbNode("ROOT", UsbNodeKind.Hub, "Root hub",
            [Port("ROOT", 1, dockOn == 1 ? dock : null, UsbBus.Usb3), Port("ROOT", 2, dockOn == 2 ? dock : null, UsbBus.Usb2)]);
        return [new UsbNode("C1", UsbNodeKind.Controller, "C1", [root])];
    }

    [Fact]
    public void AMoveIsShownInTheStatusFieldAndThePortsTestedAreInTheExportedLog()
    {
        var log = new EventLog();
        var window = new MainViewModel(null, [], new ComputerIdentity(null, null), log: log);

        window.Load(Computer(1), []);
        window.Load(Computer(2), []);

        Assert.Equal("The setup moved from Port 1 (USB 3) to Port 2 (USB 2).", window.PortStatus);
        Assert.Empty(window.Disconnected);
        var export = log.Export();
        Assert.Contains("Ports tested" + Environment.NewLine + "Tested on 2 of 2 ports", export, StringComparison.Ordinal);
        Assert.Contains("On Port 2 the Camera runs on USB 2 (USB 3 on Port 1).", export, StringComparison.Ordinal);
    }
}
