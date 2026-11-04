using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UsbHelper.Detection.MacOS;

/// <summary>
/// IOKit first-match and terminated notifications for USB devices. A dedicated thread runs a CoreFoundation run loop that
/// delivers them; registration failures are thrown from <see cref="Start"/>.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class IoKitNotificationSource : IUsbNotificationSource
{
    public IDisposable Start(Action onSignal)
    {
        var subscription = new Subscription(onSignal);
        subscription.Begin();
        return subscription;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _onSignal;
        private readonly Thread _thread;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _stop;
        private GCHandle _self;
        private MacAppActivity? _activity;

        public Subscription(Action onSignal)
        {
            _onSignal = onSignal;
            _thread = new Thread(Run) { IsBackground = true, Name = "USB IOKit notifications" };
        }

        public void Begin()
        {
            _self = GCHandle.Alloc(this);
            _thread.Start();
            try
            {
                _ready.Task.GetAwaiter().GetResult();
            }
            catch
            {
                _thread.Join();
                _self.Free();
                throw;
            }

            _activity = MacAppActivity.Begin("Following USB changes");
        }

        public void Dispose()
        {
            _stop = true;
            _activity?.Dispose();
            _thread.Join();
            _self.Free();
        }

        private void Run()
        {
            IntPtr port = IntPtr.Zero;
            var iterators = new List<uint>();
            try
            {
                port = IoKit.IONotificationPortCreate(0);
                if (port == IntPtr.Zero)
                {
                    throw new InvalidOperationException("IONotificationPortCreate failed; USB change notifications are unavailable.");
                }

                foreach (var type in new[] { IoKit.FirstMatchNotification, IoKit.TerminatedNotification })
                {
                    var matching = IoKit.IOServiceMatching("IOUSBHostDevice");
                    if (matching == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("IOServiceMatching failed; USB change notifications are unavailable.");
                    }

                    var result = IoKit.AddNotification(port, type, matching, GCHandle.ToIntPtr(_self), out var iterator);
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"IOServiceAddMatchingNotification({type}) failed with 0x{result:x}; USB change notifications are unavailable.");
                    }

                    iterators.Add(iterator);
                    Drain(iterator);
                }

                var source = IoKit.IONotificationPortGetRunLoopSource(port);
                IoKit.AddToCurrentRunLoop(source);
                _ready.TrySetResult();

                try
                {
                    while (!_stop)
                    {
                        IoKit.RunCurrentLoop(0.25);
                    }
                }
                finally
                {
                    IoKit.RemoveFromCurrentRunLoop(source);
                }
            }
            catch (Exception ex)
            {
                _ready.TrySetException(ex);
            }
            finally
            {
                foreach (var iterator in iterators)
                {
                    IoKit.IOObjectRelease(iterator);
                }

                if (port != IntPtr.Zero)
                {
                    IoKit.IONotificationPortDestroy(port);
                }
            }
        }

        /// <summary>Walks the iterator so IOKit arms the notification again; returns whether it held any device.</summary>
        private static bool Drain(uint iterator)
        {
            var any = false;
            uint item;
            while ((item = IoKit.IOIteratorNext(iterator)) != 0)
            {
                any = true;
                IoKit.IOObjectRelease(item);
            }

            return any;
        }

        [UnmanagedCallersOnly]
        internal static void Callback(IntPtr refcon, uint iterator)
        {
            if (GCHandle.FromIntPtr(refcon).Target is not Subscription self)
            {
                return;
            }

            if (Drain(iterator))
            {
                try
                {
                    self._onSignal();
                }
                catch (Exception)
                {
                    // An exception must not cross into native code; the watcher's own signal does not throw.
                }
            }
        }
    }

    internal static unsafe delegate* unmanaged<IntPtr, uint, void> CallbackPointer => &Subscription.Callback;
}
