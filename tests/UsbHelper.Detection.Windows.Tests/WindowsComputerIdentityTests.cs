using System.Runtime.InteropServices;
using UsbHelper.Detection.Windows;

namespace UsbHelper.Detection.Windows.Tests;

public class WindowsComputerIdentityTests
{
    [Theory]
    [InlineData(Architecture.X64, "GenuineIntel", "windows-intel")]
    [InlineData(Architecture.X64, "AuthenticAMD", "windows-amd")]
    [InlineData(Architecture.Arm64, "Qualcomm Technologies Inc", "windows-arm")]
    [InlineData(Architecture.Arm64, null, "windows-arm")]
    [InlineData(Architecture.X64, "SomethingElse", null)] // Unknown vendor: no guess.
    [InlineData(Architecture.X64, null, null)]
    [InlineData(Architecture.X86, "GenuineIntel", null)] // No platform profile for 32-bit Windows.
    public void PlatformFollowsArchitectureAndProcessorVendor(Architecture architecture, string? vendor, string? platform)
    {
        Assert.Equal(platform, WindowsComputerIdentity.PlatformFor(architecture, vendor));
    }
}
