namespace UsbHelper.Detection.MacOS;

/// <summary>
/// Tells the watcher that USB devices were attached or removed. A fake implementation makes batching and disposal
/// testable without hardware.
/// </summary>
internal interface IUsbNotificationSource
{
    /// <summary>
    /// Starts delivering signals; <paramref name="onSignal"/> may be called from any thread, any number of times.
    /// Throws when the notifications cannot be registered. Disposing stops delivery and returns only after the last
    /// running <paramref name="onSignal"/> has finished.
    /// </summary>
    IDisposable Start(Action onSignal);
}
