namespace UsbHelper.Detection.Windows;

/// <summary>Reads USB_PORT_CONNECTOR_PROPERTIES (usbioctl.h, packed) from IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES.</summary>
internal static class PortConnector
{
    private const int PortPropertiesOffset = 8; // ConnectionIndex (4), ActualLength (4)
    private const uint PortIsUserConnectable = 0x1; // first bit of USB_PORT_PROPERTIES
    private const uint PortConnectorIsTypeC = 0x8; // fourth bit of USB_PORT_PROPERTIES

    /// <summary>Whether the user can plug into the port, or null when the answer is too short to say.</summary>
    public static bool? IsUserConnectable(byte[] buffer, int bytesReturned) => HasBit(buffer, bytesReturned, PortIsUserConnectable);

    /// <summary>Whether the port is a USB Type-C connector, or null when the answer is too short to say.</summary>
    public static bool? IsTypeC(byte[] buffer, int bytesReturned) => HasBit(buffer, bytesReturned, PortConnectorIsTypeC);

    private static bool? HasBit(byte[] buffer, int bytesReturned, uint bit) =>
        bytesReturned < PortPropertiesOffset + sizeof(uint)
            ? null
            : (BitConverter.ToUInt32(buffer, PortPropertiesOffset) & bit) != 0;
}
