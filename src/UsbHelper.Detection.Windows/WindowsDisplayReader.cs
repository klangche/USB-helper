using System.ComponentModel;
using System.Globalization;
using System.Runtime.Versioning;
using UsbHelper.Core.Detection;
using static UsbHelper.Detection.Windows.NativeMethods;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// Reads the active monitors with QueryDisplayConfig and DisplayConfigGetDeviceInfo. No administrator rights. Reads no
/// bandwidth or refresh rate, and does not tie a monitor to a USB port or dock (#93).
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsDisplayReader
{
    /// <summary>Each active monitor once, with its name and output type (#133).</summary>
    /// <exception cref="Win32Exception">Windows could not report the display configuration.</exception>
    public static IReadOnlyList<DisplayMonitor> ReadMonitors()
    {
        while (true)
        {
            var result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
            if (result != ERROR_SUCCESS)
            {
                throw new Win32Exception(result);
            }
            var paths = new byte[pathCount * DisplayPaths.DISPLAYCONFIG_PATH_INFO_SIZE];
            var modes = new byte[modeCount * DISPLAYCONFIG_MODE_INFO_SIZE];
            result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == ERROR_INSUFFICIENT_BUFFER)
            {
                continue; // The configuration changed between the two calls.
            }
            if (result != ERROR_SUCCESS)
            {
                throw new Win32Exception(result);
            }
            return [.. DisplayPaths.Targets(paths, pathCount).Select(target => new DisplayMonitor(
                string.Create(CultureInfo.InvariantCulture, $"{target.Adapter:X16}:{target.Target}"),
                Name(target.Adapter, target.Target),
                DisplayOutputs.Kind(target.Technology)))];
        }
    }

    /// <summary>Counts active external displays (see <see cref="MonitorOutputs.IsExternal"/>).</summary>
    /// <exception cref="Win32Exception">Windows could not report the display configuration.</exception>
    public static int CountExternalDisplays() => DisplayMonitor.ExternalCount(ReadMonitors())!.Value;

    // No name rather than no monitor when Windows cannot name one.
    private static string? Name(long adapter, uint target)
    {
        var packet = DisplayTargetName.Request(adapter, target);
        return DisplayConfigGetDeviceInfo(packet) == ERROR_SUCCESS ? DisplayTargetName.Name(packet) : null;
    }
}
