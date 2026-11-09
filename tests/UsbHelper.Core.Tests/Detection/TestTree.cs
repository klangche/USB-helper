using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Tests.Detection;

/// <summary>Builds small USB trees for tests. Hub ports are numbered from 1 in the order given; null is an empty port.</summary>
internal static class TestTree
{
    public static UsbNode Device(string id, string? problem = null) => new(id, UsbNodeKind.Device, id, []) { Problem = problem };

    public static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    public static UsbNode Controller(string id, UsbNode rootHub) => new(id, UsbNodeKind.Controller, id, [rootHub]);

    public static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    /// <summary>The hub with its ports marked user-connectable or not, in port order; null leaves a port unknown.</summary>
    public static UsbNode Connectable(this UsbNode hub, params bool?[] userConnectable) =>
        hub with { Children = hub.Children.Select((port, index) => port with { UserConnectable = userConnectable[index] }).ToList() };

    /// <summary>The hub with every port in it and below it on <paramref name="bus"/>.</summary>
    public static UsbNode OnBus(this UsbNode hub, UsbBus bus) => hub with
    {
        Children = hub.Children.Select(port => port with
        {
            Bus = bus,
            Children = port.Children.Select(child => child.Kind == UsbNodeKind.Hub ? child.OnBus(bus) : child).ToList(),
        }).ToList(),
    };

    /// <summary>A root hub whose first port leads to the USB 2 tree <paramref name="usb2"/> and second to the USB 3 tree <paramref name="usb3"/>.</summary>
    public static UsbNode RootWithBuses(string id, UsbNode usb2, UsbNode usb3)
    {
        var root = Hub(id, usb2.OnBus(UsbBus.Usb2), usb3.OnBus(UsbBus.Usb3));
        return root with
        {
            Children = [root.Children[0] with { Bus = UsbBus.Usb2 }, root.Children[1] with { Bus = UsbBus.Usb3 }],
        };
    }

    /// <summary>
    /// Shaped like the Mac16,13 tester log of 2026-11-05 (#191): 8 hubs in all, of which the USB 2 bus has 6 with the
    /// deepest at tier 5, hop 3, and the USB 3 bus 2, the deepest at tier 3, hop 1. The capture itself is not in the
    /// repository; the shape is built from the numbers in #195.
    /// </summary>
    public static IReadOnlyList<UsbNode> TesterMac() =>
    [
        Controller("C1", RootWithBuses("ROOT",
            Hub("H1", Hub("H2", Hub("H3", Device("HEADSET"))), Hub("H4", Device("MOUSE")), Hub("H5", Hub("H6", (UsbNode?)null))),
            Hub("S1", Hub("S2", (UsbNode?)null)))),
    ];
}
