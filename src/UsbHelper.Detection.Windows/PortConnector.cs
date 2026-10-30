namespace UsbHelper.Detection.Windows;

/// <summary>Reads USB_PORT_CONNECTOR_PROPERTIES (usbioctl.h, packed) from IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES.</summary>
internal static class PortConnector
{
    private const int PortPropertiesOffset = 8; // ConnectionIndex (4), ActualLength (4)
    private const uint PortIsUserConnectable = 0x1; // first bit of USB_PORT_PROPERTIES

    /// <summary>Whether the user can plug into the port, or null when the answer is too short to say.</summary>
    public static bool? IsUserConnectable(byte[] buffer, int bytesReturned) =>
        bytesReturned < PortPropertiesOffset + sizeof(uint)
            ? null
            : (BitConverter.ToUInt32(buffer, PortPropertiesOffset) & PortIsUserConnectable) != 0;
}
