namespace UsbHelper.Core.Detection;

/// <summary>Finds the smallest set of <see cref="UsbChange"/>s that turns one tree into another.</summary>
public static class UsbTreeDiff
{
    /// <summary>Changes between two whole trees (lists of controllers).</summary>
    public static IReadOnlyList<UsbChange> Compare(IReadOnlyList<UsbNode> oldRoots, IReadOnlyList<UsbNode> newRoots)
    {
        var changes = new List<UsbChange>();
        foreach (var oldRoot in oldRoots.Where(old => newRoots.All(current => current.Id != old.Id)))
        {
            changes.Add(new UsbChange(UsbChangeKind.Removed, null, oldRoot));
        }
        foreach (var newRoot in newRoots)
        {
            var oldRoot = oldRoots.FirstOrDefault(old => old.Id == newRoot.Id);
            if (oldRoot == null)
            {
                changes.Add(new UsbChange(UsbChangeKind.Added, null, newRoot));
            }
            else if (SameNode(oldRoot, newRoot) && oldRoot.Children.Count == 1 && newRoot.Children.Count == 1
                && SameNode(oldRoot.Children[0], newRoot.Children[0]))
            {
                changes.AddRange(CompareHub(oldRoot.Children[0], newRoot.Children[0])); // Same root hub: compare its ports.
            }
            else if (!SameTree(oldRoot, newRoot))
            {
                changes.Add(new UsbChange(UsbChangeKind.Changed, null, newRoot));
            }
        }
        return changes;
    }

    /// <summary>Changes on the ports of one hub, read twice. Both must have the same id.</summary>
    public static IReadOnlyList<UsbChange> CompareHub(UsbNode oldHub, UsbNode newHub)
    {
        var changes = new List<UsbChange>();
        foreach (var newPort in newHub.Children)
        {
            var oldPort = oldHub.Children.FirstOrDefault(port => port.Id == newPort.Id);
            var oldChild = oldPort?.Children.FirstOrDefault();
            var newChild = newPort.Children.FirstOrDefault();
            if (oldPort != null && SameTree(oldPort, newPort))
            {
                continue;
            }
            if (oldPort != null && oldChild != null && newChild != null && oldChild.Kind == UsbNodeKind.Hub
                && SameNode(oldPort, newPort) && SameNode(oldChild, newChild))
            {
                changes.AddRange(CompareHub(oldChild, newChild)); // Same hub on the port: look further down.
                continue;
            }
            var kind = oldChild == null && newChild != null ? UsbChangeKind.Added
                : oldChild != null && newChild == null ? UsbChangeKind.Removed
                : UsbChangeKind.Changed;
            changes.Add(new UsbChange(kind, newHub.Id, newPort));
        }
        return changes;
    }

    /// <summary>Same node and same subtree.</summary>
    public static bool SameTree(UsbNode a, UsbNode b) =>
        SameNode(a, b) && a.Children.Count == b.Children.Count && a.Children.Zip(b.Children).All(pair => SameTree(pair.First, pair.Second));

    // Same fields, ignoring children.
    private static bool SameNode(UsbNode a, UsbNode b) =>
        a.Id == b.Id && a.Kind == b.Kind && a.Name == b.Name && a.PortNumber == b.PortNumber
        && a.VendorId == b.VendorId && a.ProductId == b.ProductId && a.Problem == b.Problem;
}
