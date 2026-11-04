using UsbHelper.Core.Detection;
using UsbHelper.Detection.MacOS;

namespace UsbHelper.Detection.MacOS.Tests;

public class MacUsbWatcherTests
{
    private sealed class FakeSource : IUsbNotificationSource
    {
        public Action? Signal;
        public bool Disposed;
        public Exception? FailWith;

        public IDisposable Start(Action onSignal)
        {
            if (FailWith != null)
            {
                throw FailWith;
            }

            Signal = onSignal;
            return new Stop(this);
        }

        private sealed class Stop(FakeSource source) : IDisposable
        {
            public void Dispose() => source.Disposed = true;
        }
    }

    private static UsbNode Port(string id, params UsbNode[] children) =>
        new(id, UsbNodeKind.Port, "port", children) { PortNumber = 1 };

    private static UsbNode Tree(params UsbNode[] ports) =>
        new("c", UsbNodeKind.Controller, "ctl", [new UsbNode("h", UsbNodeKind.Hub, "hub", ports)]);

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    private static UsbNode Empty => Tree(Port("p1"));

    private static UsbNode Occupied => Tree(Port("p1", new UsbNode("d", UsbNodeKind.Device, "dev", [])));

    [Fact]
    public void A_burst_of_signals_gives_one_batch()
    {
        var source = new FakeSource();
        var batches = new List<IReadOnlyList<UsbChange>>();
        var delivered = new ManualResetEventSlim();
        var reads = 0;
        using var watcher = new MacUsbWatcher(source, () => { reads++; return [Occupied]; }, [Empty],
            c => { lock (batches) { batches.Add(c); } delivered.Set(); }, Short);

        for (var i = 0; i < 5; i++)
        {
            source.Signal!();
        }

        Assert.True(delivered.Wait(5000));
        Thread.Sleep(200);
        lock (batches)
        {
            Assert.Single(batches);
            Assert.Equal(UsbChangeKind.Added, batches[0].Single().Kind);
        }

        Assert.Equal(1, reads);
    }

    [Fact]
    public void A_signal_without_a_difference_gives_no_batch()
    {
        var source = new FakeSource();
        var read = new ManualResetEventSlim();
        var called = false;
        using var watcher = new MacUsbWatcher(source, () => { read.Set(); return [Empty]; }, [Empty], _ => called = true, Short);

        source.Signal!();

        Assert.True(read.Wait(5000));
        Thread.Sleep(100);
        Assert.False(called);
    }

    [Fact]
    public void A_failed_read_is_retried_on_the_next_signal()
    {
        var source = new FakeSource();
        var fail = true;
        var delivered = new ManualResetEventSlim();
        using var watcher = new MacUsbWatcher(source, () => fail ? throw new InvalidOperationException() : [Occupied], [Empty],
            _ => delivered.Set(), Short);

        source.Signal!();
        Thread.Sleep(300);
        fail = false;
        source.Signal!();

        Assert.True(delivered.Wait(5000));
    }

    [Fact]
    public void Dispose_stops_the_source_and_drops_pending_changes()
    {
        var source = new FakeSource();
        var called = false;
        var watcher = new MacUsbWatcher(source, () => [Occupied], [Empty], _ => called = true, TimeSpan.FromMilliseconds(200));

        source.Signal!();
        watcher.Dispose();
        Thread.Sleep(500);

        Assert.True(source.Disposed);
        Assert.False(called);
        watcher.Dispose();
    }

    [Fact]
    public void A_registration_failure_is_thrown_not_hidden()
    {
        var source = new FakeSource { FailWith = new InvalidOperationException("no notifications") };

        Assert.Throws<InvalidOperationException>(() => new MacUsbWatcher(source, () => [Empty], [Empty], _ => { }, Short));
    }

    [Fact]
    public void Detector_watch_uses_the_notification_source()
    {
        var source = new FakeSource();
        var detector = new MacUsbDetector(new EmptyReader(), source);

        using var watch = detector.Watch(detector.Enumerate(), _ => { });

        Assert.NotNull(source.Signal);
    }

    private sealed class EmptyReader : IIoRegistryReader
    {
        public IReadOnlyList<RegistryEntry> ReadControllers() => [];
    }
}
