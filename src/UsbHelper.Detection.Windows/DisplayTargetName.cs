using System.Text;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// The DISPLAYCONFIG_TARGET_DEVICE_NAME packet (wingdi.h) for DisplayConfigGetDeviceInfo: a 20-byte header (type, size,
/// adapter LUID, target id), flags, output technology, EDID manufacturer and product ids, connector instance, then
/// monitorFriendlyDeviceName (64 UTF-16 characters) and monitorDevicePath (128).
/// </summary>
internal static class DisplayTargetName
{
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const int DISPLAYCONFIG_TARGET_DEVICE_NAME_SIZE = 420; // header 20, then 16, name 128, device path 256
    private const int NameOffset = 36;
    private const int NameLength = 64;
    private const int DevicePathOffset = 164;
    private const int DevicePathLength = 128;

    /// <summary>A request for the name of <paramref name="target"/> on <paramref name="adapter"/>; the reply is written into it.</summary>
    public static byte[] Request(long adapter, uint target)
    {
        var packet = new byte[DISPLAYCONFIG_TARGET_DEVICE_NAME_SIZE];
        BitConverter.GetBytes(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME).CopyTo(packet, 0);
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 4);
        BitConverter.GetBytes(adapter).CopyTo(packet, 8);
        BitConverter.GetBytes(target).CopyTo(packet, 16);
        return packet;
    }

    /// <summary>The monitor's friendly name from a reply, usually from its EDID; null when it has none.</summary>
    public static string? Name(byte[] reply) => Text(reply, NameOffset, NameLength);

    /// <summary>The monitor's device interface path from a reply (#143); null when it has none.</summary>
    public static string? DevicePath(byte[] reply) => Text(reply, DevicePathOffset, DevicePathLength);

    private static string? Text(byte[] reply, int offset, int length)
    {
        var text = Encoding.Unicode.GetString(reply, offset, length * 2);
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        text = (end < 0 ? text : text[..end]).Trim();
        return text.Length == 0 ? null : text;
    }
}
