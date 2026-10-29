namespace UsbHelper.Detection.Windows;

/// <summary>A device node from SetupAPI.</summary>
/// <param name="ProblemCode">Windows device problem code (CM_PROB_*), or null when the device has no problem.</param>
internal sealed record RawDevNode(string InstanceId, string Name, int? ProblemCode);

/// <param name="Error">Why the root hub could not be read, or null.</param>
internal sealed record RawController(RawDevNode Node, RawHub? RootHub, string? Error);

/// <param name="Node">The hub's device node, or null when it was not found.</param>
/// <param name="Error">Why the hub could not be read, or null.</param>
internal sealed record RawHub(RawDevNode? Node, IReadOnlyList<RawPort> Ports, string? Error);

/// <summary>One hub port, from IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX.</summary>
/// <param name="ConnectionStatus">USB_CONNECTION_STATUS value.</param>
/// <param name="Device">Device node of the connected device, or null when none or not found.</param>
/// <param name="Hub">The connected hub's ports, when the device is a hub.</param>
internal sealed record RawPort(
    int Number,
    int ConnectionStatus,
    bool IsHub,
    ushort VendorId,
    ushort ProductId,
    RawDevNode? Device,
    RawHub? Hub)
{
    /// <summary>Why the port could not be read, or null.</summary>
    public string? Error { get; init; }
}
