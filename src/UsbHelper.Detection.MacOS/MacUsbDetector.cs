using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>USB detection on macOS.</summary>
public sealed class MacUsbDetector : IUsbDetector
{
    private readonly IIoRegistryReader _reader;

    public MacUsbDetector() : this(OperatingSystem.IsMacOS() ? new IoRegistryReader() : throw new PlatformNotSupportedException("macOS only."))
    {
    }

    internal MacUsbDetector(IIoRegistryReader reader) => _reader = reader;

    public IReadOnlyList<UsbNode> Enumerate() => UsbTreeBuilder.Build(_reader.ReadControllers());

    /// <summary>Added by the change-detection step (#157).</summary>
    public IDisposable Watch(IReadOnlyList<UsbNode> tree, Action<IReadOnlyList<UsbChange>> onChanges) =>
        throw new NotSupportedException("macOS change detection is not implemented yet.");
}
