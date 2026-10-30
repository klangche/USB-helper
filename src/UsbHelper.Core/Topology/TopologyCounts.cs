using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Topology;

/// <summary>Where a hub or device sits in the tree.</summary>
/// <param name="Tier">USB tier: the root hub is tier 1, each hub below adds one, the node's own tier counts.</param>
/// <param name="Hops">Hubs between the root port and the node, root hub excluded.</param>
public sealed record ConnectionDepth(int Tier, int Hops);

/// <summary>
/// The counting rules for the three topology limits (owner decision in #2).
/// Example: root port, dock hub, monitor hub, keyboard: the keyboard is tier 4 with 2 hops, and there are 2 hubs.
/// Hubs inside docks and monitors count like any other hub, because the operating system reports them as hubs.
/// A hub that could not be read (it has a <see cref="UsbNode.Problem"/> and no ports) counts as one hub with
/// nothing below it, so the counts are then minimums; the diagnostics must report that case as unknown.
/// </summary>
public static class TopologyCounts
{
    /// <summary>Number of external hubs connected to the computer. Root hubs are not counted.</summary>
    public static int HubCount(IReadOnlyList<UsbNode> tree) => tree.SelectMany(controller => controller.Children).Sum(HubsBelow);

    /// <summary>Tier and hop depth of every connected hub and device, by id. Root hubs and controllers are not included.</summary>
    public static IReadOnlyDictionary<string, ConnectionDepth> Depths(IReadOnlyList<UsbNode> tree)
    {
        var depths = new Dictionary<string, ConnectionDepth>();
        foreach (var rootHub in tree.SelectMany(controller => controller.Children))
        {
            AddBelow(rootHub, hubsAbove: 1, depths);
        }
        return depths;
    }

    /// <summary>Every element of <paramref name="tree"/> for which <see cref="CannotBeRead"/> is true.</summary>
    public static IEnumerable<UsbNode> Unreadable(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(node => Unreadable(node.Children).Prepend(node)).Where(CannotBeRead);

    /// <summary>
    /// True when what is behind <paramref name="node"/> could not be read: a hub without ports, a controller without a
    /// root hub, or a port with an error and nothing on it. What is behind it is unknown, so the counts are then
    /// minimums. A working hub always has at least one port, and a port with a connection error still shows the device
    /// that caused it.
    /// </summary>
    public static bool CannotBeRead(UsbNode node) =>
        node.Children.Count == 0
        && (node.Kind is UsbNodeKind.Hub or UsbNodeKind.Controller || (node.Kind == UsbNodeKind.Port && node.Problem != null));

    // hubsAbove counts the hub itself and every hub above it, root hub included.
    private static void AddBelow(UsbNode hub, int hubsAbove, Dictionary<string, ConnectionDepth> depths)
    {
        foreach (var connected in hub.Children.SelectMany(port => port.Children))
        {
            depths[connected.Id] = new ConnectionDepth(Tier: hubsAbove + 1, Hops: hubsAbove - 1);
            if (connected.Kind == UsbNodeKind.Hub)
            {
                AddBelow(connected, hubsAbove + 1, depths);
            }
        }
    }

    private static int HubsBelow(UsbNode hub) =>
        hub.Children.SelectMany(port => port.Children).Where(node => node.Kind == UsbNodeKind.Hub).Sum(child => 1 + HubsBelow(child));
}
