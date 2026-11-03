namespace UsbHelper.Detection.Windows;

/// <summary>
/// DEVPKEY_Device_ContainerId (devpkey.h): the physical device a device node belongs to (#143). A monitor and the USB hub
/// built into it share one.
/// </summary>
internal static class ContainerIds
{
    public const uint DevpropTypeGuid = 0x0D; // DEVPROP_TYPE_GUID, devpropdef.h

    /// <summary>The container id from a property reply; null for another type, a short reply or the empty GUID.</summary>
    public static Guid? FromProperty(uint type, byte[] buffer, uint size) =>
        type == DevpropTypeGuid && size == 16 && new Guid(buffer.AsSpan(0, 16)) is var id && id != Guid.Empty ? id : null;

    /// <summary>
    /// The device instance id behind a device interface path: <c>\\?\DISPLAY#SAM0F0E#5&amp;...#{interface guid}</c> is
    /// <c>DISPLAY\SAM0F0E\5&amp;...</c>. Null when the path has no such form.
    /// </summary>
    public static string? InstanceId(string interfacePath)
    {
        var path = interfacePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? interfacePath[4..] : interfacePath;
        var end = path.LastIndexOf("#{", StringComparison.Ordinal);
        if (end > 0)
        {
            path = path[..end];
        }
        return path.Length == 0 ? null : path.Replace('#', '\\');
    }
}
