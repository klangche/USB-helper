using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// USB_NODE_CONNECTION_INFORMATION_EX_V2 (usbioctl.h) for IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2: which USB
/// protocols a port supports, so which bus it is on (#193). Not a speed: nothing here says how fast a device runs.
/// </summary>
internal static class PortProtocols
{
    private const int Size = 16; // ConnectionIndex, Length, SupportedUsbProtocols, Flags
    private const int ProtocolsOffset = 8;
    private const uint Usb110 = 0x1;
    private const uint Usb200 = 0x2;
    private const uint Usb300 = 0x4;

    /// <summary>The request for <paramref name="port"/>: the caller says it understands USB 1.1, 2.0 and 3.</summary>
    public static byte[] Request(int port)
    {
        var buffer = new byte[Size];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), (uint)port);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), (uint)Size);
        BitConverter.TryWriteBytes(buffer.AsSpan(ProtocolsOffset), Usb110 | Usb200 | Usb300);
        return buffer;
    }

    /// <summary>
    /// USB 3 when the port supports only USB 3, USB 2 when it supports USB 1.1 or 2.0 and not USB 3. Null when it
    /// supports both or none, or the answer is too short: then Windows does not say which bus a device is on.
    /// </summary>
    public static UsbBus? Bus(byte[] buffer, int bytesReturned)
    {
        if (bytesReturned < ProtocolsOffset + sizeof(uint))
        {
            return null;
        }
        var protocols = BitConverter.ToUInt32(buffer, ProtocolsOffset);
        var usb3 = (protocols & Usb300) != 0;
        var usb2 = (protocols & (Usb110 | Usb200)) != 0;
        return usb3 == usb2 ? null : usb3 ? UsbBus.Usb3 : UsbBus.Usb2;
    }
}
