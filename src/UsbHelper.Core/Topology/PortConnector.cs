using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Topology;

/// <summary>Where a port is, for its hover (#266).</summary>
public enum PortPlace
{
    /// <summary>On the computer's root hub, and the system says it is one of the computer's own ports.</summary>
    ComputerPort,

    /// <summary>On the computer's root hub, and the system says it is internal (not reachable from outside).</summary>
    Internal,

    /// <summary>On the computer's root hub, and the system does not say whether it is one of the computer's own ports.</summary>
    NotSaid,

    /// <summary>On a hub.</summary>
    OnHub,
}

/// <summary>
/// What the app knows about one port's connector (#266): USB-C or not, the data lines in use, the other half of the
/// connector on the other bus, and where it is. What it does not know stays null and is shown as not detected, never
/// guessed: USB-A versus USB-B, the DisplayPort lanes, power and the cable's e-marker are never read.
/// </summary>
/// <param name="UsbC">True when the system says the port is USB-C, false when it says it is not, null when it says nothing.</param>
/// <param name="Bus">The bus of the port's own half, or null when not reported.</param>
/// <param name="Partner">The other half of the connector on the other bus (#342, #355), or null when none is known.</param>
public sealed record PortConnector(string Name, bool? UsbC, UsbBus? Bus, bool InUse, UsbNode? Partner, PortPlace Place)
{
    /// <summary>True when the USB 2 pair carries data now, false when it does not, null when no bus is known.</summary>
    public bool? Usb2InUse => LinesInUse(UsbBus.Usb2);

    /// <summary>True when the USB 3 lanes carry data now, false when they do not, null when no bus is known.</summary>
    public bool? Usb3InUse => LinesInUse(UsbBus.Usb3);

    // Known only from a half on that bus: a half whose bus is not reported says nothing about either line.
    private bool? LinesInUse(UsbBus bus) => Bus == bus || Partner?.Bus == bus
        ? (Bus == bus && InUse) || (Partner?.Bus == bus && Partner.Children.Count > 0)
        : null;

    /// <summary>
    /// The connector of <paramref name="portId"/> in <paramref name="tree"/>, with its other half <paramref name="partnerId"/>
    /// when known; null when the port is not in the tree.
    /// </summary>
    public static PortConnector? Of(IReadOnlyList<UsbNode> tree, string portId, string? partnerId)
    {
        if (Find(tree, portId, null) is not ({ } port, var parent))
        {
            return null;
        }
        partnerId ??= port.CompanionPortId;
        var partner = partnerId == null ? null : Find(tree, partnerId, null)?.Node;
        var onRoot = parent != null && Find(tree, parent.Id, null)?.Parent?.Kind == UsbNodeKind.Controller;
        var place = !onRoot ? PortPlace.OnHub
            : port.UserConnectable switch { true => PortPlace.ComputerPort, false => PortPlace.Internal, null => PortPlace.NotSaid };
        return new PortConnector(port.Name, port.UsbC ?? partner?.UsbC, port.Bus, port.Children.Count > 0, partner, place);
    }

    /// <summary>The ids of <paramref name="id"/> and every element above it, from the controller down (#266, the path to highlight).</summary>
    public static IReadOnlyList<string> PathTo(IReadOnlyList<UsbNode> tree, string id)
    {
        foreach (var node in tree)
        {
            if (node.Id == id)
            {
                return [id];
            }
            if (PathTo(node.Children, id) is { Count: > 0 } below)
            {
                return [node.Id, .. below];
            }
        }
        return [];
    }

    private static (UsbNode Node, UsbNode? Parent)? Find(IReadOnlyList<UsbNode> nodes, string id, UsbNode? parent)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
            {
                return (node, parent);
            }
            if (Find(node.Children, id, node) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
