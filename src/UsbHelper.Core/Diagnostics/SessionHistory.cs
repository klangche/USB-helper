using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Diagnostics;

/// <summary>A hub or device seen earlier in this session that is no longer connected.</summary>
/// <param name="Node">The hub or device as it was last seen.</param>
/// <param name="PortId">The port it was connected to.</param>
/// <param name="PortName">A readable name for that port, such as "Port 2 of Generic USB Hub".</param>
/// <param name="AncestorIds">The controller, hubs and ports it was behind, its own port included.</param>
public sealed record MissingDevice(UsbNode Node, string PortId, string PortName, IReadOnlyList<string> AncestorIds);

/// <summary>
/// Every hub and device seen since the application started (owner decision in #2). A device that is gone
/// stays missing until it returns or <see cref="Clear"/> is called. A device that returns is reconnected (owner
/// decision in #54) until it leaves again or is cleared. Nothing is saved to disk.
/// Placeholders for devices the operating system could not identify are not remembered: once identified, the
/// same device gets its real id.
/// </summary>
public sealed class SessionHistory
{
    private readonly Dictionary<string, MissingDevice> seen = [];
    private readonly HashSet<string> gone = [];
    private readonly HashSet<string> reconnected = [];

    /// <summary>Remembers everything in <paramref name="tree"/>. Call after enumerating and after each change.</summary>
    public void Record(IReadOnlyList<UsbNode> tree)
    {
        var present = new HashSet<string>();
        foreach (var controller in tree)
        {
            foreach (var rootHub in controller.Children)
            {
                RecordBelow(rootHub, [controller.Id, rootHub.Id], present);
            }
        }
        foreach (var id in seen.Keys)
        {
            if (present.Contains(id))
            {
                if (gone.Remove(id))
                {
                    reconnected.Add(id);
                }
            }
            else
            {
                gone.Add(id);
                reconnected.Remove(id);
            }
        }
    }

    /// <summary>Everything seen this session that is not in <paramref name="tree"/>.</summary>
    public IReadOnlyList<MissingDevice> Missing(IReadOnlyList<UsbNode> tree) =>
        seen.Values.Where(device => UsbTree.Find(tree, device.Node.Id) == null).ToList();

    /// <summary>
    /// Everything in <paramref name="tree"/> that disconnected and came back this session, at the port where it is
    /// now, however short or long it was gone.
    /// </summary>
    public IReadOnlyList<MissingDevice> Reconnected(IReadOnlyList<UsbNode> tree) =>
        seen.Values.Where(device => reconnected.Contains(device.Node.Id) && UsbTree.Find(tree, device.Node.Id) != null).ToList();

    /// <summary>Forgets a missing or reconnected device, so it is no longer reported.</summary>
    public void Clear(string id)
    {
        seen.Remove(id);
        gone.Remove(id);
        reconnected.Remove(id);
    }

    private void RecordBelow(UsbNode hub, IReadOnlyList<string> ancestors, HashSet<string> present)
    {
        foreach (var port in hub.Children)
        {
            foreach (var node in port.Children)
            {
                if (!node.Id.StartsWith(port.Id + "|", StringComparison.Ordinal))
                {
                    seen[node.Id] = new MissingDevice(node, port.Id, $"{port.Name} of {hub.Name}", [.. ancestors, port.Id]);
                    present.Add(node.Id);
                }
                if (node.Kind == UsbNodeKind.Hub)
                {
                    RecordBelow(node, [.. ancestors, port.Id, node.Id], present);
                }
            }
        }
    }
}
