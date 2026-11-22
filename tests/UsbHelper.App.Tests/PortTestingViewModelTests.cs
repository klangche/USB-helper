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

    private static IReadOnlyList<UsbNode> Computer(int? dockOn)
    {
        var bus = dockOn == 2 ? UsbBus.Usb2 : UsbBus.Usb3;
        var dock = Ids(Hub($"DOCK{dockOn}", bus, Ids(Device($"CAMERA{dockOn}") with { Name = "Camera" }, 0x046D, 0x0893)), 0x2109, 0x2817);
        var root = new UsbNode("ROOT", UsbNodeKind.Hub, "Root hub",
            [Port("ROOT", 1, dockOn == 1 ? dock : null, UsbBus.Usb3) with { UsbC = true }, Port("ROOT", 2, dockOn == 2 ? dock : null, UsbBus.Usb2)]);
        return [new UsbNode("C1", UsbNodeKind.Controller, "C1", [root])];
    }

    [Fact]
    public void AMoveIsShownInTheStatusFieldAndThePortsTestedAreInTheExportedLog()
    {
        var log = new EventLog();
        var window = new MainViewModel(null, [], new ComputerIdentity(null, null), log: log);

        window.Load(Computer(1), []);
        // Unplugged, then plugged into Port 2: two changes, as the watchers send them.
        window.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", PortOf(Computer(null), 1))]);
        Assert.Null(window.PortStatus);
        window.Apply([new UsbChange(UsbChangeKind.Added, "ROOT", PortOf(Computer(2), 2))]);

        Assert.Equal("The setup moved from Port 1 (USB-C, USB 3) to Port 2 (USB 2).", window.PortStatus);
        Assert.Empty(window.Disconnected);
        var export = log.Export();
        Assert.Contains("Ports tested" + Environment.NewLine + "Tested on 2 of 2 ports", export, StringComparison.Ordinal);
        Assert.Contains("On Port 2 the Camera runs on USB 2 (USB 3 on Port 1).", export, StringComparison.Ordinal);
        // The difference is a finding, and the report has it and the "Ports tested" summary.
        Assert.Contains(window.Findings, finding => finding.Message == "On Port 2 the Camera runs on USB 2 (USB 3 on Port 1).");
        var report = window.Report(new DateTime(2026, 11, 22), null, null, "0.11.0");
        Assert.Contains(report.Findings, finding => finding.Message == "On Port 2 the Camera runs on USB 2 (USB 3 on Port 1).");
        Assert.StartsWith("Tested on 2 of 2 ports", report.PortsTested, StringComparison.Ordinal);
    }

    private static UsbNode PortOf(IReadOnlyList<UsbNode> tree, int number) => tree[0].Children[0].Children[number - 1];
}
