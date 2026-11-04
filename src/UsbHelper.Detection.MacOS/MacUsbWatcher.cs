using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>
/// Turns notification signals into <see cref="UsbChange"/> batches: waits until a burst of signals is quiet, reads the
/// topology again and diffs it against the last known tree. Batches are delivered one at a time.
/// </summary>
internal sealed class MacUsbWatcher : IDisposable
{
    private readonly Func<IReadOnlyList<UsbNode>> _read;
    private readonly Action<IReadOnlyList<UsbChange>> _onChanges;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private readonly IDisposable _subscription;
    private IReadOnlyList<UsbNode> _tree;
    private bool _disposed;

    public MacUsbWatcher(
        IUsbNotificationSource source,
        Func<IReadOnlyList<UsbNode>> read,
        IReadOnlyList<UsbNode> tree,
        Action<IReadOnlyList<UsbChange>> onChanges,
        TimeSpan debounce)
    {
        _read = read;
        _tree = tree;
        _onChanges = onChanges;
        _debounce = debounce;
        _timer = new Timer(_ => Flush());
        _subscription = source.Start(Signal);
    }

    private void Signal()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            IReadOnlyList<UsbChange> changes;
            try
            {
                var current = _read();
                changes = UsbTreeDiff.Compare(_tree, current);
                _tree = current;
            }
            catch (Exception)
            {
                // The registry can be mid-change; the next signal reads again against the unchanged last tree.
                return;
            }

            if (changes.Count > 0)
            {
                _onChanges(changes);
            }
        }
    }

    public void Dispose()
    {
        // Stop the source first so no new signal arrives, then wait for a running batch via the lock.
        _subscription.Dispose();
        lock (_gate)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
