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
            return [.. DisplayPaths.Targets(paths, pathCount).Select(Monitor)];
        }
    }

    /// <summary>Counts active external displays (see <see cref="MonitorOutputs.IsExternal"/>).</summary>
    /// <exception cref="Win32Exception">Windows could not report the display configuration.</exception>
    public static int CountExternalDisplays() => DisplayMonitor.ExternalCount(ReadMonitors())!.Value;

    // No name or container rather than no monitor when Windows cannot name one.
    private static DisplayMonitor Monitor((long Adapter, uint Target, uint Technology) target)
    {
        var packet = DisplayTargetName.Request(target.Adapter, target.Target);
        var known = DisplayConfigGetDeviceInfo(packet) == ERROR_SUCCESS;
        return new DisplayMonitor(
            string.Create(CultureInfo.InvariantCulture, $"{target.Adapter:X16}:{target.Target}"),
            known ? DisplayTargetName.Name(packet) : null,
            DisplayOutputs.Kind(target.Technology))
        {
            ContainerId = known && DisplayTargetName.DevicePath(packet) is { } path ? ContainerId(path) : null,
        };
    }

    // The monitor's device node, found from its interface path, and that node's container id (#143).
    private static Guid? ContainerId(string devicePath)
    {
        if (ContainerIds.InstanceId(devicePath) is not { } instanceId || CM_Locate_DevNode(out var devInst, instanceId, 0) != CR_SUCCESS)
        {
            return null;
        }
        var buffer = new byte[16];
        uint size = 16;
        return CM_Get_DevNode_Property(devInst, DevpkeyDeviceContainerId, out var type, buffer, ref size, 0) == CR_SUCCESS
            ? ContainerIds.FromProperty(type, buffer, size)
            : null;
    }
}
