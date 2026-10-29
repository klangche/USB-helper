using System.Runtime.Versioning;
using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>USB detection on Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUsbDetector : IUsbDetector
{
    public IReadOnlyList<UsbNode> Enumerate() => UsbTreeBuilder.Build(WindowsUsbReader.Read());
}
