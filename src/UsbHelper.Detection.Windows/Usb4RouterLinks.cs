using System.Globalization;

namespace UsbHelper.Detection.Windows;

/// <summary>One node of Windows' USB4 enumerator (#332): its instance id, its parent's and its name.</summary>
internal sealed record Usb4Node(string InstanceId, string? ParentId, string Name);

/// <summary>
/// The USB4 routers of each host router, and the USB controller each host router's USB 3 tunnel lands on (#332). From the
/// owner's Asus with a Parade PS9010 dock (#267, #354): Microsoft's connection manager lists a root device router
/// (<c>USB4\ROOT_DEVICE_ROUTER…</c>) under each host router (a PCI device), and a dock as a device router under it, so a
/// dock is at depth 1. The host routers (PCI device 13, functions 2 and 3) sit on the same PCI device as the Type-C xHCI
/// controller (function 0) that carries the dock's USB 3; its USB 2 runs on the PCH controller, not through a router.
/// So a controller is linked to the host routers on its own PCI device. Seen on one PC so far.
/// </summary>
internal static class Usb4RouterLinks
{
    private const string RootDeviceRouter = @"USB4\ROOT_DEVICE_ROUTER";

    /// <summary>
    /// For each host router (by instance id): the deepest device router below it, 0 when none, and the routers' names.
    /// Nodes that are not routers (a virtual power device) are left out.
    /// </summary>
    public static IReadOnlyDictionary<string, (int Depth, IReadOnlyList<string> Names)> Depths(IReadOnlyList<Usb4Node> nodes)
    {
        var byId = nodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
        var hosts = new Dictionary<string, (int Depth, IReadOnlyList<string> Names)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in nodes.Where(IsRoot))
        {
            if (root.ParentId is { } host)
            {
                hosts[host] = (0, []);
            }
        }
        foreach (var node in nodes.Where(node => !IsRoot(node) && IsDeviceRouter(node)))
        {
            var depth = 0;
            var current = node;
            while (current != null && !IsRoot(current) && depth <= 16)
            {
                depth++;
                current = current.ParentId is { } parent && byId.TryGetValue(parent, out var up) ? up : null;
            }
            if (current?.ParentId is { } host && hosts.TryGetValue(host, out var known))
            {
                hosts[host] = (Math.Max(known.Depth, depth), [.. known.Names, node.Name]);
            }
        }
        return hosts;
    }

    /// <summary>
    /// The PCI device a node is on, from its location paths: the first <c>PCIROOT(…)#PCI(DDFF)…</c> path, cut before the
    /// last function number, e.g. <c>PCIROOT(0)#PCI(0D</c> for <c>PCIROOT(0)#PCI(0D00)</c>. Null when there is none.
    /// </summary>
    public static string? Slot(IReadOnlyList<string> locationPaths) =>
        locationPaths.FirstOrDefault(path => path.StartsWith("PCIROOT(", StringComparison.OrdinalIgnoreCase)) is { } path
            && path.LastIndexOf("#PCI(", StringComparison.OrdinalIgnoreCase) is var at and >= 0
            && path.IndexOf(')', at) is var end and > 0 && end - at - 5 == 4
            && int.TryParse(path.AsSpan(at + 5, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
                ? path[..(at + 7)].ToUpperInvariant()
                : null;

    // A device router: a node of the USB4 enumerator other than the virtual power device.
    private static bool IsDeviceRouter(Usb4Node node) =>
        node.InstanceId.StartsWith(@"USB4\", StringComparison.OrdinalIgnoreCase)
        && !node.InstanceId.StartsWith(@"USB4\VIRTUAL_POWER_PDO", StringComparison.OrdinalIgnoreCase);

    private static bool IsRoot(Usb4Node node) => node.InstanceId.StartsWith(RootDeviceRouter, StringComparison.OrdinalIgnoreCase);
}
