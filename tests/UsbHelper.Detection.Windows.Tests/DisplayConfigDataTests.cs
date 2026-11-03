using System.Text;
using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows.Tests;

// #133: each active monitor's name and output type, from QueryDisplayConfig and DisplayConfigGetDeviceInfo data.
public class DisplayConfigDataTests
{
    [Theory]
    [InlineData(0x80000000u, MonitorOutput.Internal)]
    [InlineData(6u, MonitorOutput.Internal)] // LVDS panel
    [InlineData(11u, MonitorOutput.Internal)] // Embedded DisplayPort panel
    [InlineData(13u, MonitorOutput.Internal)] // Embedded UDI panel
    [InlineData(5u, MonitorOutput.Hdmi)]
    [InlineData(10u, MonitorOutput.DisplayPort)] // Including DisplayPort Alt Mode over USB-C
    [InlineData(18u, MonitorOutput.UsbTunnel)] // DisplayPort tunnelled over USB4 or Thunderbolt
    [InlineData(4u, MonitorOutput.Dvi)]
    [InlineData(0u, MonitorOutput.Vga)]
    [InlineData(16u, MonitorOutput.Indirect)] // For example DisplayLink
    [InlineData(17u, MonitorOutput.Virtual)]
    [InlineData(15u, MonitorOutput.Wireless)] // Miracast
    [InlineData(1u, MonitorOutput.Other)] // S-Video
    [InlineData(0xFFFFFFFFu, MonitorOutput.Other)]
    public void OutputTypeComesFromTheOutputTechnology(uint technology, MonitorOutput output)
    {
        Assert.Equal(output, DisplayOutputs.Kind(technology));
    }

    [Fact]
    public void EachTargetIsReadOnceFromThePaths()
    {
        // Clone mode: two paths lead to the same target, so it is one monitor.
        var paths = Paths((Adapter: 0x1_0000_0002L, Target: 4u, Technology: 5u), (0x1_0000_0002L, 4u, 5u), (0x1_0000_0002L, 7u, 0x80000000u));

        var targets = DisplayPaths.Targets(paths, 3);

        Assert.Equal([(0x1_0000_0002L, 4u, 5u), (0x1_0000_0002L, 7u, 0x80000000u)], targets);
    }

    [Fact]
    public void TheNameRequestAsksForTheTargetsName()
    {
        var request = DisplayTargetName.Request(0x1_0000_0002L, 7);

        Assert.Equal(420, request.Length);
        Assert.Equal(2u, BitConverter.ToUInt32(request, 0)); // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
        Assert.Equal(420u, BitConverter.ToUInt32(request, 4));
        Assert.Equal(0x1_0000_0002L, BitConverter.ToInt64(request, 8));
        Assert.Equal(7u, BitConverter.ToUInt32(request, 16));
    }

    [Fact]
    public void TheMonitorNameIsReadFromTheReply()
    {
        var reply = DisplayTargetName.Request(1, 7);
        Encoding.Unicode.GetBytes("DELL U2723QE").CopyTo(reply, 36);

        Assert.Equal("DELL U2723QE", DisplayTargetName.Name(reply));
    }

    [Fact]
    public void AMonitorWithoutANameHasNone()
    {
        // Built-in panels often report no friendly name.
        Assert.Null(DisplayTargetName.Name(DisplayTargetName.Request(1, 7)));
    }

    // Runs the real Windows API on the CI runner. A runner without an interactive display session may refuse.
    [Fact]
    public void ReadingMonitorsOnWindowsGivesAListOrAWindowsError()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            var monitors = WindowsDisplayReader.ReadMonitors();
            Assert.Equal(monitors.Count, monitors.Select(monitor => monitor.Id).Distinct().Count());
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static byte[] Paths(params (long Adapter, uint Target, uint Technology)[] paths)
    {
        var bytes = new byte[paths.Length * DisplayPaths.DISPLAYCONFIG_PATH_INFO_SIZE];
        for (var i = 0; i < paths.Length; i++)
        {
            var offset = i * DisplayPaths.DISPLAYCONFIG_PATH_INFO_SIZE;
            BitConverter.GetBytes(paths[i].Adapter).CopyTo(bytes, offset + DisplayPaths.DISPLAYCONFIG_PATH_TARGET_ADAPTER_OFFSET);
            BitConverter.GetBytes(paths[i].Target).CopyTo(bytes, offset + DisplayPaths.DISPLAYCONFIG_PATH_TARGET_ID_OFFSET);
            BitConverter.GetBytes(paths[i].Technology).CopyTo(bytes, offset + DisplayPaths.DISPLAYCONFIG_PATH_OUTPUT_TECHNOLOGY_OFFSET);
        }
        return bytes;
    }
}
