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
}
