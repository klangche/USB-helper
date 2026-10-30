namespace UsbHelper.Detection.Windows;

/// <summary>The name shown for a device node (owner decision in #39).</summary>
internal static class DeviceName
{
    /// <summary>
    /// The product name the device reports on the bus (DEVPKEY_Device_BusReportedDeviceDesc), falling back to the
    /// Windows name (friendly name, then device description), then to the instance id.
    /// </summary>
    public static string Choose(string? busReported, string? friendlyName, string? description, string instanceId) =>
        new[] { busReported, friendlyName, description }.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? instanceId;
}
