using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>Turns raw Windows USB data into the operating-system-neutral tree.</summary>
internal static class UsbTreeBuilder
{
    // USB_CONNECTION_STATUS values from usbioctl.h.
    private const int NoDeviceConnected = 0;

    public static IReadOnlyList<UsbNode> Build(IReadOnlyList<RawController> controllers) =>
        controllers.Select(Controller).ToList();

    private static UsbNode Controller(RawController controller)
    {
        var id = controller.Node.InstanceId;
        UsbNode[] children = controller.RootHub == null ? [] : [Hub(controller.RootHub, null, id + "|roothub")];
        return new UsbNode(id, UsbNodeKind.Controller, controller.Node.Name, children)
        {
            Problem = DeviceProblem(controller.Node.ProblemCode)
                ?? (controller.Error == null ? null : $"Could not read the root hub: {controller.Error}"),
        };
    }

    private static UsbNode Hub(RawHub hub, RawDevNode? portDevice, string fallbackId)
    {
        var node = hub.Node ?? portDevice;
        var id = node?.InstanceId ?? fallbackId;
        return new UsbNode(id, UsbNodeKind.Hub, node?.Name ?? "USB hub", hub.Ports.Select(port => Port(port, id)).ToList())
        {
            Problem = DeviceProblem(node?.ProblemCode) ?? (hub.Error == null ? null : $"Could not read the hub: {hub.Error}"),
        };
    }

    private static UsbNode Port(RawPort port, string hubId)
    {
        var id = $"{hubId}|port{port.Number}";
        UsbNode[] children = port.ConnectionStatus == NoDeviceConnected ? []
            : port.IsHub && port.Hub != null ? [WithIds(Hub(port.Hub, port.Device, id + "|device"), port)]
            : [Device(port, id + "|device")];
        return new UsbNode(id, UsbNodeKind.Port, $"Port {port.Number}", children)
        {
            PortNumber = port.Number,
            Problem = port.Error == null ? ConnectionProblem(port.ConnectionStatus) : $"Could not read the port: {port.Error}",
        };
    }

    private static UsbNode Device(RawPort port, string fallbackId) =>
        WithIds(new UsbNode(
            port.Device?.InstanceId ?? fallbackId,
            port.IsHub ? UsbNodeKind.Hub : UsbNodeKind.Device,
            port.Device?.Name ?? "Unknown USB device",
            [])
        {
            Problem = DeviceProblem(port.Device?.ProblemCode),
        }, port);

    // A failed enumeration leaves the descriptor empty; 0000:0000 is not a real vendor and product.
    private static UsbNode WithIds(UsbNode node, RawPort port) =>
        port.VendorId == 0 && port.ProductId == 0 ? node : node with { VendorId = port.VendorId, ProductId = port.ProductId };

    private static string? ConnectionProblem(int status) => status switch
    {
        0 or 1 => null, // no device, device connected
        2 => "The device failed enumeration.",
        3 => "The port reports a general failure.",
        4 => "The device caused an overcurrent condition.",
        5 => "Not enough power for the device.",
        6 => "Windows reports not enough bandwidth for the device.",
        7 => "Hubs are nested too deeply.",
        8 => "The device is connected to a legacy hub.",
        9 or 10 => null, // enumerating, being reset: transient
        _ => $"Unknown connection status {status}.",
    };

    private static string? DeviceProblem(int? code) => code switch
    {
        null => null,
        10 => "Windows cannot start this device (code 10).",
        22 => "The device is disabled (code 22).",
        28 => "The drivers for this device are not installed (code 28).",
        43 => "Windows stopped this device because it reported problems (code 43).",
        _ => $"Windows reports a problem with this device (code {code}).",
    };
}
