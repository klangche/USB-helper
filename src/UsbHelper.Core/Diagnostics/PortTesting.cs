using System.Globalization;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Diagnostics;

/// <summary>One of the computer's own ports (#342): a user-connectable port on a root hub or a built-in hub.</summary>
/// <param name="Name">"Port 2", as the tree shows it; "Port 2 on Root hub 2" when another of the computer's ports has the same name.</param>
/// <param name="Bus">USB 2 or USB 3, as the operating system reports it; null when it does not.</param>
/// <param name="UsbC">True for a USB-C port, false for another kind, null when the operating system does not say.</param>
/// <param name="Order">Its place in the tree, for sorting.</param>
public sealed record ComputerPort(string Id, string Name, UsbBus? Bus, bool? UsbC = null, int Order = 0)
{
    /// <summary>"Port 1 (USB-C, USB 3)", "Port 1 (USB 3)", or "Port 1" when neither is known.</summary>
    public string Label => Details(UsbC == true ? "USB-C" : null, Bus switch { UsbBus.Usb2 => "USB 2", UsbBus.Usb3 => "USB 3", _ => null }) is { } details
        ? $"{Name} ({details})"
        : Name;

    /// <summary>The known details joined with ", ", or null when there are none.</summary>
    public static string? Details(params string?[] details) =>
        details.OfType<string>().ToList() is { Count: > 0 } known ? string.Join(", ", known) : null;
}

/// <summary>
/// The same setup moved to another of the computer's ports (#342): everything behind <see cref="From"/> went away and
/// hubs and devices with the same vendor and product ids came behind <see cref="To"/>, in the same change or a later one.
/// </summary>
/// <param name="Pairs">Each node that came back: its id behind the old port and behind the new one.</param>
/// <param name="NotBack">The nodes that did not come back; they stay missing.</param>
public sealed record SetupMove(ComputerPort From, ComputerPort To, IReadOnlyList<(string Before, string After)> Pairs, IReadOnlyList<UsbNode> NotBack);

/// <summary>One device behind a port when it was last evaluated (#342).</summary>
/// <param name="Bus">The bus it ran on, or null when unknown.</param>
/// <param name="Error">True when the operating system reported a problem with it, or it was disconnected.</param>
public sealed record PortDevice(string Name, UsbBus? Bus, bool Error);

/// <summary>The last result of the setup on one of the computer's ports (#342).</summary>
/// <param name="Devices">Each device behind it (not the hubs), by vendor and product id.</param>
public sealed record PortResult(ComputerPort Port, ConnectionStatus Status, IReadOnlyDictionary<string, PortDevice> Devices);

/// <summary>How a port was worse than another with the same setup (#342).</summary>
public enum PortDifferenceKind
{
    /// <summary>A device had an error on the worse port and none on the better one.</summary>
    Error,

    /// <summary>A device ran on USB 2 on the worse port and on USB 3 on the better one.</summary>
    Usb2,

    /// <summary>The setup was within the limits on the better port and over one on the worse port.</summary>
    Limit,
}

/// <summary>A difference between two ports that had the same setup (#342).</summary>
/// <param name="Device">The device, or null for <see cref="PortDifferenceKind.Limit"/>, which is about the whole setup.</param>
/// <param name="Status">For <see cref="PortDifferenceKind.Limit"/>, the result on <see cref="Worse"/>.</param>
public sealed record PortDifference(PortDifferenceKind Kind, string? Device, ComputerPort Worse, ComputerPort Better, ConnectionStatus Status = ConnectionStatus.Green);

/// <summary>"Ports tested" (#342): each port tried with its result, and the ports not tried.</summary>
/// <param name="Total">
/// The computer's ports: its computer file's USB <c>ports</c> entries (MacBook Neo: 2), unless the system showed more
/// ports than the file has entries, which means an entry stands for several ports (MacBook Pro: one <c>thunderbolt4</c>
/// for three); then, and without a file, the ports seen this session.
/// </param>
/// <param name="SeenOnly">The total is the ports seen, and some do not say whether they are built in (macOS).</param>
public sealed record PortsTested(IReadOnlyList<PortResult> Tried, IReadOnlyList<ComputerPort> NotTried, int Total, bool SeenOnly);

/// <summary>
/// Testing a setup on several of the computer's ports (#342). It recognises the same setup moved to another port by
/// vendor and product ids and the shape of the tree, never by serial numbers; keeps the last result per port; and says
/// how the ports differ. Session only: nothing is saved.
/// </summary>
public sealed class PortTesting
{
    private readonly Dictionary<string, PortResult> results = [];
    private readonly Dictionary<string, ComputerPort> seen = [];

    // The two halves of each connector seen this session (#342), by port id both ways.
    private readonly Dictionary<string, string> pairs = [];

    // What left each computer port this session, and whether the port emptied since (#358).
    private readonly Dictionary<string, Departed> departed = [];

    // What left a computer port and has not come back: whether the port then emptied, and, for what a move left
    // behind, the port it moved to; such leftovers wait until that port empties or something else arrives.
    private sealed record Departed(ComputerPort Port, List<UsbNode> Nodes, bool Emptied, string? MovedTo = null);

    /// <summary>The other half of <paramref name="portId"/>'s connector, as learned this session (#266), or null.</summary>
    public string? PartnerOf(string portId) => pairs.GetValueOrDefault(portId);

    /// <summary>The two halves of each connector seen this session, by port id both ways (#370: the tree by port uses them).</summary>
    public IReadOnlyDictionary<string, string> Pairs => pairs;

    /// <summary>The computer's own ports in <paramref name="tree"/>, in tree order: user-connectable, or not said, on a root hub or a built-in hub.</summary>
    public static IReadOnlyList<ComputerPort> PortsOf(IReadOnlyList<UsbNode> tree) => [.. Ports(tree).Select(each => each.Port)];

    /// <summary>
    /// Notes the change from <paramref name="before"/> to <paramref name="after"/> and returns the move or replug it
    /// completes, if any (#342, #358). What leaves a computer port is remembered for the session, with whether the port
    /// then emptied. Hubs and devices that arrive on a port are the same setup when every one of them, by vendor and
    /// product id, left a port that emptied: that port itself first (a replug, <see cref="SetupMove.From"/> equal to
    /// <see cref="SetupMove.To"/>), else another (a move). The halves of one USB-C port, and an unplug and a plug-in,
    /// arrive as separate changes, so what has not come back yet stays waiting for the next change. A single device that
    /// drops while the rest of its port stays connected is not a replug: it stays "reconnected" (#54). Null when nothing
    /// came back.
    /// </summary>
    public SetupMove? Note(IReadOnlyList<UsbNode> before, IReadOnlyList<UsbNode> after)
    {
        Learn(Ports(before, pairs));
        Learn(Ports(after, pairs));
        var portsBefore = Occupied(before, pairs);
        var portsAfter = Occupied(after, pairs);
        foreach (var (id, (port, nodes)) in portsBefore)
        {
            var now = portsAfter.TryGetValue(id, out var still) ? still.Nodes.Select(node => node.Id).ToHashSet() : [];
            var left = nodes.Where(node => !now.Contains(node.Id)).ToList();
            if (left.Count > 0)
            {
                var known = departed.GetValueOrDefault(id);
                departed[id] = new Departed(port, [.. known?.Nodes ?? [], .. left], known?.Emptied == true || !portsAfter.ContainsKey(id));
            }
        }
        SetupMove? move = null;
        var fed = new HashSet<string>(); // Ports whose leftovers received more of their setup in this change.
        var anyArrived = false;
        foreach (var (id, (to, nodes)) in portsAfter)
        {
            var was = portsBefore.TryGetValue(id, out var earlier) ? earlier.Nodes.Select(node => node.Id).ToHashSet() : [];
            var arrived = nodes.Where(node => !was.Contains(node.Id)).ToList();
            if (arrived.Count == 0)
            {
                continue;
            }
            anyArrived = true;
            SetupMove? found = null;
            foreach (var (sourceId, source) in departed.Where(each => each.Value.Emptied).OrderBy(each => each.Key == id ? 0 : 1).ToList())
            {
                if (Take(source.Nodes, arrived) is { } matched)
                {
                    if (source.Nodes.Count == 0)
                    {
                        departed.Remove(sourceId);
                    }
                    else
                    {
                        departed[sourceId] = source with { MovedTo = to.Id };
                        fed.Add(sourceId);
                    }
                    found = new SetupMove(source.Port, to, matched, [.. source.Nodes]);
                    break;
                }
            }
            if (found == null && departed.TryGetValue(id, out var gone))
            {
                if (gone.Emptied)
                {
                    // Something else on a port that had emptied: what left it is not looked for any more.
                    departed.Remove(id);
                }
                else
                {
                    // Back on a port that stayed connected (#54, "reconnected"): no longer waiting.
                    Forget(gone.Nodes, arrived);
                    if (gone.Nodes.Count == 0)
                    {
                        departed.Remove(id);
                    }
                }
            }
            move ??= found;
        }
        // What a move left behind waits through quiet changes, so a late USB 3 half still joins the move; it is not back
        // once the port it moved to empties or something else arrives.
        foreach (var (sourceId, source) in departed.Where(each => each.Value.MovedTo is { } movedTo && !fed.Contains(each.Key)
            && (!portsAfter.ContainsKey(movedTo) || anyArrived)).ToList())
        {
            departed.Remove(sourceId);
        }
        return move;
    }

    // Takes one waiting node of the same vendor and product id for each arrived node that has one.
    private static void Forget(List<UsbNode> waiting, IReadOnlyList<UsbNode> arrived)
    {
        foreach (var node in arrived)
        {
            if (Key(node) is { } key && waiting.FirstOrDefault(old => Key(old) == key) is { } match)
            {
                waiting.Remove(match);
            }
        }
    }

    // Pairs every arrived node with a waiting one of the same vendor and product id, and takes those from the waiting
    // list; null, and nothing taken, when any arrived node has no match.
    private static List<(string Before, string After)>? Take(List<UsbNode> waiting, IReadOnlyList<UsbNode> arrived)
    {
        var left = waiting.ToList();
        var pairs = new List<(string, string)>();
        foreach (var node in arrived)
        {
            var match = Key(node) is { } key ? left.FirstOrDefault(old => Key(old) == key) : null;
            if (match == null)
            {
                return null;
            }
            left.Remove(match);
            pairs.Add((match.Id, node.Id));
        }
        waiting.Clear();
        waiting.AddRange(left);
        return pairs;
    }

    /// <summary>Remembers the result on every computer port that has a setup now (call after each evaluation).</summary>
    public void Update(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, ConnectionStatus> connections)
    {
        var ports = Ports(tree, pairs);
        Learn(ports);
        foreach (var (port, halves) in ports)
        {
            seen[port.Id] = port;
            SeenPortsUnknown |= halves.Any(half => half.UserConnectable == null);
        }
        var buses = Topology.TopologyCounts.Buses(tree);
        foreach (var (port, nodes) in Occupied(tree, pairs).Values)
        {
            results[port.Id] = new PortResult(port,
                nodes.Select(node => connections.GetValueOrDefault(node.Id, ConnectionStatus.Grey)).Max(),
                nodes.Where(node => node.Kind == UsbNodeKind.Device && Key(node) != null).GroupBy(node => Key(node)!)
                    .ToDictionary(group => group.Key, group => new PortDevice(group.First().Name, buses.GetValueOrDefault(group.First().Id),
                        group.First().Problem != null || connections.GetValueOrDefault(group.First().Id) == ConnectionStatus.Red)));
        }
    }

    // Remembers each connector's halves, and drops what was kept under the other half's id while it looked like a port
    // of its own: it is the same connector.
    private void Learn(List<(ComputerPort Port, IReadOnlyList<UsbNode> Halves)> ports)
    {
        foreach (var (port, halves) in ports.Where(each => each.Halves.Count == 2))
        {
            var other = halves[1].Id;
            pairs[port.Id] = other;
            pairs[other] = port.Id;
            seen.Remove(other);
            results.Remove(other);
            departed.Remove(other);
        }
    }

    /// <summary>Forgets every port (Clear).</summary>
    public void Reset()
    {
        pairs.Clear();
        results.Clear();
        seen.Clear();
        departed.Clear();
        SeenPortsUnknown = false;
    }

    /// <summary>The ports tried with their last result, in tree order.</summary>
    public IReadOnlyList<PortResult> Tried => [.. results.Values.OrderBy(result => result.Port.Order)];

    /// <summary>
    /// How the ports tried differ, for every pair where one is worse: a device that connected on one and not on the
    /// other, that had an error on one only, or that ran on USB 3 on one and on USB 2 on the other; or the setup within
    /// the limits on one and over one on the other. Each difference names both ports.
    /// </summary>
    public IReadOnlyList<PortDifference> Differences()
    {
        var differences = new List<PortDifference>();
        var tried = Tried;
        foreach (var better in tried)
        {
            // Like with like (#358): only ports where the same devices were connected, so a device that was not plugged in
            // on one port is no difference, and a limit is compared only for the same setup.
            foreach (var worse in tried.Where(other => other != better && SameSetup(other, better)))
            {
                var found = differences.Count;
                foreach (var (key, device) in better.Devices)
                {
                    var there = worse.Devices[key];
                    if (there.Error && !device.Error)
                    {
                        differences.Add(new PortDifference(PortDifferenceKind.Error, device.Name, worse.Port, better.Port));
                    }
                    else if (device.Bus == UsbBus.Usb3 && there.Bus == UsbBus.Usb2)
                    {
                        differences.Add(new PortDifference(PortDifferenceKind.Usb2, device.Name, worse.Port, better.Port));
                    }
                }
                // Over a limit only when no device explains the worse result: an error or USB 2 also lowers it.
                if (differences.Count == found && better.Status == ConnectionStatus.Green && worse.Status is ConnectionStatus.Orange or ConnectionStatus.Purple)
                {
                    differences.Add(new PortDifference(PortDifferenceKind.Limit, null, worse.Port, better.Port, worse.Status));
                }
            }
        }
        // Once each (#358): a port worse for one reason is said once, against the first better port in tree order.
        return [.. differences.GroupBy(difference => (difference.Kind, difference.Device, difference.Worse.Id)).Select(group => group.First())];
    }

    private static bool SameSetup(PortResult a, PortResult b) => a.Devices.Keys.ToHashSet().SetEquals(b.Devices.Keys);

    /// <summary>The "Ports tested" summary; the total comes from <paramref name="profile"/>'s <c>ports</c> when it can (see <see cref="PortsTested.Total"/>).</summary>
    public PortsTested Summary(ResolvedProfile? profile = null)
    {
        var inFile = profile?.Find("ports")?.Fields.Values.Count(entry => entry.Fields.ContainsKey("usb")) ?? 0;
        var seenCount = Math.Max(seen.Count, results.Count);
        var fromFile = inFile > 0 && inFile >= seenCount;
        return new PortsTested(Tried, [.. seen.Values.Where(port => !results.ContainsKey(port.Id)).OrderBy(port => port.Order)],
            fromFile ? inFile : seenCount, !fromFile && SeenPortsUnknown);
    }

    /// <summary>True when any port seen does not say whether it is user-connectable.</summary>
    public bool SeenPortsUnknown { get; private set; }

    /// <summary>The summary as the log, the exported log and the report write it, in English.</summary>
    public static string LogText(PortsTested tested, IReadOnlyList<PortDifference> differences)
    {
        if (tested.Tried.Count == 0)
        {
            return "Ports tested: none.";
        }
        var stable = tested.Tried.All(result => result.Status == ConnectionStatus.Green);
        var text = stable
            ? string.Create(CultureInfo.InvariantCulture, $"Tested on {tested.Tried.Count} of {tested.Total} ports, all stable.")
            : string.Create(CultureInfo.InvariantCulture, $"Tested on {tested.Tried.Count} of {tested.Total} ports: ")
              + string.Join(", ", tested.Tried.Select(result => $"{result.Port.Name} {Word(result.Status)}{Reason(result, differences)}")) + ".";
        foreach (var port in tested.NotTried)
        {
            text += $" {port.Name} not tested.";
        }
        if (tested.SeenOnly)
        {
            text += " The ports are those seen this session: the system does not say which ports are built in.";
        }
        return text;
    }

    /// <summary>A difference as a finding's text, in English: "On Port 2 the Camera runs on USB 2 (USB 3 on Port 1)."</summary>
    public static string DifferenceText(PortDifference difference) => difference.Kind switch
    {
        PortDifferenceKind.Error => $"On {difference.Worse.Name} the {difference.Device} has an error; on {difference.Better.Name} it has none.",
        PortDifferenceKind.Usb2 => $"On {difference.Worse.Name} the {difference.Device} runs on USB 2 (USB 3 on {difference.Better.Name}).",
        _ => $"On {difference.Worse.Name} the setup is over a limit ({Word(difference.Status)}); on {difference.Better.Name} it is within.",
    };

    private static string Word(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Green => "stable",
        ConnectionStatus.Orange => "potentially unstable",
        ConnectionStatus.Purple => "unstable",
        ConnectionStatus.Red => "disconnected",
        _ => "unknown",
    };

    // "(camera on USB 2)", "(camera did not connect)" or "(camera has an error)": the first device difference that makes
    // this port the worse one; a limit difference adds nothing, the status word already says it.
    private static string Reason(PortResult result, IReadOnlyList<PortDifference> differences) =>
        differences.FirstOrDefault(difference => difference.Worse.Id == result.Port.Id && difference.Kind != PortDifferenceKind.Limit) is { } worse
            ? worse.Kind switch
            {
                PortDifferenceKind.Error => $" ({worse.Device} has an error)",
                _ => $" ({worse.Device} on USB 2)",
            }
            : "";

    // The hubs and devices behind each occupied computer port, top first, in tree order, by port id. A connector with
    // two halves (#342) gives its USB 3 half's nodes first, then its USB 2 half's.
    private static Dictionary<string, (ComputerPort Port, IReadOnlyList<UsbNode> Nodes)> Occupied(IReadOnlyList<UsbNode> tree,
        IReadOnlyDictionary<string, string>? known = null)
    {
        var occupied = new Dictionary<string, (ComputerPort, IReadOnlyList<UsbNode>)>();
        foreach (var (port, halves) in Ports(tree, known))
        {
            var nodes = halves.SelectMany(half => half.Children).SelectMany(Flatten).ToList();
            if (nodes.Count > 0)
            {
                occupied[port.Id] = (port, nodes);
            }
        }
        return occupied;
    }

    // The computer's ports in tree order, each with its port nodes: one, or the two halves of one connector. A name that
    // two ports share gets the hub it is on.
    internal static List<(ComputerPort Port, IReadOnlyList<UsbNode> Halves)> Ports(IReadOnlyList<UsbNode> tree,
        IReadOnlyDictionary<string, string>? known = null)
    {
        var found = Connectors(tree.SelectMany(controller => controller.Children).SelectMany(rootHub => OwnPortNodes(rootHub)).ToList(), known);
        var shared = found.GroupBy(each => each.Halves[0].Name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var names = found.Select(each => shared.Contains(each.Halves[0].Name) ? $"{each.Halves[0].Name} on {each.Hub.Name}" : each.Halves[0].Name).ToList();
        // Still the same (two hubs of one name): numbered in tree order.
        var still = names.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var numbered = new Dictionary<string, int>();
        var usbCPorts = 0;
        return [.. found.Select((each, i) =>
        {
            var name = names[i];
            if (still.Contains(name))
            {
                numbered[name] = numbered.GetValueOrDefault(name) + 1;
                name = string.Create(CultureInfo.InvariantCulture, $"{name} ({numbered[name]})");
            }
            var main = each.Halves[0];
            var bus = each.Halves.Any(half => half.Bus == UsbBus.Usb3) ? UsbBus.Usb3 : main.Bus;
            var usbC = each.Halves.Any(half => half.UsbC == true) ? true : main.UsbC;
            // A USB-C connector with both halves is named as the computer's port (#358): "USB-C port 1", in tree order.
            if (usbC == true && each.Halves.Count == 2)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"USB-C port {++usbCPorts}");
            }
            return (new ComputerPort(main.Id, name, bus, usbC, i), (IReadOnlyList<UsbNode>)each.Halves);
        })];
    }

    /// <summary>
    /// The computer's connectors (#342): a USB 2 port and a USB 3 port are one connector when the devices on them have the
    /// same physical location (Windows' ACPI <c>_PLD</c>, which names the connector across controllers: on the owner's
    /// Asus the USB-C port's USB 3 half is on the Type-C controller and its USB 2 half on the PCH), or else when the hub
    /// reports them as companions. The USB 3 half comes first. Every other port is a connector of its own.
    /// </summary>
    /// <param name="known">The pairs seen earlier this session, by port id both ways: they stay one connector when only one
    /// half is occupied, or none, and no _PLD shows them together.</param>
    private static List<(IReadOnlyList<UsbNode> Halves, UsbNode Hub)> Connectors(List<(UsbNode Port, UsbNode Hub)> ports,
        IReadOnlyDictionary<string, string>? known)
    {
        var partner = new Dictionary<int, int>();
        // One USB 3 half and one that is not: firmware that gives several ports the same _PLD pairs no two USB 2 ports.
        void Pair(int a, int b)
        {
            if (a != b && !partner.ContainsKey(a) && !partner.ContainsKey(b)
                && (ports[a].Port.Bus == UsbBus.Usb3) != (ports[b].Port.Bus == UsbBus.Usb3))
            {
                partner[a] = b;
                partner[b] = a;
            }
        }
        var byId = ports.Select((each, i) => (each.Port.Id, Index: i)).GroupBy(each => each.Id).ToDictionary(group => group.Key, group => group.First().Index);
        for (var i = 0; i < ports.Count; i++)
        {
            if (known != null && known.TryGetValue(ports[i].Port.Id, out var remembered) && byId.TryGetValue(remembered, out var other))
            {
                Pair(i, other);
            }
        }
        var located = ports.Select((each, i) => (Location: each.Port.Children.FirstOrDefault()?.PhysicalLocation, Index: i))
            .Where(each => each.Location != null).GroupBy(each => each.Location!).Where(group => group.Count() == 2);
        foreach (var group in located)
        {
            Pair(group.First().Index, group.Last().Index);
        }
        for (var i = 0; i < ports.Count; i++)
        {
            if (ports[i].Port.CompanionPortId is { } companion && byId.TryGetValue(companion, out var other))
            {
                Pair(i, other);
            }
        }
        var connectors = new List<(IReadOnlyList<UsbNode>, UsbNode)>();
        for (var i = 0; i < ports.Count; i++)
        {
            if (!partner.TryGetValue(i, out var other))
            {
                connectors.Add(([ports[i].Port], ports[i].Hub));
            }
            else if (other > i)
            {
                var (first, second) = ports[other].Port.Bus == UsbBus.Usb3 && ports[i].Port.Bus != UsbBus.Usb3 ? (other, i) : (i, other);
                connectors.Add(([ports[first].Port, ports[second].Port], ports[first].Hub));
            }
        }
        return connectors;
    }

    private static IEnumerable<UsbNode> Flatten(UsbNode node) =>
        node.Children.Where(child => child.Kind == UsbNodeKind.Port).SelectMany(port => port.Children).SelectMany(Flatten).Prepend(node);

    // Vendor and product id: what makes two hubs or devices "the same" (#342); null when the system did not read them.
    private static string? Key(UsbNode node) =>
        node.VendorId is { } vendor && node.ProductId is { } product ? string.Create(CultureInfo.InvariantCulture, $"{vendor:X4}:{product:X4}") : null;

    // User-connectable or unknown ports of the root hub, and of hubs behind its ports that are not user-connectable.
    private static IEnumerable<(UsbNode Port, UsbNode Hub)> OwnPortNodes(UsbNode builtInHub) =>
        builtInHub.Children.SelectMany(port => port.UserConnectable == false
            ? port.Children.Where(node => node.Kind == UsbNodeKind.Hub).SelectMany(OwnPortNodes)
            : [(port, builtInHub)]);
}
