using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Topology;

/// <summary>Where a hub or device sits in the tree.</summary>
/// <param name="Tier">USB tier: the computer's own port is on tier 1, each hub below it adds one, the node's own tier counts.</param>
public sealed record ConnectionDepth(int Tier);

/// <summary>Hub count and deepest tier on one bus.</summary>
public sealed record BusCounts(int Hubs, int DeepestTier);

/// <summary>
/// The counts per bus (#195). A USB 3 hub is two hubs in one box, one on each bus, and each bus has its own tree and
/// limits, so the halves are not added up.
/// </summary>
/// <param name="BusUnknown">
/// True when a counted hub or device is on a port whose bus the operating system does not report. It is in neither
/// bus's numbers, so they are then minimums.
/// </param>
/// <param name="AnyBusKnown">True when at least one counted hub or device is on a port with a known bus.</param>
public sealed record PerBusCounts(BusCounts Usb2, BusCounts Usb3, bool BusUnknown, bool AnyBusKnown);

/// <summary>
/// The counting rules for the topology limits, hubs and tiers (owner decisions in #2, #39 and #297).
/// Example: laptop port, dock hub, monitor hub, keyboard: the keyboard is tier 4, and there are 2 hubs.
/// Counting starts at the computer's own (user-connectable) port: a hub reached from the root hub only through ports
/// that are not user-connectable is built into the computer and is not counted; it has no depth. Hubs inside docks
/// and monitors count like any other hub, because the operating system reports them as hubs. A hub on a port the
/// operating system says nothing about is counted (<see cref="PossiblyBuiltIn"/>).
/// A hub that could not be read (it has a <see cref="UsbNode.Problem"/> and no ports) counts as one hub with
/// nothing below it, so the counts are then minimums; the diagnostics must report that case as unknown.
/// Active cables and extenders the operating system does not show are marked by the user per port (#45): a mark of
/// N adds N tiers to everything behind that port and min(N, 2) hubs on that port's bus (#297). On a port whose bus is
/// not reported, those hubs count toward the total only, like the hubs on it. A mark on a port with nothing on it
/// adds nothing.
/// </summary>
public static class TopologyCounts
{
    /// <summary>
    /// Number of external hubs connected to the computer, the hubs added by the user's marks included. Root hubs and
    /// built-in hubs are not counted.
    /// </summary>
    /// <param name="marks">The user's marks per port id, for active cables and extenders the operating system does not show.</param>
    public static int HubCount(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, int>? marks = null)
    {
        var entries = Counted(tree, marks).ToList();
        return entries.Count(entry => entry.Node.Kind == UsbNodeKind.Hub) + MarkedHubs(entries, marks, _ => true);
    }

    /// <summary>
    /// Hubs and deepest tier per bus. A hub or device belongs to the bus of the port it is plugged into. One on a port
    /// with an unknown bus is left out of both and sets <see cref="PerBusCounts.BusUnknown"/>: never guessed.
    /// </summary>
    /// <param name="marks">The user's marks per port id, for active cables and extenders the operating system does not show.</param>
    public static PerBusCounts PerBus(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, int>? marks = null)
    {
        var entries = Counted(tree, marks).ToList();
        BusCounts On(UsbBus bus)
        {
            var onBus = entries.Where(entry => entry.Bus == bus).ToList();
            return new BusCounts(
                onBus.Count(entry => entry.Node.Kind == UsbNodeKind.Hub) + MarkedHubs(entries, marks, port => port.Bus == bus),
                onBus.Select(entry => entry.Depth.Tier).DefaultIfEmpty(0).Max());
        }
        return new PerBusCounts(On(UsbBus.Usb2), On(UsbBus.Usb3),
            entries.Any(entry => entry.Bus == null), entries.Any(entry => entry.Bus != null));
    }

    /// <summary>
    /// The bus of the port each counted hub and device is plugged into, by id; null when the operating system does not
    /// report it.
    /// </summary>
    public static IReadOnlyDictionary<string, UsbBus?> Buses(IReadOnlyList<UsbNode> tree) =>
        Counted(tree, null).ToDictionary(entry => entry.Node.Id, entry => entry.Bus);

    /// <summary>
    /// Tier of every connected hub and device, by id. Root hubs, built-in hubs and controllers are not included.
    /// </summary>
    /// <param name="marks">The user's marks per port id, for active cables and extenders the operating system does not show.</param>
    public static IReadOnlyDictionary<string, ConnectionDepth> Depths(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, int>? marks = null) =>
        Counted(tree, marks).ToDictionary(entry => entry.Node.Id, entry => entry.Depth);

    /// <summary>The hubs a mark of <paramref name="mark"/> adds (#297): as many as the mark, at most 2.</summary>
    public static int MarkHubs(int mark) => Math.Clamp(mark, 0, 2);

    /// <summary>Every element of <paramref name="tree"/> for which <see cref="CannotBeRead"/> is true.</summary>
    public static IEnumerable<UsbNode> Unreadable(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(node => Unreadable(node.Children).Prepend(node)).Where(CannotBeRead);

    /// <summary>
    /// True when what is behind <paramref name="node"/> could not be read: a hub without ports, a controller without a
    /// root hub, or a port with an error and nothing on it. What is behind it is unknown, so the counts are then
    /// minimums. A working hub always has at least one port, and a port with a connection error still shows the device
    /// that caused it. That device is unreadable too when it could not be identified (#185): no vendor and product id,
    /// and a problem or a placeholder id, so it may be a hub.
    /// </summary>
    public static bool CannotBeRead(UsbNode node) =>
        (node.Children.Count == 0
            && (node.Kind is UsbNodeKind.Hub or UsbNodeKind.Controller || (node.Kind == UsbNodeKind.Port && node.Problem != null)))
        || IsUnidentified(node);

    /// <summary>
    /// A device the operating system could not identify (#185): no vendor and product id, and a problem or a placeholder
    /// id (one with <c>|</c>, see <see cref="UsbNode.Id"/>). It may be a hub.
    /// </summary>
    public static bool IsUnidentified(UsbNode node) =>
        node.Kind == UsbNodeKind.Device && node.VendorId == null && node.ProductId == null
        && (node.Problem != null || node.Id.Contains('|', StringComparison.Ordinal));

    /// <summary>
    /// Hubs counted as external although the operating system does not say whether their port is built into the
    /// computer: hubs on a port of a root hub or built-in hub whose <see cref="UsbNode.UserConnectable"/> is null.
    /// </summary>
    public static IEnumerable<UsbNode> PossiblyBuiltIn(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(controller => controller.Children).SelectMany(PossiblyBuiltInBelow);

    // The hubs added by the marks on the ports of the counted hubs and devices that match.
    private static int MarkedHubs(IEnumerable<(UsbNode Node, ConnectionDepth Depth, UsbBus? Bus, UsbNode Port)> entries,
        IReadOnlyDictionary<string, int>? marks, Func<UsbNode, bool> match) =>
        marks == null ? 0
        : entries.Select(entry => entry.Port).DistinctBy(port => port.Id).Where(match).Sum(port => MarkHubs(marks.GetValueOrDefault(port.Id)));

    private static IEnumerable<(UsbNode Node, ConnectionDepth Depth, UsbBus? Bus, UsbNode Port)> Counted(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, int>? marks) =>
        tree.SelectMany(controller => controller.Children).SelectMany(rootHub => Below(rootHub, hubsAbove: 1, builtIn: true, marks));

    // hubsAbove counts the hub itself, every counted hub above it, root hub included, and every tier marked above it.
    // builtIn: the hub is the root hub or built into the computer, so its ports may be the computer's own.
    private static IEnumerable<(UsbNode Node, ConnectionDepth Depth, UsbBus? Bus, UsbNode Port)> Below(UsbNode hub, int hubsAbove, bool builtIn, IReadOnlyDictionary<string, int>? marks)
    {
        foreach (var port in hub.Children)
        {
            var above = hubsAbove + (marks?.GetValueOrDefault(port.Id) ?? 0);
            foreach (var connected in port.Children)
            {
                if (connected.Kind == UsbNodeKind.Hub && builtIn && port.UserConnectable == false)
                {
                    foreach (var entry in Below(connected, above, builtIn: true, marks))
                    {
                        yield return entry;
                    }
                    continue;
                }
                yield return (connected, new ConnectionDepth(Tier: above + 1), port.Bus, port);
                if (connected.Kind == UsbNodeKind.Hub)
                {
                    foreach (var entry in Below(connected, above + 1, builtIn: false, marks))
                    {
                        yield return entry;
                    }
                }
            }
        }
    }

    private static IEnumerable<UsbNode> PossiblyBuiltInBelow(UsbNode builtInHub) =>
        builtInHub.Children.SelectMany(port => port.Children.Where(node => node.Kind == UsbNodeKind.Hub).SelectMany(hub =>
            port.UserConnectable == false ? PossiblyBuiltInBelow(hub)
            : port.UserConnectable == null ? [hub]
            : []));
}
