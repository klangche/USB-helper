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
    private readonly object _readGate = new();
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

    // The read runs outside _gate, so an IOKit signal or Dispose never waits for it: it can take a while, since it also
    // runs system_profiler for the USB4 routers (#332). _readGate keeps two flushes from reading at the same time.
    private void Flush()
    {
        lock (_readGate)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            IReadOnlyList<UsbNode> current;
            try
            {
                current = _read();
            }
            catch (Exception)
            {
                // The registry can be mid-change; the next signal reads again against the unchanged last tree.
                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                var changes = UsbTreeDiff.Compare(_tree, current);
                _tree = current;
                if (changes.Count > 0)
                {
                    _onChanges(changes);
                }
            }
        }
    }

    public void Dispose()
    {
        // Stop the source first so no new signal arrives, then wait for a running change callback via the lock. A read in
        // progress is not waited for: its flush sees _disposed and drops what it read.
        _subscription.Dispose();
        lock (_gate)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
