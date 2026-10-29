using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UsbHelper.Core.Detection;
using static UsbHelper.Detection.Windows.NativeMethods;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// Listens for USB device node events with CM_Register_Notification (no window, no administrator rights),
/// waits until events settle, then reads only the affected hubs again and reports what changed.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsUsbWatcher : IDisposable
{
    // Plugging in one device produces several events; they are handled together.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    private readonly Action<IReadOnlyList<UsbChange>> onChanges;
    private readonly CmNotifyCallback callback; // Kept in a field so the delegate is not collected while registered.
    private readonly Timer timer;
    private readonly Lock pendingLock = new();
    private readonly Lock processLock = new();
    private readonly List<DeviceEvent> pending = [];
    private readonly IntPtr registration;
    private IReadOnlyList<UsbNode> tree;
    private bool disposed;

    public WindowsUsbWatcher(IReadOnlyList<UsbNode> tree, Action<IReadOnlyList<UsbChange>> onChanges)
    {
        this.tree = tree;
        this.onChanges = onChanges;
        callback = OnNotification;
        timer = new Timer(_ => Process());

        var filter = Marshal.AllocHGlobal(CM_NOTIFY_FILTER_SIZE);
        try
        {
            Marshal.Copy(new byte[CM_NOTIFY_FILTER_SIZE], 0, filter, CM_NOTIFY_FILTER_SIZE);
            Marshal.WriteInt32(filter, 0, CM_NOTIFY_FILTER_SIZE);
            Marshal.WriteInt32(filter, 4, (int)CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES);
            Marshal.WriteInt32(filter, 8, (int)CM_NOTIFY_FILTER_TYPE_DEVICEINSTANCE);
            var result = CM_Register_Notification(filter, IntPtr.Zero, callback, out registration);
            if (result != CR_SUCCESS)
            {
                timer.Dispose();
                throw new InvalidOperationException($"CM_Register_Notification failed (CONFIGRET {result}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(filter);
        }
    }

    public void Dispose()
    {
        CM_Unregister_Notification(registration); // Waits for callbacks that are running.
        lock (processLock)
        {
            disposed = true;
        }
        timer.Dispose();
    }

    private uint OnNotification(IntPtr notify, IntPtr context, uint action, IntPtr eventData, uint eventDataSize)
    {
        if (action is CM_NOTIFY_ACTION_DEVICEINSTANCEENUMERATED or CM_NOTIFY_ACTION_DEVICEINSTANCESTARTED or CM_NOTIFY_ACTION_DEVICEINSTANCEREMOVED)
        {
            var instanceId = Marshal.PtrToStringUni(eventData + CM_NOTIFY_EVENT_DATA_INSTANCE_ID_OFFSET)!.ToUpperInvariant();
            if (instanceId.StartsWith(@"USB\", StringComparison.Ordinal))
            {
                var removed = action == CM_NOTIFY_ACTION_DEVICEINSTANCEREMOVED;
                // Read the parent now: once the batch is handled the device may be gone again.
                var parent = removed ? null : WindowsUsbReader.ParentInstanceId(instanceId);
                lock (pendingLock)
                {
                    pending.Add(new DeviceEvent(instanceId, removed, parent));
                    timer.Change(SettleTime, Timeout.InfiniteTimeSpan);
                }
            }
        }
        return 0; // ERROR_SUCCESS
    }

    private void Process()
    {
        lock (processLock)
        {
            List<DeviceEvent> events;
            lock (pendingLock)
            {
                events = [.. pending];
                pending.Clear();
            }
            if (disposed || events.Count == 0)
            {
                return;
            }

            IReadOnlyList<UsbChange> changes;
            try
            {
                var plan = RefreshPlanner.Plan(tree, events);
                changes = plan.Full
                    ? UsbTreeDiff.Compare(tree, UsbTreeBuilder.Build(WindowsUsbReader.Read()))
                    : plan.HubIds
                        .SelectMany(id => UsbTreeDiff.CompareHub(UsbTree.Find(tree, id)!, UsbTreeBuilder.BuildHub(WindowsUsbReader.ReadHub(id), id)))
                        .ToList();
            }
            catch (Win32Exception)
            {
                return; // Windows could not list the devices right now; the next event reads again.
            }
            if (changes.Count > 0)
            {
                tree = UsbTree.Apply(tree, changes);
                onChanges(changes);
            }
        }
    }
}
