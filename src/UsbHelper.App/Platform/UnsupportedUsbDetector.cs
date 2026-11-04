using UsbHelper.Core.Detection;

namespace UsbHelper.App.Platform;

/// <summary>Stands in where no USB detection exists yet. It has no tree to give, so it throws instead of returning an empty one.</summary>
public sealed class UnsupportedUsbDetector : IUsbDetector
{
    public IReadOnlyList<UsbNode> Enumerate() => throw new PlatformNotSupportedException("USB detection is not available on this operating system yet.");

    public IDisposable Watch(IReadOnlyList<UsbNode> tree, Action<IReadOnlyList<UsbChange>> onChanges) =>
        throw new PlatformNotSupportedException("USB detection is not available on this operating system yet.");
}
