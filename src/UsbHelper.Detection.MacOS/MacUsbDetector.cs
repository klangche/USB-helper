using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>USB detection on macOS.</summary>
public sealed class MacUsbDetector : IUsbDetector
{
    private readonly IIoRegistryReader _reader;

    public MacUsbDetector() : this(OperatingSystem.IsMacOS() ? new IoRegistryReader() : throw new PlatformNotSupportedException("macOS only."),
        readBuses: ReadBuses)
    {
    }

    // The USB4/Thunderbolt buses with their routers (#332), from system_profiler; none when it cannot be read.
    private static IReadOnlyList<ThunderboltBus>? ReadBuses()
    {
        try
        {
            return MacThunderbolt.Parse(new SystemProfiler().Read("SPThunderboltDataType"));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private readonly Func<IReadOnlyList<ThunderboltBus>?>? _readBuses;

    internal static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    private readonly IUsbNotificationSource? _notifications;

    internal MacUsbDetector(IIoRegistryReader reader, IUsbNotificationSource? notifications = null, Func<IReadOnlyList<ThunderboltBus>?>? readBuses = null)
    {
        _reader = reader;
        _notifications = notifications;
        _readBuses = readBuses;
    }

    public IReadOnlyList<UsbNode> Enumerate() => UsbTreeBuilder.Build(_reader.ReadControllers(), _readBuses?.Invoke());

    public IDisposable Watch(IReadOnlyList<UsbNode> tree, Action<IReadOnlyList<UsbChange>> onChanges)
    {
        var notifications = _notifications ?? (OperatingSystem.IsMacOS() ? new IoKitNotificationSource() : throw new PlatformNotSupportedException("macOS only."));
        return new MacUsbWatcher(notifications, Enumerate, tree, onChanges, DebounceInterval);
    }
}
