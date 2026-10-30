using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Topology;

/// <summary>Where a hub or device sits in the tree.</summary>
/// <param name="Tier">USB tier: the computer's own port is on tier 1, each hub below it adds one, the node's own tier counts.</param>
/// <param name="Hops">Hubs between the computer's own port and the node.</param>
public sealed record ConnectionDepth(int Tier, int Hops);

/// <summary>
/// The counting rules for the three topology limits (owner decisions in #2 and #39).
/// Example: laptop port, dock hub, monitor hub, keyboard: the keyboard is tier 4 with 2 hops, and there are 2 hubs.
/// Counting starts at the computer's own (user-connectable) port: a hub reached from the root hub only through ports
/// that are not user-connectable is built into the computer and is not counted; it has no depth. Hubs inside docks
/// and monitors count like any other hub, because the operating system reports them as hubs. A hub on a port the
/// operating system says nothing about is counted (<see cref="PossiblyBuiltIn"/>).
/// A hub that could not be read (it has a <see cref="UsbNode.Problem"/> and no ports) counts as one hub with
/// nothing below it, so the counts are then minimums; the diagnostics must report that case as unknown.
/// </summary>
public static class TopologyCounts
{
    /// <summary>Number of external hubs connected to the computer. Root hubs and built-in hubs are not counted.</summary>
    public static int HubCount(IReadOnlyList<UsbNode> tree) => Counted(tree).Count(entry => entry.Node.Kind == UsbNodeKind.Hub);

    /// <summary>
    /// Tier and hop depth of every connected hub and device, by id. Root hubs, built-in hubs and controllers are not
    /// included.
    /// </summary>
    public static IReadOnlyDictionary<string, ConnectionDepth> Depths(IReadOnlyList<UsbNode> tree) =>
        Counted(tree).ToDictionary(entry => entry.Node.Id, entry => entry.Depth);

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

    /// <summary>
    /// Hubs counted as external although the operating system does not say whether their port is built into the
    /// computer: hubs on a port of a root hub or built-in hub whose <see cref="UsbNode.UserConnectable"/> is null.
    /// </summary>
    public static IEnumerable<UsbNode> PossiblyBuiltIn(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(controller => controller.Children).SelectMany(PossiblyBuiltInBelow);

    private static IEnumerable<(UsbNode Node, ConnectionDepth Depth)> Counted(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(controller => controller.Children).SelectMany(rootHub => Below(rootHub, hubsAbove: 1, builtIn: true));

    // hubsAbove counts the hub itself and every counted hub above it, root hub included. builtIn: the hub is the root
    // hub or built into the computer, so its ports may be the computer's own.
    private static IEnumerable<(UsbNode Node, ConnectionDepth Depth)> Below(UsbNode hub, int hubsAbove, bool builtIn)
    {
        foreach (var port in hub.Children)
        {
            foreach (var connected in port.Children)
            {
                if (connected.Kind == UsbNodeKind.Hub && builtIn && port.UserConnectable == false)
                {
                    foreach (var entry in Below(connected, hubsAbove, builtIn: true))
                    {
                        yield return entry;
                    }
                    continue;
                }
                yield return (connected, new ConnectionDepth(Tier: hubsAbove + 1, Hops: hubsAbove - 1));
                if (connected.Kind == UsbNodeKind.Hub)
                {
                    foreach (var entry in Below(connected, hubsAbove + 1, builtIn: false))
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
