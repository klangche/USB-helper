using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Tests.Detection;

public class DisplayMonitorTests
{
    [Theory]
    [InlineData(MonitorOutput.Hdmi)]
    [InlineData(MonitorOutput.DisplayPort)]
    [InlineData(MonitorOutput.UsbTunnel)]
    [InlineData(MonitorOutput.Dvi)]
    [InlineData(MonitorOutput.Vga)]
    [InlineData(MonitorOutput.Other)] // Leaving it out could hide an exceeded limit.
    public void MonitorsOnTheComputersOwnOutputsAreExternal(MonitorOutput output)
    {
        Assert.True(output.IsExternal());
    }

    [Theory]
    [InlineData(MonitorOutput.Internal)]
    [InlineData(MonitorOutput.Indirect)]
    [InlineData(MonitorOutput.Wireless)]
    [InlineData(MonitorOutput.Virtual)]
    public void BuiltInDisplayLinkWirelessAndVirtualAreNotExternal(MonitorOutput output)
    {
        Assert.False(output.IsExternal());
    }

    [Fact]
    public void TheExternalCountLeavesOutTheOthersAndIsUnknownWhenTheMonitorsAre()
    {
        IReadOnlyList<DisplayMonitor> monitors =
            [new("1", null, MonitorOutput.Internal), new("2", "DELL", MonitorOutput.Hdmi), new("3", "LG", MonitorOutput.Indirect)];

        Assert.Equal(1, DisplayMonitor.ExternalCount(monitors));
        Assert.Null(DisplayMonitor.ExternalCount(null));
    }
}
