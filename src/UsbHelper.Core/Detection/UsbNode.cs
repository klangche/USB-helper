namespace UsbHelper.Core.Detection;

public enum UsbNodeKind
{
    Controller,
    Hub,
    Port,
    Device,
}

/// <summary>
/// One element of the detected USB tree. A controller has its root hub as child, a hub has its ports,
/// a port has the hub or device connected to it (none when empty).
/// </summary>
/// <param name="Id">
/// Stable across enumerations while the device stays on the same port. An id that starts with its port's id and
/// <c>|</c> is a placeholder for a hub or device the operating system could not identify.
/// </param>
/// <param name="Name">Name reported by the operating system.</param>
public sealed record UsbNode(string Id, UsbNodeKind Kind, string Name, IReadOnlyList<UsbNode> Children)
{
    /// <summary>Port number on the parent hub; set for ports only.</summary>
    public int? PortNumber { get; init; }

    /// <summary>
    /// Set for ports only: true when the user can plug into it, false when it is wired inside the computer (or inside
    /// a dock or monitor), null when the operating system does not say.
    /// </summary>
    public bool? UserConnectable { get; init; }

    /// <summary>Set for ports only: true when the operating system calls it a USB-C connector, false when not, null when it does not say.</summary>
    public bool? UsbC { get; init; }

    public ushort? VendorId { get; init; }

    public ushort? ProductId { get; init; }

    /// <summary>
    /// DisplayLink's USB vendor id, 0x17E9. Sources: the USB ID Repository's usb.ids ("17e9  DisplayLink",
    /// http://www.linux-usb.org/usb.ids, read from its mirror https://github.com/usbids/usbids, version 2026.06.26), and the
    /// Linux DisplayLink driver, which matches on ".idVendor = 0x17e9" (drivers/gpu/drm/udl/udl_drv.c).
    /// </summary>
    public const ushort DisplayLinkVendorId = 0x17E9;

    /// <summary>
    /// A DisplayLink device: a USB display adapter whose displays need the DisplayLink driver. Only a device, never a hub:
    /// the adapter is the display, the hub above it only leads to it.
    /// </summary>
    public bool IsDisplayLink => Kind == UsbNodeKind.Device && VendorId == DisplayLinkVendorId;

    /// <summary>A problem the operating system reports for this element, or null when it reports none.</summary>
    public string? Problem { get; init; }
}
