using System.ComponentModel;

namespace UsbHelper.Detection.Windows.Tests;

public class DisplayOutputsTests
{
    [Theory]
    [InlineData(0u)] // VGA (HD15)
    [InlineData(4u)] // DVI
    [InlineData(5u)] // HDMI
    [InlineData(10u)] // DisplayPort, including DisplayPort Alt Mode over USB-C
    [InlineData(18u)] // DisplayPort tunnelled over USB4 or Thunderbolt
    [InlineData(0xFFFFFFFFu)] // Other
    public void ExternalOutputsCount(uint technology)
    {
        Assert.True(DisplayOutputs.IsExternal(technology));
    }

    [Theory]
    [InlineData(0x80000000u)] // Internal panel
    [InlineData(6u)] // LVDS panel
    [InlineData(11u)] // Embedded DisplayPort panel
    [InlineData(13u)] // Embedded UDI panel
    [InlineData(15u)] // Miracast
    [InlineData(16u)] // Indirect wired, for example DisplayLink
    [InlineData(17u)] // Indirect virtual
    public void BuiltInDisplayLinkAndWirelessDoNotCount(uint technology)
    {
        Assert.False(DisplayOutputs.IsExternal(technology));
    }

    // Runs the real Windows API on the CI runner. A runner without an interactive display session may refuse.
    [Fact]
    public void CountingDisplaysOnWindowsGivesACountOrAWindowsError()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            Assert.True(WindowsDisplayReader.CountExternalDisplays() >= 0);
        }
        catch (Win32Exception)
        {
            // Accepted: the reader reports Windows' refusal instead of guessing a count.
        }
    }
}
