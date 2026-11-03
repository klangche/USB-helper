namespace UsbHelper.Core.Detection;

/// <summary>
/// Where each monitor is shown (#143): under the USB hub or device that Windows puts in the same container (the
/// physical device both belong to, such as a USB-C monitor with a built-in hub), else in the Displays group. Nothing is
/// guessed: only an equal container id links.
/// </summary>
/// <param name="Linked">The linked monitors by the id of the USB node they are shown under.</param>
/// <param name="Unlinked">The monitors with no USB node in their container, in their order.</param>
public sealed record MonitorPlacement(IReadOnlyDictionary<string, IReadOnlyList<DisplayMonitor>> Linked, IReadOnlyList<DisplayMonitor> Unlinked)
{
    /// <summary>
    /// The container id Windows gives every device built into the computer
    /// (https://learn.microsoft.com/windows-hardware/drivers/install/container-ids). The laptop panel and the built-in
    /// webcam share it without one being inside the other, so it links nothing.
    /// </summary>
    public static readonly Guid ThisComputer = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>Links each monitor to the highest USB node in its container: a USB-C monitor's hub, not a device behind it.</summary>
    public static MonitorPlacement Place(IReadOnlyList<UsbNode> tree, IReadOnlyList<DisplayMonitor> monitors)
    {
        var linked = new Dictionary<string, List<DisplayMonitor>>();
        var unlinked = new List<DisplayMonitor>();
        foreach (var monitor in monitors)
        {
            var node = monitor.ContainerId is { } id && id != ThisComputer && id != Guid.Empty
                ? tree.SelectMany(TopDown).FirstOrDefault(candidate => candidate.ContainerId == id)
                : null;
            if (node == null)
            {
                unlinked.Add(monitor);
            }
            else
            {
                (linked.TryGetValue(node.Id, out var list) ? list : linked[node.Id] = []).Add(monitor);
            }
        }
        return new(linked.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DisplayMonitor>)pair.Value), unlinked);
    }

    // Breadth first, so a node is met before anything below it.
    private static IEnumerable<UsbNode> TopDown(UsbNode root)
    {
        var queue = new Queue<UsbNode>([root]);
        while (queue.TryDequeue(out var node))
        {
            yield return node;
            foreach (var child in node.Children)
            {
                queue.Enqueue(child);
            }
        }
    }
}
