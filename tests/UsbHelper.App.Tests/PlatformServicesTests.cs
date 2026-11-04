using UsbHelper.App.Platform;

namespace UsbHelper.App.Tests;

public class PlatformServicesTests
{
    [Fact]
    public void UnsupportedPlatformSaysDetectionIsNotAvailable()
    {
        var platform = PlatformServices.Unsupported();

        Assert.False(platform.IsSupported);
        Assert.IsType<UnsupportedUsbDetector>(platform.Detector);
        Assert.Throws<PlatformNotSupportedException>(platform.Detector.Enumerate);
        Assert.Throws<PlatformNotSupportedException>(() => platform.Detector.Watch([], _ => { }));
    }

    [Fact]
    public void UnsupportedPlatformReportsNothingItDoesNotKnow()
    {
        var platform = PlatformServices.Unsupported();
        var identity = platform.ReadIdentity();

        Assert.Null(identity.Manufacturer);
        Assert.Null(identity.Model);
        Assert.Null(identity.Chip);
        Assert.Null(identity.Platform);
        Assert.Equal(Environment.MachineName, identity.MachineName);
        Assert.Null(platform.ReadMonitors()); // unknown, not an empty list
    }

    [Fact]
    public void CurrentIsSupportedOnlyOnWindows()
    {
        var platform = PlatformServices.Current();

        Assert.Equal(OperatingSystem.IsWindows(), platform.IsSupported);
        Assert.Equal(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "this operating system", platform.Name);
    }
}
