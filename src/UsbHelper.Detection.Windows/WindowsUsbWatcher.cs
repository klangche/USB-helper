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
    // Plugging in one device produces several events; they are handled together once no new event came for
    // SettleTime, but never later than MaxWait after the first one.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(2);

    // After a failed read, the whole tree is read again after this delay.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly Action<IReadOnlyList<UsbChange>> onChanges;
    private readonly CmNotifyCallback callback;
    private readonly GCHandle callbackHandle; // Keeps the delegate alive while Windows may call it.
    private readonly Timer timer;
    private readonly Lock pendingLock = new();
    private readonly Lock processLock = new();
    private readonly List<DeviceEvent> pending = [];
    private readonly IntPtr registration;
    private IReadOnlyList<UsbNode> tree;
    private bool disposed;
    private int disposeCalls;
    private bool fullReadDue; // A read failed earlier, so changes may have been missed.
    private DateTime firstPendingAt;

    public WindowsUsbWatcher(IReadOnlyList<UsbNode> tree, Action<IReadOnlyList<UsbChange>> onChanges)
    {
        this.tree = tree;
        this.onChanges = onChanges;
        callback = OnNotification;
        callbackHandle = GCHandle.Alloc(callback);
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
                callbackHandle.Free();
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
        if (Interlocked.Increment(ref disposeCalls) > 1)
        {
            return;
        }
        CM_Unregister_Notification(registration); // Waits for callbacks that are running.
        lock (processLock)
        {
            disposed = true;
        }
        timer.Dispose();
        callbackHandle.Free();
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
                    var now = DateTime.UtcNow;
                    if (pending.Count == 0)
                    {
                        firstPendingAt = now;
                    }
                    pending.Add(new DeviceEvent(instanceId, removed, parent));
                    var wait = Math.Min(SettleTime.Ticks, Math.Max(0, (MaxWait - (now - firstPendingAt)).Ticks));
                    timer.Change(TimeSpan.FromTicks(wait), Timeout.InfiniteTimeSpan);
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
            bool full;
            lock (pendingLock)
            {
                events = [.. pending];
                pending.Clear();
                full = fullReadDue;
                fullReadDue = false;
            }
            if (disposed || (events.Count == 0 && !full))
            {
                return;
            }

            List<UsbChange> changes;
            try
            {
                changes = [.. full ? ReadAll() : ReadAffected(events)];
            }
            catch (Win32Exception)
            {
                // Windows could not list the devices right now. Read everything again shortly, so nothing is lost.
                // The events are kept: a short drop shows only in them.
                lock (pendingLock)
                {
                    pending.InsertRange(0, events);
                    fullReadDue = true;
                    timer.Change(RetryDelay, Timeout.InfiniteTimeSpan);
                }
                return;
            }
            var newTree = UsbTree.Apply(tree, changes);
            changes.AddRange(ReconnectFinder.Find(tree, newTree, events));
            if (changes.Count > 0)
            {
                tree = newTree;
                onChanges(changes);
            }
        }
    }

    private IReadOnlyList<UsbChange> ReadAffected(IReadOnlyList<DeviceEvent> events)
    {
        var plan = RefreshPlanner.Plan(tree, events);
        if (plan.Full)
        {
            return ReadAll();
        }
        var changes = new List<UsbChange>();
        foreach (var hubId in plan.HubIds)
        {
            var oldHub = UsbTree.Find(tree, hubId)!;
            var hubChanges = UsbTreeDiff.CompareHub(oldHub, UsbTreeBuilder.BuildHub(WindowsUsbReader.ReadHub(hubId), oldHub));
            if (hubChanges == null)
            {
                return ReadAll(); // The hub itself changed, for example it could not be read: compare from the top.
            }
            changes.AddRange(hubChanges);
        }
        return changes;
    }

    private IReadOnlyList<UsbChange> ReadAll() => UsbTreeDiff.Compare(tree, UsbTreeBuilder.Build(WindowsUsbReader.Read()));
}
