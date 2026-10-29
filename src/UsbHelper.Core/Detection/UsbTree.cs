namespace UsbHelper.Core.Detection;

/// <summary>Lookups and updates on a USB tree (the list of controllers).</summary>
public static class UsbTree
{
    /// <summary>The nodes from a controller down to the node with <paramref name="id"/>, or empty when not found.</summary>
    public static IReadOnlyList<UsbNode> PathTo(IReadOnlyList<UsbNode> roots, string id)
    {
        foreach (var root in roots)
        {
            if (root.Id == id)
            {
                return [root];
            }
            var below = PathTo(root.Children, id);
            if (below.Count > 0)
            {
                return [root, .. below];
            }
        }
        return [];
    }

    public static UsbNode? Find(IReadOnlyList<UsbNode> roots, string id) => PathTo(roots, id).LastOrDefault();

    /// <summary>The hub whose port the node with <paramref name="id"/> is connected to, or null (not found, or a root hub or controller).</summary>
    public static UsbNode? ParentHub(IReadOnlyList<UsbNode> roots, string id)
    {
        var path = PathTo(roots, id);
        return path.Count >= 3 && path[^2].Kind == UsbNodeKind.Port ? path[^3] : null;
    }

    /// <summary>Returns the tree with <paramref name="changes"/> applied. Unaffected nodes are kept as they are.</summary>
    public static IReadOnlyList<UsbNode> Apply(IReadOnlyList<UsbNode> roots, IEnumerable<UsbChange> changes)
    {
        foreach (var change in changes)
        {
            roots = change.ParentId == null
                ? ApplyToControllers(roots, change)
                : Replace(roots, change.Node);
        }
        return roots;
    }

    private static IReadOnlyList<UsbNode> ApplyToControllers(IReadOnlyList<UsbNode> roots, UsbChange change) => change.Kind switch
    {
        UsbChangeKind.Added => [.. roots, change.Node],
        UsbChangeKind.Removed => roots.Where(root => root.Id != change.Node.Id).ToList(),
        _ => roots.Select(root => root.Id == change.Node.Id ? change.Node : root).ToList(),
    };

    // Replaces the node with the same id, rebuilding only the nodes on the path to it.
    private static IReadOnlyList<UsbNode> Replace(IReadOnlyList<UsbNode> nodes, UsbNode replacement)
    {
        var result = new UsbNode[nodes.Count];
        var changed = false;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Id == replacement.Id)
            {
                result[i] = replacement;
                changed = true;
                continue;
            }
            var children = Replace(node.Children, replacement);
            result[i] = ReferenceEquals(children, node.Children) ? node : node with { Children = children };
            changed |= !ReferenceEquals(result[i], node);
        }
        return changed ? result : nodes;
    }
}
