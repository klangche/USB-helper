namespace UsbHelper.Detection.Windows;

/// <summary>Hub symbolic link names, as returned by the root hub and connection name IOCTLs.</summary>
internal static class HubName
{
    /// <summary><c>USB#ROOT_HUB30#4&amp;1a2b&amp;0&amp;0#{guid}</c> becomes <c>USB\ROOT_HUB30\4&amp;1a2b&amp;0&amp;0</c>.</summary>
    public static string ToInstanceId(string symbolicName)
    {
        var end = symbolicName.LastIndexOf("#{", StringComparison.Ordinal);
        return (end < 0 ? symbolicName : symbolicName[..end]).Replace('#', '\\');
    }

    public static string ToDevicePath(string symbolicName) => @"\\.\" + symbolicName;
}
