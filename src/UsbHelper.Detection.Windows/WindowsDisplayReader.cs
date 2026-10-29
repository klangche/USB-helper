using System.ComponentModel;
using System.Runtime.Versioning;
using static UsbHelper.Detection.Windows.NativeMethods;

namespace UsbHelper.Detection.Windows;

/// <summary>Counts active external displays with QueryDisplayConfig. No administrator rights. Reads no bandwidth or refresh rate.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsDisplayReader
{
    /// <exception cref="Win32Exception">Windows could not report the display configuration.</exception>
    public static int CountExternalDisplays()
    {
        while (true)
        {
            var result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
            if (result != ERROR_SUCCESS)
            {
                throw new Win32Exception(result);
            }
            var paths = new byte[pathCount * DISPLAYCONFIG_PATH_INFO_SIZE];
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

            // In clone mode several paths can lead to the same display; count each display once.
            var displays = new HashSet<(long Adapter, uint Target)>();
            for (var i = 0; i < pathCount; i++)
            {
                var offset = i * DISPLAYCONFIG_PATH_INFO_SIZE;
                if (DisplayOutputs.IsExternal(BitConverter.ToUInt32(paths, offset + DISPLAYCONFIG_PATH_OUTPUT_TECHNOLOGY_OFFSET)))
                {
                    displays.Add((BitConverter.ToInt64(paths, offset + DISPLAYCONFIG_PATH_TARGET_ADAPTER_OFFSET),
                        BitConverter.ToUInt32(paths, offset + DISPLAYCONFIG_PATH_TARGET_ID_OFFSET)));
                }
            }
            return displays.Count;
        }
    }
}
