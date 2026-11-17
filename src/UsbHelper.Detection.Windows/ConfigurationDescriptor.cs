namespace UsbHelper.Detection.Windows;

/// <summary>
/// USB_DESCRIPTOR_REQUEST (usbioctl.h) for IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, and the interface classes in the
/// configuration descriptor it returns (#274). The configuration descriptor (USB 2.0 specification 9.6.3) is followed by
/// its interface descriptors (9.6.5), each with its bInterfaceClass, and their class-specific and endpoint descriptors.
/// </summary>
internal static class ConfigurationDescriptor
{
    /// <summary>ConnectionIndex (4), then the setup packet: bmRequest, bRequest, wValue, wIndex, wLength (pshpack1).</summary>
    private const int DataOffset = 12;
    private const int WValueOffset = 6;
    private const int WLengthOffset = 10;
    private const byte ConfigurationType = 2;
    private const byte InterfaceType = 4;
    private const int InterfaceLength = 9;
    private const int InterfaceClassOffset = 5;

    /// <summary>The length of the configuration descriptor itself, which holds the length of all of it (wTotalLength).</summary>
    public const int HeaderLength = 9;

    /// <summary>
    /// The request for the first <paramref name="length"/> bytes of configuration descriptor 0 of the device on
    /// <paramref name="port"/>, the way USBView asks for it. Windows writes the reply after the request header.
    /// </summary>
    public static byte[] Request(int port, int length)
    {
        var buffer = new byte[DataOffset + length];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), (uint)port);
        BitConverter.TryWriteBytes(buffer.AsSpan(WValueOffset), (ushort)(ConfigurationType << 8));
        BitConverter.TryWriteBytes(buffer.AsSpan(WLengthOffset), (ushort)length);
        return buffer;
    }

    /// <summary>wTotalLength from a reply to a request for <see cref="HeaderLength"/> bytes; null when it is not one.</summary>
    public static int? TotalLength(byte[] buffer, int bytesReturned)
    {
        if (bytesReturned < DataOffset + HeaderLength || bytesReturned > buffer.Length || buffer[DataOffset + 1] != ConfigurationType)
        {
            return null;
        }
        var total = BitConverter.ToUInt16(buffer, DataOffset + 2);
        return total < HeaderLength ? null : total;
    }

    /// <summary>
    /// The bInterfaceClass of every interface descriptor, alternate settings included. Null when the reply is not one
    /// whole, well-formed configuration descriptor: then Windows does not say what the device is.
    /// </summary>
    public static IReadOnlyList<int>? InterfaceClasses(byte[] buffer, int bytesReturned)
    {
        if (TotalLength(buffer, bytesReturned) is not { } total || DataOffset + total > bytesReturned)
        {
            return null;
        }
        var data = buffer.AsSpan(DataOffset, total);
        var classes = new List<int>();
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset];
            if (length < 2 || offset + length > data.Length)
            {
                return null;
            }
            if (data[offset + 1] == InterfaceType)
            {
                if (length < InterfaceLength)
                {
                    return null;
                }
                classes.Add(data[offset + InterfaceClassOffset]);
            }
            offset += length;
        }
        return classes;
    }
}
