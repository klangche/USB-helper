using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// Finds drops shorter than the watcher's settle time: a device removed and added again within one batch of
/// events is in the tree before and after, so comparing the trees does not show it.
/// </summary>
internal static class ReconnectFinder
{
    /// <summary>
    /// One <see cref="UsbChangeKind.Reconnected"/> change for each hub or device that was connected before the
    /// batch, has a removal event in it, and is connected again after it.
    /// </summary>
    public static IReadOnlyList<UsbChange> Find(IReadOnlyList<UsbNode> before, IReadOnlyList<UsbNode> after, IReadOnlyList<DeviceEvent> events)
    {
        var changes = new List<UsbChange>();
        foreach (var id in events.Where(e => e.Removed).Select(e => e.InstanceId).Distinct())
        {
            if (UsbTree.Find(before, id) != null && UsbTree.ParentHub(after, id) is { } hub)
            {
                changes.Add(new UsbChange(UsbChangeKind.Reconnected, hub.Id, UsbTree.PathTo(after, id)[^2]));
            }
        }
        return changes;
    }
}
