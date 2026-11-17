using System.Globalization;
using System.Text;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// The kind of a device from the compatible ids Windows gave its device node and its interface child nodes (#274, owner
/// decision 2026-11-17: "both"). Windows builds them from the descriptors when it enumerates the device, so reading them
/// sends nothing to the device: a single-function device and every interface child (MI_nn) of a composite device have
/// <c>USB\Class_xx</c>, a composite device itself only <c>USB\DevClass_xx</c> and <c>USB\COMPOSITE</c>.
/// </summary>
internal static class CompatibleIds
{
    private const string ClassPrefix = @"USB\Class_";

    /// <summary>
    /// The interface classes from the compatible ids of <paramref name="device"/> and its <paramref name="interfaces"/>.
    /// When they give none (no device node, or a composite device whose interface nodes do not exist because its
    /// composite driver did not load), <paramref name="readDescriptor"/> reads them from the device. Null: unknown.
    /// </summary>
    public static IReadOnlyList<int>? InterfaceClasses(
        RawDevNode? device, IReadOnlyList<RawDevNode> interfaces, Func<IReadOnlyList<int>?> readDescriptor)
    {
        IEnumerable<RawDevNode> nodes = device == null ? [] : interfaces.Prepend(device);
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
