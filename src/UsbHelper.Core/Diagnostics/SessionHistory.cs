using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Diagnostics;

/// <summary>A hub or device seen earlier in this session that is no longer connected.</summary>
/// <param name="Node">The hub or device as it was last seen.</param>
/// <param name="PortId">The port it was connected to.</param>
/// <param name="PortName">A readable name for that port, such as "Port 2 of Generic USB Hub".</param>
/// <param name="AncestorIds">The controller and hubs it was behind.</param>
public sealed record MissingDevice(UsbNode Node, string PortId, string PortName, IReadOnlyList<string> AncestorIds);

/// <summary>
/// Every hub and device seen since the application started (owner decision in #2). A device that is gone
/// stays missing until it returns or <see cref="Clear"/> is called. Nothing is saved to disk.
/// Placeholders for devices the operating system could not identify are not remembered: once identified, the
/// same device gets its real id.
/// </summary>
public sealed class SessionHistory
{
    private readonly Dictionary<string, MissingDevice> seen = [];

    /// <summary>Remembers everything in <paramref name="tree"/>. Call after enumerating and after each change.</summary>
    public void Record(IReadOnlyList<UsbNode> tree)
    {
        foreach (var controller in tree)
        {
            foreach (var rootHub in controller.Children)
            {
                RecordBelow(rootHub, [controller.Id, rootHub.Id]);
            }
        }
    }

    /// <summary>Everything seen this session that is not in <paramref name="tree"/>.</summary>
    public IReadOnlyList<MissingDevice> Missing(IReadOnlyList<UsbNode> tree) =>
        seen.Values.Where(device => UsbTree.Find(tree, device.Node.Id) == null).ToList();

    /// <summary>Forgets a missing device, so it is no longer reported.</summary>
    public void Clear(string id) => seen.Remove(id);

    private void RecordBelow(UsbNode hub, IReadOnlyList<string> ancestors)
    {
        foreach (var port in hub.Children)
        {
            foreach (var node in port.Children)
            {
                if (!node.Id.StartsWith(port.Id + "|", StringComparison.Ordinal))
                {
                    seen[node.Id] = new MissingDevice(node, port.Id, $"{port.Name} of {hub.Name}", ancestors);
                }
                if (node.Kind == UsbNodeKind.Hub)
                {
                    RecordBelow(node, [.. ancestors, node.Id]);
                }
            }
        }
    }
}
