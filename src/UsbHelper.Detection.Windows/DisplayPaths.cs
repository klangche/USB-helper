namespace UsbHelper.Detection.Windows;

/// <summary>Reads the targets out of a QueryDisplayConfig path array (DISPLAYCONFIG_PATH_INFO, wingdi.h).</summary>
internal static class DisplayPaths
{
    public const int DISPLAYCONFIG_PATH_INFO_SIZE = 72; // source info 20, target info 48, flags 4
    public const int DISPLAYCONFIG_PATH_TARGET_ADAPTER_OFFSET = 20; // LUID (8), then target id (4)
    public const int DISPLAYCONFIG_PATH_TARGET_ID_OFFSET = 28;
    public const int DISPLAYCONFIG_PATH_OUTPUT_TECHNOLOGY_OFFSET = 36; // after modeInfoIdx (4)

    /// <summary>Each target once, in path order. In clone mode several paths can lead to the same target.</summary>
    public static IReadOnlyList<(long Adapter, uint Target, uint Technology)> Targets(byte[] paths, uint pathCount)
    {
        var targets = new List<(long Adapter, uint Target, uint Technology)>();
        for (var i = 0; i < pathCount; i++)
        {
            var offset = i * DISPLAYCONFIG_PATH_INFO_SIZE;
            var target = (BitConverter.ToInt64(paths, offset + DISPLAYCONFIG_PATH_TARGET_ADAPTER_OFFSET),
                BitConverter.ToUInt32(paths, offset + DISPLAYCONFIG_PATH_TARGET_ID_OFFSET),
                BitConverter.ToUInt32(paths, offset + DISPLAYCONFIG_PATH_OUTPUT_TECHNOLOGY_OFFSET));
            if (!targets.Any(known => known.Adapter == target.Item1 && known.Target == target.Item2))
            {
                targets.Add(target);
            }
        }
        return targets;
    }
}
