using System.Globalization;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Diagnostics;

/// <summary>One of the computer's own ports (#342): a user-connectable port on a root hub or a built-in hub.</summary>
/// <param name="Name">"Port 2", as the tree shows it.</param>
/// <param name="Bus">USB 2 or USB 3, as the operating system reports it; null when it does not.</param>
public sealed record ComputerPort(string Id, string Name, UsbBus? Bus)
{
    /// <summary>"Port 1 (USB 3)", or "Port 1" when the bus is not known.</summary>
    public string Label => Bus switch
    {
        UsbBus.Usb2 => $"{Name} (USB 2)",
        UsbBus.Usb3 => $"{Name} (USB 3)",
        _ => Name,
    };
}

/// <summary>
/// The same setup moved to another of the computer's ports (#342): everything behind <see cref="From"/> went away and
/// hubs and devices with the same vendor and product ids came behind <see cref="To"/>.
/// </summary>
/// <param name="Pairs">Each node that came back: its id behind the old port and behind the new one.</param>
/// <param name="NotBack">The nodes that did not come back; they stay missing.</param>
public sealed record SetupMove(ComputerPort From, ComputerPort To, IReadOnlyList<(string Before, string After)> Pairs, IReadOnlyList<UsbNode> NotBack);

/// <summary>The last result of the setup on one of the computer's ports (#342).</summary>
/// <param name="Devices">Each device behind it (not the hubs), by vendor and product id: its name and the bus it ran on.</param>
public sealed record PortResult(ComputerPort Port, ConnectionStatus Status, IReadOnlyDictionary<string, (string Name, UsbBus? Bus)> Devices);

/// <summary>A difference between two ports that had the same setup (#342).</summary>
/// <param name="Bus">For a bus difference, the bus on <see cref="Worse"/>, and <see cref="OtherBus"/> on <see cref="Better"/>.</param>
public sealed record PortDifference(string Device, ComputerPort Worse, ComputerPort Better, bool Missing, UsbBus? Bus, UsbBus? OtherBus);

/// <summary>"Ports tested" (#342): each port tried with its result, and the ports not tried.</summary>
/// <param name="Total">The computer's ports: from its computer file's <c>ports</c>, else the ports seen.</param>
/// <param name="SeenOnly">The total is the ports seen this session, because the system does not say which are built in (macOS).</param>
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

    /// <summary>The computer's own ports in <paramref name="tree"/>: user-connectable, or not said, on a root hub or a built-in hub.</summary>
    public static IReadOnlyList<ComputerPort> PortsOf(IReadOnlyList<UsbNode> tree) =>
        [.. tree.SelectMany(controller => controller.Children).SelectMany(OwnPorts)];

    /// <summary>
    /// The move from <paramref name="before"/> to <paramref name="after"/>, if any: a port that had a setup and is now
    /// empty, and another that was empty and now has one whose top hub or device has the same vendor and product id
    /// and whose ids were all behind the old port. Null when there is none.
    /// </summary>
    public static SetupMove? Detect(IReadOnlyList<UsbNode> before, IReadOnlyList<UsbNode> after)
    {
        var portsBefore = Occupied(before);
        var portsAfter = Occupied(after);
        foreach (var (from, oldNodes) in portsBefore.Where(port => !portsAfter.Keys.Any(now => now.Id == port.Key.Id)).Select(port => (port.Key, port.Value)))
        {
            foreach (var (to, newNodes) in portsAfter.Where(port => !portsBefore.Keys.Any(old => old.Id == port.Key.Id)).Select(port => (port.Key, port.Value)))
            {
                if (Key(oldNodes[0]) != Key(newNodes[0]) || Key(oldNodes[0]) == null)
                {
                    continue;
                }
                var left = oldNodes.ToList();
                var pairs = new List<(string, string)>();
                var fits = true;
                foreach (var node in newNodes)
                {
                    var match = left.FirstOrDefault(old => Key(old) != null && Key(old) == Key(node));
                    if (match == null)
                    {
                        fits = false;
                        break;
                    }
                    left.Remove(match);
                    pairs.Add((match.Id, node.Id));
                }
                if (fits)
                {
                    return new SetupMove(from, to, pairs, left);
                }
            }
        }
        return null;
    }

    /// <summary>Remembers the result on every computer port that has a setup now (call after each evaluation).</summary>
    public void Update(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, ConnectionStatus> connections)
    {
        foreach (var port in tree.SelectMany(controller => controller.Children).SelectMany(OwnPortNodes))
        {
            seen[port.Id] = new ComputerPort(port.Id, port.Name, port.Bus);
            SeenPortsUnknown |= port.UserConnectable == null;
        }
        foreach (var (port, nodes) in Occupied(tree))
        {
            var buses = TopologyBuses(tree);
            results[port.Id] = new PortResult(port,
                nodes.Select(node => connections.GetValueOrDefault(node.Id, ConnectionStatus.Grey)).Max(),
                nodes.Where(node => node.Kind == UsbNodeKind.Device && Key(node) != null).GroupBy(node => Key(node)!)
                    .ToDictionary(group => group.Key, group => (group.First().Name, buses.GetValueOrDefault(group.First().Id))));
        }
    }

    /// <summary>Forgets every port (Clear).</summary>
    public void Reset()
    {
        results.Clear();
        seen.Clear();
        SeenPortsUnknown = false;
    }

    /// <summary>The ports tried with their last result, in the order the computer lists them.</summary>
    public IReadOnlyList<PortResult> Tried => [.. results.Values.OrderBy(result => result.Port.Name, StringComparer.Ordinal)];

    /// <summary>
    /// How the ports tried differ, for every pair where one is worse: a hub or device that connected on one and not on the
    /// other, or that ran on USB 3 on one and on USB 2 on the other. Each difference names both ports.
    /// </summary>
    public IReadOnlyList<PortDifference> Differences()
    {
        var differences = new List<PortDifference>();
        var tried = Tried;
        foreach (var a in tried)
        {
            foreach (var b in tried.Where(b => b != a))
            {
                foreach (var (key, (name, busOnA)) in a.Devices)
                {
                    if (!b.Devices.TryGetValue(key, out var onB))
                    {
                        differences.Add(new PortDifference(name, b.Port, a.Port, Missing: true, null, null));
                    }
                    else if (busOnA == UsbBus.Usb3 && onB.Bus == UsbBus.Usb2)
                    {
                        differences.Add(new PortDifference(name, b.Port, a.Port, Missing: false, UsbBus.Usb2, UsbBus.Usb3));
                    }
                }
            }
        }
        return differences;
    }

    /// <summary>The "Ports tested" summary, with the total from the computer file's <c>ports</c> when <paramref name="profile"/> has them.</summary>
    public PortsTested Summary(ResolvedProfile? profile)
    {
        var fromFile = profile?.Find("ports")?.Fields.Count;
        var notTried = seen.Values.Where(port => !results.ContainsKey(port.Id)).OrderBy(port => port.Name, StringComparer.Ordinal).ToList();
        var total = fromFile is > 0 ? Math.Max(fromFile.Value, results.Count) : seen.Count;
        // macOS does not say which ports are built in: the total is the ports seen (#342).
        var seenOnly = fromFile is not > 0 && SeenPortsUnknown;
        return new PortsTested(Tried, notTried, total, seenOnly);
    }

    /// <summary>True when any port seen does not say whether it is user-connectable.</summary>
    public bool SeenPortsUnknown { get; private set; }

    /// <summary>The summary as the log and the exported log write it, in English.</summary>
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
    public static string DifferenceText(PortDifference difference) => difference.Missing
        ? $"On {difference.Worse.Name} the {difference.Device} did not connect; on {difference.Better.Name} it did."
        : $"On {difference.Worse.Name} the {difference.Device} runs on USB 2 (USB 3 on {difference.Better.Name}).";

    private static string Word(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Green => "stable",
        ConnectionStatus.Orange => "potentially unstable",
        ConnectionStatus.Purple => "unstable",
        ConnectionStatus.Red => "disconnected",
        _ => "unknown",
    };

    // "(camera on USB 2)" or "(camera did not connect)": the first difference that makes this port the worse one.
    private static string Reason(PortResult result, IReadOnlyList<PortDifference> differences) =>
        differences.FirstOrDefault(difference => difference.Worse.Id == result.Port.Id) is { } worse
            ? worse.Missing ? $" ({worse.Device} did not connect)" : $" ({worse.Device} on USB 2)"
            : "";

    // The hubs and devices behind each occupied computer port, top first, in tree order.
    private static Dictionary<ComputerPort, IReadOnlyList<UsbNode>> Occupied(IReadOnlyList<UsbNode> tree)
    {
        var occupied = new Dictionary<ComputerPort, IReadOnlyList<UsbNode>>();
        foreach (var port in tree.SelectMany(controller => controller.Children).SelectMany(OwnPortNodes))
        {
            var nodes = port.Children.SelectMany(Flatten).ToList();
            if (nodes.Count > 0)
            {
                occupied[new ComputerPort(port.Id, port.Name, port.Bus)] = nodes;
            }
        }
        return occupied;
    }

    private static IEnumerable<UsbNode> Flatten(UsbNode node) =>
        node.Children.Where(child => child.Kind == UsbNodeKind.Port).SelectMany(port => port.Children).SelectMany(Flatten).Prepend(node);

    // Vendor and product id: what makes two hubs or devices "the same" (#342); null when the system did not read them.
    private static string? Key(UsbNode node) =>
        node.VendorId is { } vendor && node.ProductId is { } product ? string.Create(CultureInfo.InvariantCulture, $"{vendor:X4}:{product:X4}") : null;

    private static IEnumerable<ComputerPort> OwnPorts(UsbNode rootHub) =>
        OwnPortNodes(rootHub).Select(port => new ComputerPort(port.Id, port.Name, port.Bus));

    // User-connectable or unknown ports of the root hub, and of hubs behind its ports that are not user-connectable.
    private static IEnumerable<UsbNode> OwnPortNodes(UsbNode builtInHub) =>
        builtInHub.Children.SelectMany(port => port.UserConnectable == false
            ? port.Children.Where(node => node.Kind == UsbNodeKind.Hub).SelectMany(OwnPortNodes)
            : [port]);

    private static IReadOnlyDictionary<string, UsbBus?> TopologyBuses(IReadOnlyList<UsbNode> tree) => Topology.TopologyCounts.Buses(tree);
}
