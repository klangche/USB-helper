using System.Globalization;
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
    /// <summary>A device that connected on the better port did not on the worse one.</summary>
    Missing,

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
/// <param name="Total">The computer's ports seen this session: the computer files do not count ports one by one.</param>
/// <param name="SeenOnly">Some ports seen do not say whether they are built in (macOS), so the total may include others.</param>
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

    // Ports that had a setup and are empty now, with what was behind them: a later change may bring it to another port.
    private readonly Dictionary<string, (ComputerPort Port, IReadOnlyList<UsbNode> Nodes)> emptied = [];

    /// <summary>The computer's own ports in <paramref name="tree"/>, in tree order: user-connectable, or not said, on a root hub or a built-in hub.</summary>
    public static IReadOnlyList<ComputerPort> PortsOf(IReadOnlyList<UsbNode> tree) => [.. Ports(tree).Select(each => each.Port)];

    /// <summary>
    /// Notes the change from <paramref name="before"/> to <paramref name="after"/> and returns the move it completes, if
    /// any. A port that had a setup and is now empty is remembered for the session; a port that was empty and now has a
    /// setup is a move when its top hub or device has the same vendor and product id as the top of a remembered port,
    /// and its ids were all behind that port. An unplug and a replug often arrive as two changes, so they need not come
    /// together. Null when there is no move.
    /// </summary>
    public SetupMove? Note(IReadOnlyList<UsbNode> before, IReadOnlyList<UsbNode> after)
    {
        var portsBefore = Occupied(before);
        var portsAfter = Occupied(after);
        foreach (var (port, nodes) in portsBefore.Values.Where(each => !portsAfter.ContainsKey(each.Port.Id)))
        {
            emptied[port.Id] = (port, nodes);
        }
        SetupMove? move = null;
        foreach (var (to, newNodes) in portsAfter.Values.Where(each => !portsBefore.ContainsKey(each.Port.Id)))
        {
            foreach (var (from, oldNodes) in emptied.Values.Where(each => each.Port.Id != to.Id))
            {
                if (Match(from, oldNodes, to, newNodes) is { } found)
                {
                    move = found;
                    emptied.Remove(from.Id);
                    break;
                }
            }
            if (move != null)
            {
                break;
            }
        }
        // A port with a setup again is no longer empty: what was behind it before is not looked for elsewhere.
        foreach (var id in portsAfter.Keys)
        {
            emptied.Remove(id);
        }
        return move;
    }

    private static SetupMove? Match(ComputerPort from, IReadOnlyList<UsbNode> oldNodes, ComputerPort to, IReadOnlyList<UsbNode> newNodes)
    {
        if (Key(oldNodes[0]) is not { } top || Key(newNodes[0]) != top)
        {
            return null;
        }
        var left = oldNodes.ToList();
        var pairs = new List<(string, string)>();
        foreach (var node in newNodes)
        {
            var match = left.FirstOrDefault(old => Key(old) != null && Key(old) == Key(node));
            if (match == null)
            {
                return null;
            }
            left.Remove(match);
            pairs.Add((match.Id, node.Id));
        }
        return new SetupMove(from, to, pairs, left);
    }

    /// <summary>Remembers the result on every computer port that has a setup now (call after each evaluation).</summary>
    public void Update(IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, ConnectionStatus> connections)
    {
        foreach (var (port, node) in Ports(tree))
        {
            seen[port.Id] = port;
            SeenPortsUnknown |= node.UserConnectable == null;
        }
        var buses = Topology.TopologyCounts.Buses(tree);
        foreach (var (port, nodes) in Occupied(tree).Values)
        {
            results[port.Id] = new PortResult(port,
                nodes.Select(node => connections.GetValueOrDefault(node.Id, ConnectionStatus.Grey)).Max(),
                nodes.Where(node => node.Kind == UsbNodeKind.Device && Key(node) != null).GroupBy(node => Key(node)!)
                    .ToDictionary(group => group.Key, group => new PortDevice(group.First().Name, buses.GetValueOrDefault(group.First().Id),
                        group.First().Problem != null || connections.GetValueOrDefault(group.First().Id) == ConnectionStatus.Red)));
        }
    }

    /// <summary>Forgets every port (Clear).</summary>
    public void Reset()
    {
        results.Clear();
        seen.Clear();
        emptied.Clear();
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
            foreach (var worse in tried.Where(other => other != better))
            {
                if (better.Status == ConnectionStatus.Green && worse.Status is ConnectionStatus.Orange or ConnectionStatus.Purple)
                {
                    differences.Add(new PortDifference(PortDifferenceKind.Limit, null, worse.Port, better.Port, worse.Status));
                }
                foreach (var (key, device) in better.Devices)
                {
                    if (!worse.Devices.TryGetValue(key, out var there))
                    {
                        differences.Add(new PortDifference(PortDifferenceKind.Missing, device.Name, worse.Port, better.Port));
                    }
                    else if (there.Error && !device.Error)
                    {
                        differences.Add(new PortDifference(PortDifferenceKind.Error, device.Name, worse.Port, better.Port));
                    }
                    else if (device.Bus == UsbBus.Usb3 && there.Bus == UsbBus.Usb2)
                    {
                        differences.Add(new PortDifference(PortDifferenceKind.Usb2, device.Name, worse.Port, better.Port));
                    }
                }
            }
        }
        return differences;
    }

    /// <summary>The "Ports tested" summary.</summary>
    public PortsTested Summary() => new(Tried,
        [.. seen.Values.Where(port => !results.ContainsKey(port.Id)).OrderBy(port => port.Order)],
        Math.Max(seen.Count, results.Count), SeenPortsUnknown);

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
        PortDifferenceKind.Missing => $"On {difference.Worse.Name} the {difference.Device} did not connect; on {difference.Better.Name} it did.",
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
                PortDifferenceKind.Missing => $" ({worse.Device} did not connect)",
                PortDifferenceKind.Error => $" ({worse.Device} has an error)",
                _ => $" ({worse.Device} on USB 2)",
            }
            : "";

    // The hubs and devices behind each occupied computer port, top first, in tree order, by port id.
    private static Dictionary<string, (ComputerPort Port, IReadOnlyList<UsbNode> Nodes)> Occupied(IReadOnlyList<UsbNode> tree)
    {
        var occupied = new Dictionary<string, (ComputerPort, IReadOnlyList<UsbNode>)>();
        foreach (var (port, node) in Ports(tree))
        {
            var nodes = node.Children.SelectMany(Flatten).ToList();
            if (nodes.Count > 0)
            {
                occupied[port.Id] = (port, nodes);
            }
        }
        return occupied;
    }

    // The computer's ports in tree order, each with its port node. A name that two ports share gets the hub it is on.
    private static List<(ComputerPort Port, UsbNode Node)> Ports(IReadOnlyList<UsbNode> tree)
    {
        var found = tree.SelectMany(controller => controller.Children).SelectMany(rootHub => OwnPortNodes(rootHub)).ToList();
        var shared = found.GroupBy(each => each.Port.Name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var names = found.Select(each => shared.Contains(each.Port.Name) ? $"{each.Port.Name} on {each.Hub.Name}" : each.Port.Name).ToList();
        // Still the same (two hubs of one name): numbered in tree order.
        var still = names.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var numbered = new Dictionary<string, int>();
        return [.. found.Select((each, i) =>
        {
            var name = names[i];
            if (still.Contains(name))
            {
                numbered[name] = numbered.GetValueOrDefault(name) + 1;
                name = string.Create(CultureInfo.InvariantCulture, $"{name} ({numbered[name]})");
            }
            return (new ComputerPort(each.Port.Id, name, each.Port.Bus, each.Port.UsbC, i), each.Port);
        })];
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
