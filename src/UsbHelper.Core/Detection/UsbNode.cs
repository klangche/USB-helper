namespace UsbHelper.Core.Detection;

public enum UsbNodeKind
{
    Controller,
    Hub,
    Port,
    Device,
}

/// <summary>
/// The bus a port is on (#191). A USB 3 hub is two hubs in one box, a USB 2 hub on the USB 2 bus and a SuperSpeed hub
/// on the USB 3 bus, and each bus has its own tree and limits.
/// </summary>
public enum UsbBus
{
    Usb2,
    Usb3,
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

    /// <summary>
    /// Set for ports only: the bus the port is on, or null when the operating system does not say or says both (#193).
    /// Never inferred from a name or a speed.
    /// </summary>
    public UsbBus? Bus { get; init; }

    /// <summary>
    /// Set for ports only: the id of the port on the other bus that belongs to the same connector (the USB 2 port of a
    /// USB 3 connector, or the other way round), or null when the operating system names none (#193).
    /// </summary>
    public string? CompanionPortId { get; init; }

    public ushort? VendorId { get; init; }

    public ushort? ProductId { get; init; }

    /// <summary>
    /// Set for devices only: camera, audio, both or neither, from the classes of the device's interfaces, or
    /// <see cref="UsbDeviceKind.Unknown"/> when the operating system does not say (#274). Null for hubs, ports and controllers.
    /// </summary>
    public UsbDeviceKind? DeviceKind { get; init; }

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
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDisplayLink => Kind == UsbNodeKind.Device && VendorId == DisplayLinkVendorId;

    /// <summary>
    /// For hubs and devices: the container id the operating system gives the physical device this node is part of
    /// (#143), or null when it gives none. A monitor in the same container is shown under this node.
    /// </summary>
    public Guid? ContainerId { get; init; }

    /// <summary>A problem the operating system reports for this element, or null when it reports none.</summary>
    public string? Problem { get; init; }

    /// <summary>
    /// For a hub or device: the physical location Windows gives its connector (DEVPKEY_Device_PhysicalDeviceLocation, the
    /// ACPI <c>_PLD</c> buffer) as hex, or null. Both halves of one USB-C port have the same one (#342, the owner's Asus on #354).
    /// </summary>
    public string? PhysicalLocation { get; init; }

    /// <summary>
    /// For a controller: how many tiers it allows from its root hub, when the system reports it (#330). macOS gives
    /// <c>UsbHostControllerTierLimit</c> on each controller, 6 on an M1 Max: the USB specification's 7 tiers less the
    /// hubs inside the computer that the tree does not show. Null when not reported.
    /// </summary>
    public int? TierLimit { get; init; }

    /// <summary>The tiers of the USB specification (USB 2.0 section 4.1.1, USB 3.2 the same): a controller with fewer has hidden hubs.</summary>
    public const int SpecificationTiers = 7;

    /// <summary>
    /// For a controller: the hubs inside the computer it does not show, from <see cref="TierLimit"/> (#330):
    /// <see cref="SpecificationTiers"/> less the limit, or 0 when the limit is unknown or not below 7.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore] // Follows from TierLimit, which is written.
    public int HiddenHubs => TierLimit is { } limit and > 0 and < SpecificationTiers ? SpecificationTiers - limit : 0;
}
