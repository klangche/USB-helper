using System.ComponentModel;
using System.Runtime.Versioning;
using UsbHelper.Core.Detection;
using UsbHelper.Detection.Windows;

namespace UsbHelper.App.Platform;

/// <summary>
/// The operating system's detection behind the Core contracts (#160): USB detector, computer identity and displays.
/// <see cref="Current"/> is the only place that picks them; the rest of the app uses only this.
/// </summary>
/// <param name="Name">The operating system, as shown in the log.</param>
/// <param name="IsSupported">False when detection for this operating system does not exist yet.</param>
/// <param name="ReadMonitors">The displays, or null (unknown) when the operating system cannot report them.</param>
public sealed record PlatformServices(
    string Name,
    bool IsSupported,
    IUsbDetector Detector,
    Func<ComputerIdentity> ReadIdentity,
    Func<IReadOnlyList<DisplayMonitor>?> ReadMonitors)
{
    public static PlatformServices Current() => OperatingSystem.IsWindows() ? Windows() : Unsupported();

    [SupportedOSPlatform("windows")]
    public static PlatformServices Windows() => new(
        "Windows", IsSupported: true, new WindowsUsbDetector(), WindowsComputerIdentity.Read, ReadWindowsMonitors);

    /// <summary>
    /// Placeholder until macOS detection exists (#156-#158) and for any other operating system: it reports that detection
    /// is not available and never returns made-up data.
    /// </summary>
    public static PlatformServices Unsupported() => new(
        OperatingSystem.IsMacOS() ? "macOS" : "this operating system", IsSupported: false,
        new UnsupportedUsbDetector(), () => new ComputerIdentity(null, null, MachineName: Environment.MachineName), () => null);

    // Unknown rather than a guess when Windows cannot report the display configuration.
    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<DisplayMonitor>? ReadWindowsMonitors()
    {
        try
        {
            return WindowsDisplayReader.ReadMonitors();
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
