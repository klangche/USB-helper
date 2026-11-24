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

    /// <summary>For <see cref="PortGroupKind.Other"/>: why each part could not be placed, for its hover.</summary>
    public IReadOnlyList<PortReason> Reasons { get; init; } = [];

    /// <summary>For a port: true when the system says it is USB-C, as <see cref="ComputerPort.UsbC"/>.</summary>
    public bool? UsbC { get; init; }

    /// <summary>The controllers the group's ports are on, so the selected controller's port can be opened.</summary>
    public IReadOnlyList<string> ControllerIds { get; init; } = [];
}

/// <summary>Why something is under "Other USB ports (not identified)" (#370).</summary>
public enum PortReasonKind
{
    /// <summary>The system calls the port <see cref="PortReason.Place"/> internal, but says <see cref="PortReason.Subject"/> is not part of the computer.</summary>
    InternalPortOutsideDevice,

    /// <summary>The controller <see cref="PortReason.Subject"/> could not be read: <see cref="PortReason.Detail"/>.</summary>
    UnreadableController,

    /// <summary>
    /// The USB4 routers <see cref="PortReason.Subject"/> (empty when unnamed) are on controller <see cref="PortReason.Place"/>,
    /// which has <see cref="PortReason.Count"/> ports in use: the system does not say which one they are on.
    /// </summary>
    RouterNotPlaced,
}

/// <summary>One reason for <see cref="PortGroupKind.Other"/>, as names the app words in its own language.</summary>
public sealed record PortReason(PortReasonKind Kind, string Subject, string? Place = null, int Count = 0, string? Detail = null);

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
    /// <param name="missing">The disconnected devices kept at their last ports: their ports keep their place, and a dropped
    /// USB 3 hub is still one hub.</param>
    public static PortTree Build(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, string>? known = null,
        IReadOnlyList<MissingDevice>? missing = null)
    {
        var halves = new Dictionary<string, HubHalf>();
        var controllerOf = new Dictionary<string, UsbNode>();
        var portById = new Dictionary<string, UsbNode>();
        foreach (var controller in tree)
        {
            foreach (var port in controller.Children.SelectMany(Ports))
            {
                controllerOf[port.Id] = controller;
                portById[port.Id] = port;
            }
        }
        var kept = (missing ?? []).GroupBy(device => device.PortId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<UsbNode>)[.. group.Select(device => device.Node)]);
        IReadOnlyList<UsbNode> KeptAt(UsbNode port) => kept.GetValueOrDefault(port.Id) ?? [];
        bool InUse(UsbNode port) => port.Children.Count > 0 || KeptAt(port).Count > 0;
        var sorting = new Sorting(KeptAt);

        foreach (var rootHub in tree.SelectMany(controller => controller.Children))
        {
            sorting.Internal(rootHub);
        }
        foreach (var controller in tree.Where(controller => controller.Problem != null))
        {
            sorting.Other.Add(controller with { Children = [] });
            sorting.Reasons.Add(new PortReason(PortReasonKind.UnreadableController, controller.Name, Detail: controller.Problem));
        }

        var connectors = new List<(ComputerPort Port, IReadOnlyList<UsbNode> Halves)>();
        foreach (var (port, connector) in PortTesting.Ports(tree, known).Where(each => !each.Halves.Any(half => sorting.PlacedElsewhere.Contains(half.Id))))
        {
            // A built-in device on a port the system calls user-connectable (#330): in the computer's own container.
            var nodes = connector.SelectMany(half => half.Children.Concat(KeptAt(half))).ToList();
            if (nodes.Count > 0 && nodes.All(node => node.Kind == UsbNodeKind.Device && node.InLocalMachine == true))
            {
                sorting.BuiltIn.AddRange(connector.Where(InUse));
            }
            else
            {
                connectors.Add((port, connector));
            }
        }

        // The routers of a controller are on the one port whose half on that controller has something connected; with
        // several the system does not say which, so they are not placed. A disconnected device kept at a port is no router.
        var routerAt = new Dictionary<IReadOnlyList<UsbNode>, PortRouter>();
        foreach (var controller in tree.Where(controller => controller.Usb4Routers is > 0))
        {
            var on = connectors.Where(each => each.Halves.Any(half => controllerOf.GetValueOrDefault(half.Id) == controller && half.Children.Count > 0)).ToList();
            if (on.Count == 1)
            {
                routerAt[on[0].Halves] = Router(controller);
            }
            else if (on.Count > 1)
            {
                sorting.Reasons.Add(new PortReason(PortReasonKind.RouterNotPlaced, Router(controller).Names ?? "", controller.Name, on.Count));
            }
        }

        var missingNames = new Dictionary<string, string>();
        var hiddenMissing = new HashSet<string>();
        var merging = new Merging(halves, KeptAt, missingNames, hiddenMissing);
        var groups = new List<PortGroup>();
        foreach (var (port, connector) in connectors)
        {
            IReadOnlyList<UsbNode> shown = connector;
            if (connector is [var usb3, var usb2] && usb3.Bus == UsbBus.Usb3 && usb2.Bus != UsbBus.Usb3)
            {
                if (usb3.Children is [var hub3] && usb2.Children is [var hub2] && SameHub(hub3, hub2))
                {
                    shown = [usb3 with { Children = [merging.Merge(hub3, hub2, usb2.Id)] }, usb2 with { Children = [] }];
                }
                else
                {
                    merging.PairMissing(usb3, usb2);
                }
            }
            groups.Add(new PortGroup(PortGroupId(port.Id), PortGroupKind.Port, port.Name, shown)
            {
                Router = routerAt.GetValueOrDefault(connector),
                UsbC = port.UsbC,
                ControllerIds = [.. connector.Select(half => controllerOf.GetValueOrDefault(half.Id)?.Id).OfType<string>().Distinct()],
            });
        }
        if (sorting.BuiltIn.Count > 0)
        {
            groups.Add(new PortGroup(BuiltInId, PortGroupKind.BuiltIn, "", sorting.BuiltIn)
            {
                ControllerIds = [.. sorting.BuiltIn.Select(port => controllerOf.GetValueOrDefault(port.Id)?.Id).OfType<string>().Distinct()],
            });
        }
        if (sorting.Other.Count > 0 || sorting.Reasons.Count > 0)
        {
            groups.Add(new PortGroup(OtherId, PortGroupKind.Other, "", sorting.Other)
            {
                Reasons = sorting.Reasons,
                ControllerIds = [.. sorting.Other.Select(node => controllerOf.GetValueOrDefault(node.Id)?.Id ?? (node.Kind == UsbNodeKind.Controller ? node.Id : null))
                    .OfType<string>().Distinct()],
            });
        }

        var buses = new Dictionary<string, UsbBus>();
        foreach (var node in groups.SelectMany(group => group.Nodes))
        {
            AddBuses(node, halves, buses);
        }
        // A disconnected device names the bus of its last port too.
        foreach (var device in missing ?? [])
        {
            if (portById.GetValueOrDefault(device.PortId)?.Bus is { } bus && !halves.ContainsKey(device.Node.Id))
            {
                buses[device.Node.Id] = bus;
            }
        }
        return new PortTree(groups, halves, buses) { MissingNames = missingNames, HiddenMissing = hiddenMissing };
    }

    /// <summary>
    /// For a disconnected USB 3 hub shown with its disconnected USB 2 half (by the USB 3 half's id): the name of both, as
    /// a connected one is named.
    /// </summary>
    public IReadOnlyDictionary<string, string> MissingNames { get; init; } = new Dictionary<string, string>();

    /// <summary>The disconnected USB 2 halves shown with their USB 3 half, so they have no row of their own.</summary>
    public IReadOnlySet<string> HiddenMissing { get; init; } = new HashSet<string>();

    // Sorts the internal ports of a root hub or built-in hub (as PortTesting.OwnPortNodes walks them): a device on one is
    // built in, unless the system says it is outside the computer; a hub outside the computer on one cannot be placed,
    // and its ports are no computer ports. An internal port keeps its place with a disconnected device or a problem.
    private sealed class Sorting(Func<UsbNode, IReadOnlyList<UsbNode>> keptAt)
    {
        public List<UsbNode> BuiltIn { get; } = [];

        public List<UsbNode> Other { get; } = [];

        public List<PortReason> Reasons { get; } = [];

        public HashSet<string> PlacedElsewhere { get; } = [];

        public void Internal(UsbNode hub)
        {
            foreach (var port in hub.Children.Where(port => port.Kind == UsbNodeKind.Port && port.UserConnectable == false))
            {
                var outside = port.Children.Concat(keptAt(port)).Where(node => node.InLocalMachine == false).ToList();
                if (outside.Count > 0)
                {
                    Other.Add(port);
                    Reasons.AddRange(outside.Select(node => new PortReason(PortReasonKind.InternalPortOutsideDevice, node.Name, port.Name)));
                    PlacedElsewhere.UnionWith(outside.Where(node => node.Kind == UsbNodeKind.Hub).SelectMany(Ports).Select(each => each.Id));
                }
                else if (port.Children is [{ Kind: UsbNodeKind.Hub } builtInHub])
                {
                    Internal(builtInHub);
                }
                else if (port.Children.Count > 0 || keptAt(port).Count > 0 || port.Problem != null)
                {
                    BuiltIn.Add(port);
                }
            }
        }
    }

    // Puts the two halves of each USB 3 hub together, connected or disconnected.
    private sealed class Merging(Dictionary<string, HubHalf> halves, Func<UsbNode, IReadOnlyList<UsbNode>> keptAt,
        Dictionary<string, string> missingNames, HashSet<string> hiddenMissing)
    {
        // The USB 3 half with the ports of both halves, by port number, the USB 3 port first. A hub on a USB 3 port and a
        // hub on its companion are one hub again; when neither hub's ports name a companion (macOS), the USB 2 port of the
        // same number is the companion, and the container rule of SameHub keeps two boxes apart where the system gives one.
        public UsbNode Merge(UsbNode usb3, UsbNode usb2, string usb2PortId)
        {
            halves[usb3.Id] = new HubHalf(usb2, usb2PortId);
            var byNumber = !usb3.Children.Concat(usb2.Children).Any(port => port.CompanionPortId != null);
            var emptied = new Dictionary<string, UsbNode>();
            var ports3 = new List<UsbNode>();
            foreach (var port3 in usb3.Children)
            {
                var port2 = usb2.Children.FirstOrDefault(port => port3.CompanionPortId == port.Id || port.CompanionPortId == port3.Id)
                    ?? (byNumber ? usb2.Children.FirstOrDefault(port => port3.PortNumber != null && port.PortNumber == port3.PortNumber) : null);
                if (port2 != null && !emptied.ContainsKey(port2.Id) && port3.Children is [var hub3] && port2.Children is [var hub2] && SameHub(hub3, hub2))
                {
                    ports3.Add(port3 with { Children = [Merge(hub3, hub2, port2.Id)] });
                    emptied[port2.Id] = port2 with { Children = [] };
                    continue;
                }
                if (port2 != null)
                {
                    PairMissing(port3, port2);
                }
                ports3.Add(port3);
            }
            var ports = ports3.Select(port => (Port: port, Usb3: true))
                .Concat(usb2.Children.Select(port => (Port: emptied.GetValueOrDefault(port.Id) ?? port, Usb3: false)))
                .OrderBy(each => each.Port.PortNumber ?? int.MaxValue)
                .ThenBy(each => each.Usb3 ? 0 : 1)
                .Select(each => each.Port)
                .ToList();
            return usb3 with { Name = Both(usb3, usb2), Children = ports, Problem = usb3.Problem ?? usb2.Problem };
        }

        // A dropped USB 3 hub (#212): its two disconnected halves, kept at the two halves of one port, are one hub too.
        public void PairMissing(UsbNode port3, UsbNode port2)
        {
            if (port3.Children.Count == 0 && port2.Children.Count == 0 && keptAt(port3) is [var hub3] && keptAt(port2) is [var hub2] && SameHub(hub3, hub2))
            {
                halves[hub3.Id] = new HubHalf(hub2, port2.Id);
                missingNames[hub3.Id] = Both(hub3, hub2);
                hiddenMissing.Add(hub2.Id);
            }
        }

        private static string Both(UsbNode usb3, UsbNode usb2) => usb3.Name == usb2.Name ? usb3.Name : $"{usb3.Name} + {usb2.Name}";
    }

    // Every port of a hub and of the hubs below it.
    private static IEnumerable<UsbNode> Ports(UsbNode hub) =>
        hub.Children.Where(port => port.Kind == UsbNodeKind.Port)
            .SelectMany(port => port.Children.Where(child => child.Kind == UsbNodeKind.Hub).SelectMany(Ports).Prepend(port));

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
