using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>A device node event from Windows.</summary>
/// <param name="ParentInstanceId">The parent device node, read when the event arrived; null for removals.</param>
internal sealed record DeviceEvent(string InstanceId, bool Removed, string? ParentInstanceId);

/// <param name="HubIds">Hubs to read again; reading a hub also reads everything below it.</param>
/// <param name="Full">The events cannot all be placed in the known tree, so read the whole tree again.</param>
internal sealed record RefreshPlan(IReadOnlyList<string> HubIds, bool Full);

/// <summary>Decides which part of the tree a batch of device events affects.</summary>
internal static class RefreshPlanner
{
    private static readonly RefreshPlan FullRefresh = new([], true);

    public static RefreshPlan Plan(IReadOnlyList<UsbNode> tree, IReadOnlyList<DeviceEvent> events)
    {
        var arrived = events.Where(e => !e.Removed).Select(e => e.InstanceId).ToHashSet();
        var hubs = new HashSet<string>();
        foreach (var e in events)
        {
            string? hub;
            if (UsbTree.Find(tree, e.InstanceId) != null)
            {
                hub = UsbTree.ParentHub(tree, e.InstanceId)?.Id; // Known device or hub: read the hub it is connected to.
            }
            else if (e.Removed)
            {
                continue; // Never part of the tree, for example an interface of a composite device.
            }
            else if (e.ParentInstanceId != null && UsbTree.Find(tree, e.ParentInstanceId) is { } parent)
            {
                hub = parent.Kind == UsbNodeKind.Hub ? parent.Id : UsbTree.ParentHub(tree, parent.Id)?.Id;
            }
            else if (e.ParentInstanceId != null && arrived.Contains(e.ParentInstanceId))
            {
                continue; // Its parent is new too and is read with the parent's own event.
            }
            else
            {
                return FullRefresh;
            }

            if (hub == null)
            {
                return FullRefresh; // A root hub or controller changed.
            }
            hubs.Add(hub);
        }
        return new RefreshPlan(hubs.Where(hub => !HasAncestorIn(tree, hub, hubs)).ToList(), false);
    }

    private static bool HasAncestorIn(IReadOnlyList<UsbNode> tree, string id, HashSet<string> ids) =>
        UsbTree.PathTo(tree, id).SkipLast(1).Any(node => ids.Contains(node.Id));
}
