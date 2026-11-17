using System.Globalization;
using System.Text;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// The kind of a device from the compatible ids Windows gave its device node and its interface child nodes (#274, owner
/// decision 2026-11-17: "both"). Windows builds them from the descriptors when it enumerates the device, so reading them
/// sends nothing to the device: every interface child (MI_nn) of a composite device has <c>USB\Class_xx</c> from its
/// interface, a composite device itself only <c>USB\DevClass_xx</c> and <c>USB\COMPOSITE</c>. A non-composite device
/// has <c>USB\Class_xx</c> from its interface only when its bDeviceClass is 0; otherwise it is the device class, which
/// says nothing about the interfaces.
/// </summary>
internal static class CompatibleIds
{
    private const string ClassPrefix = @"USB\Class_";

    /// <summary>
    /// The interface classes from the compatible ids of <paramref name="device"/> (only when its
    /// <paramref name="deviceClass"/>, bDeviceClass, is 0) and its <paramref name="interfaces"/>. When they give none (no
    /// device node, a composite device whose interface nodes do not exist because its composite driver did not load, or
    /// a device with its own device class), <paramref name="readDescriptor"/> reads them from the device. Null: unknown.
    /// </summary>
    public static IReadOnlyList<int>? InterfaceClasses(
        int deviceClass, RawDevNode? device, IReadOnlyList<RawDevNode> interfaces, Func<IReadOnlyList<int>?> readDescriptor)
    {
        IEnumerable<RawDevNode> nodes = device == null ? [] : deviceClass == 0 ? interfaces.Prepend(device) : interfaces;
        var classes = nodes.Select(node => Class(node.CompatibleIds)).OfType<int>().ToList();
        return classes.Count > 0 ? classes : readDescriptor();
    }

    /// <summary>The class xx of the first <c>USB\Class_xx</c> id, or null when there is none.</summary>
    public static int? Class(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            if (id.StartsWith(ClassPrefix, StringComparison.OrdinalIgnoreCase) && id.Length >= ClassPrefix.Length + 2
                && int.TryParse(id.AsSpan(ClassPrefix.Length, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>The strings of a REG_MULTI_SZ reply of <paramref name="size"/> bytes.</summary>
    public static IReadOnlyList<string> Parse(byte[] buffer, uint size) =>
        Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
