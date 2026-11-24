using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.Core.Topology;

/// <summary>What a row at the top of the tree by port stands for (#370).</summary>
public enum PortGroupKind
{
    /// <summary>One of the computer's own ports, both halves of a USB-C connector together.</summary>
    Port,

    /// <summary>Devices inside the computer: in the computer's own container, or on a port the system calls internal.</summary>
    BuiltIn,

    /// <summary>What cannot be placed on one of the computer's ports; <see cref="PortGroup.Reasons"/> says why.</summary>
    Other,
}

/// <summary>
/// The USB4 routers on a port's link (#359), shown as their own row between the port and its hubs (#370).
/// </summary>
/// <param name="Names">The routers as the system names them, e.g. "PS9010"; null when it gives no names.</param>
/// <param name="Depth">The deepest router's depth from the computer's host router: a dock is 1, a dock behind it 2.</param>
public sealed record PortRouter(string Id, string? Names, int Depth);

/// <summary>A row at the top of the tree by port (#370), with the ports whose hubs and devices go under it.</summary>
/// <param name="Nodes">
/// The ports (with what is on them) shown under the row, in the tree's order; for a <see cref="PortGroupKind.Port"/>
/// its halves, USB 3 first. A hub whose two halves are on the two halves of a connector is one hub on the USB 3 half
/// (see <see cref="PortTree.OtherHalves"/>), and the USB 2 half is left empty. <see cref="PortGroupKind.Other"/> also
/// has the controllers that could not be read, without their children.
/// </param>
public sealed record PortGroup(string Id, PortGroupKind Kind, string Name, IReadOnlyList<UsbNode> Nodes)
{
    /// <summary>For a port: the USB4 routers between it and its hubs, or null when none is known to be on it.</summary>
    public PortRouter? Router { get; init; }

    /// <summary>For <see cref="PortGroupKind.Other"/>: why each part could not be placed, in English, for its hover.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>For a port: true when the system says it is USB-C, as <see cref="ComputerPort.UsbC"/>.</summary>
    public bool? UsbC { get; init; }

    /// <summary>The controllers the group's ports are on, so the selected controller's port can be opened.</summary>
    public IReadOnlyList<string> ControllerIds { get; init; } = [];
}

/// <summary>The USB 2 half of a hub shown as one with its USB 3 half (#370), and the port it is on.</summary>
public sealed record HubHalf(UsbNode Hub, string PortId);

/// <summary>
/// The tree by the computer's physical ports (#370), for the window, the text and the report: a row per port with
/// something on it, both halves of a USB-C connector together (#358), then the devices built into the computer, then
/// what cannot be placed. Controllers and root hubs are no rows: they are in the port's hover (#266). The two halves of
/// a USB 3 hub are one hub. The counts and the diagnosis use the tree itself, per bus, as before: this is only how it is
/// shown.
/// </summary>
/// <param name="OtherHalves">For each hub shown with its other half, by the USB 3 half's id: the USB 2 half.</param>
/// <param name="Buses">The bus of the port each other hub and device is on, by its id, where the system says it.</param>
public sealed record PortTree(IReadOnlyList<PortGroup> Groups, IReadOnlyDictionary<string, HubHalf> OtherHalves,
    IReadOnlyDictionary<string, UsbBus> Buses)
{
    public const string BuiltInId = "group|built-in";

    /// <summary>The ids of the halves of the computer's ports shown as rows: the row is the port, so they are no rows.</summary>
    public IReadOnlySet<string> PortHalves { get; } =
        Groups.Where(group => group.Kind == PortGroupKind.Port).SelectMany(group => group.Nodes).Select(node => node.Id).ToHashSet();

    public const string OtherId = "group|other";

    /// <summary>The group id of the port whose first half is <paramref name="portId"/>.</summary>
    public static string PortGroupId(string portId) => "port|" + portId;

    /// <param name="known">The halves of each connector seen this session (<see cref="PortTesting.Pairs"/>), or null.</param>
    public static PortTree Build(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, string>? known = null)
    {
        var halves = new Dictionary<string, HubHalf>();
        var controllerOf = new Dictionary<string, UsbNode>();
        foreach (var controller in tree)
        {
            foreach (var port in controller.Children.SelectMany(Ports))
            {
                controllerOf[port.Id] = controller;
            }
        }

        var builtIn = new List<UsbNode>();
        var other = new List<UsbNode>();
        var reasons = new List<string>();
        var placedElsewhere = new HashSet<string>();
        foreach (var rootHub in tree.SelectMany(controller => controller.Children))
        {
            SortInternal(rootHub, builtIn, other, reasons, placedElsewhere);
        }
        foreach (var controller in tree.Where(controller => controller.Problem != null))
        {
            other.Add(controller with { Children = [] });
            reasons.Add($"{controller.Name}: {controller.Problem}");
        }

        var connectors = PortTesting.Ports(tree, known)
            .Where(each => !each.Halves.Any(half => placedElsewhere.Contains(half.Id)))
            .ToList();
        // The routers of a controller are on the one connector of it that has something on it; with several the system
        // does not say which, so they are not placed.
        var occupied = connectors.Where(each => each.Halves.Any(half => half.Children.Count > 0)).ToList();
        PortRouter? RouterOf(IReadOnlyList<UsbNode> connector)
        {
            var controller = connector.Select(half => controllerOf.GetValueOrDefault(half.Id)).OfType<UsbNode>()
                .FirstOrDefault(each => each.Usb4Routers is > 0);
            if (controller == null)
            {
                return null;
            }
            var on = occupied.Where(each => each.Halves.Any(half => controllerOf.GetValueOrDefault(half.Id) == controller)).ToList();
            return on.Count == 1 && ReferenceEquals(on[0].Halves, connector) ? Router(controller) : null;
        }
        foreach (var controller in tree.Where(controller => controller.Usb4Routers is > 0))
        {
            var on = occupied.Count(each => each.Halves.Any(half => controllerOf.GetValueOrDefault(half.Id) == controller));
            if (on > 1)
            {
                reasons.Add($"{Router(controller).Names ?? "A USB4 router"}: on {controller.Name}, which has {on} ports in use; the system does not say which one it is on.");
            }
        }

        var groups = new List<PortGroup>();
        foreach (var (port, connector) in connectors)
        {
            var nodes = connector.SelectMany(half => half.Children).ToList();
            if (nodes.Count > 0 && nodes.All(node => node.Kind == UsbNodeKind.Device && node.InLocalMachine == true))
            {
                builtIn.AddRange(connector.Where(half => half.Children.Count > 0));
                continue;
            }
            IReadOnlyList<UsbNode> shown = connector is [var usb3, var usb2] && usb3.Bus == UsbBus.Usb3 && usb2.Bus != UsbBus.Usb3
                && usb3.Children is [var hub3] && usb2.Children is [var hub2] && SameHub(hub3, hub2)
                ? [usb3 with { Children = [Merge(hub3, hub2, usb2.Id, halves)] }, usb2 with { Children = [] }]
                : connector;
            groups.Add(new PortGroup(PortGroupId(port.Id), PortGroupKind.Port, port.Name, shown)
            {
                Router = RouterOf(connector),
                UsbC = port.UsbC,
                ControllerIds = [.. connector.Select(half => controllerOf.GetValueOrDefault(half.Id)?.Id).OfType<string>().Distinct()],
            });
        }
        if (builtIn.Count > 0)
        {
            groups.Add(new PortGroup(BuiltInId, PortGroupKind.BuiltIn, "Built into the computer", builtIn)
            {
                ControllerIds = [.. builtIn.Select(port => controllerOf.GetValueOrDefault(port.Id)?.Id).OfType<string>().Distinct()],
            });
        }
        if (other.Count > 0 || reasons.Count > 0)
        {
            groups.Add(new PortGroup(OtherId, PortGroupKind.Other, "Other USB ports (not identified)", other) { Reasons = reasons });
        }

        var buses = new Dictionary<string, UsbBus>();
        foreach (var node in groups.SelectMany(group => group.Nodes))
        {
            AddBuses(node, halves, buses);
        }
        return new PortTree(groups, halves, buses);
    }

    // Every port of a hub and of the hubs below it.
    private static IEnumerable<UsbNode> Ports(UsbNode hub) =>
        hub.Children.Where(port => port.Kind == UsbNodeKind.Port)
            .SelectMany(port => port.Children.Where(child => child.Kind == UsbNodeKind.Hub).SelectMany(Ports).Prepend(port));

    // The internal ports of a root hub or built-in hub (as PortTesting.OwnPortNodes walks them): a device on one is built
    // in, unless the system says it is outside the computer; a hub outside the computer on one cannot be placed. The
    // ports of such a hub are no computer ports.
    private static void SortInternal(UsbNode hub, List<UsbNode> builtIn, List<UsbNode> other, List<string> reasons, HashSet<string> placedElsewhere)
    {
        foreach (var port in hub.Children.Where(port => port.Kind == UsbNodeKind.Port && port.UserConnectable == false))
        {
            foreach (var node in port.Children)
            {
                if (node.InLocalMachine == false)
                {
                    other.Add(port);
                    reasons.Add($"{node.Name}: the system calls {port.Name} internal, but says the device is not part of the computer.");
                    if (node.Kind == UsbNodeKind.Hub)
                    {
                        placedElsewhere.UnionWith(Ports(node).Select(each => each.Id));
                    }
                }
                else if (node.Kind == UsbNodeKind.Hub)
                {
                    SortInternal(node, builtIn, other, reasons, placedElsewhere);
                }
                else
                {
                    builtIn.Add(port);
                }
            }
        }
    }

    // The routers by their model, as macOS names them ("PS9010"): Windows names a router "USB4-router (1.0), Parade
    // Technologies – PS9010" (the owner's Asus on #354), its kind and vendor before the model.
    private static PortRouter Router(UsbNode controller) => new(
        "usb4|" + controller.Id,
        controller.Usb4Link?.IndexOf(": ", StringComparison.Ordinal) is >= 0 and var at
            ? System.Text.RegularExpressions.Regex.Replace(controller.Usb4Link[(at + 2)..], @"[^,]+, [^,–]+ – ", "")
            : null,
        controller.Usb4Routers ?? 0);

    // The two halves of one USB 3 hub: both hubs, and in the same container when the system gives both one (#267).
    private static bool SameHub(UsbNode usb3, UsbNode usb2) =>
        usb3.Kind == UsbNodeKind.Hub && usb2.Kind == UsbNodeKind.Hub
        && (usb3.ContainerId == null || usb2.ContainerId == null || usb3.ContainerId == usb2.ContainerId);

    // The USB 3 half with the ports of both halves, by port number, the USB 3 port first. A hub on a USB 3 port and a hub
    // on its companion (or, when the system names none, the USB 2 port of the same number) are one hub again.
    private static UsbNode Merge(UsbNode usb3, UsbNode usb2, string usb2PortId, Dictionary<string, HubHalf> halves)
    {
        halves[usb3.Id] = new HubHalf(usb2, usb2PortId);
        var emptied = new Dictionary<string, UsbNode>();
        var ports3 = new List<UsbNode>();
        foreach (var port3 in usb3.Children)
        {
            var port2 = usb2.Children.FirstOrDefault(port => port3.CompanionPortId == port.Id || port.CompanionPortId == port3.Id)
                ?? usb2.Children.FirstOrDefault(port => port3.PortNumber != null && port.PortNumber == port3.PortNumber);
            if (port2 != null && !emptied.ContainsKey(port2.Id) && port3.Children is [var hub3] && port2.Children is [var hub2] && SameHub(hub3, hub2))
            {
                ports3.Add(port3 with { Children = [Merge(hub3, hub2, port2.Id, halves)] });
                emptied[port2.Id] = port2 with { Children = [] };
            }
            else
            {
                ports3.Add(port3);
            }
        }
        var ports = ports3.Select(port => (Port: port, Usb3: true))
            .Concat(usb2.Children.Select(port => (Port: emptied.GetValueOrDefault(port.Id) ?? port, Usb3: false)))
            .OrderBy(each => each.Port.PortNumber ?? int.MaxValue)
            .ThenBy(each => each.Usb3 ? 0 : 1)
            .Select(each => each.Port)
            .ToList();
        return usb3 with
        {
            Name = usb3.Name == usb2.Name ? usb3.Name : $"{usb3.Name} + {usb2.Name}",
            Children = ports,
            Problem = usb3.Problem ?? usb2.Problem,
        };
    }

    private static void AddBuses(UsbNode port, IReadOnlyDictionary<string, HubHalf> halves, Dictionary<string, UsbBus> buses)
    {
        foreach (var node in port.Children)
        {
            if (port.Bus is { } bus && !halves.ContainsKey(node.Id))
            {
                buses[node.Id] = bus;
            }
            foreach (var below in node.Children.Where(child => child.Kind == UsbNodeKind.Port))
            {
                AddBuses(below, halves, buses);
            }
        }
    }
}
