using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>USB detection on macOS.</summary>
public sealed class MacUsbDetector : IUsbDetector
{
    private readonly IIoRegistryReader _reader;

    public MacUsbDetector() : this(OperatingSystem.IsMacOS() ? new IoRegistryReader() : throw new PlatformNotSupportedException("macOS only."))
    {
    }

    internal static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    private readonly IUsbNotificationSource? _notifications;

    internal MacUsbDetector(IIoRegistryReader reader, IUsbNotificationSource? notifications = null)
    {
        _reader = reader;
        _notifications = notifications;
    }

    public IReadOnlyList<UsbNode> Enumerate() => UsbTreeBuilder.Build(_reader.ReadControllers());

    public IDisposable Watch(IReadOnlyList<UsbNode> tree, Action<IReadOnlyList<UsbChange>> onChanges)
    {
        var notifications = _notifications ?? (OperatingSystem.IsMacOS() ? new IoKitNotificationSource() : throw new PlatformNotSupportedException("macOS only."));
        return new MacUsbWatcher(notifications, Enumerate, tree, onChanges, DebounceInterval);
    }
}
