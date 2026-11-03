using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows;

/// <summary>The output types (DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY, wingdi.h) of a monitor.</summary>
internal static class DisplayOutputs
{
    private const uint Hd15 = 0; // VGA
    private const uint Dvi = 4;
    private const uint Hdmi = 5;
    private const uint Lvds = 6;
    private const uint DisplayPortExternal = 10;
    private const uint DisplayPortEmbedded = 11;
    private const uint UdiEmbedded = 13;
    private const uint Miracast = 15;
    private const uint IndirectWired = 16; // For example DisplayLink.
    private const uint IndirectVirtual = 17;
    private const uint DisplayPortUsbTunnel = 18;
    private const uint Internal = 0x80000000;

    /// <summary>The output type; everything without its own type, "Other" (0xFFFFFFFF) included, is <see cref="MonitorOutput.Other"/>.</summary>
    public static MonitorOutput Kind(uint outputTechnology) => outputTechnology switch
    {
        Internal or Lvds or DisplayPortEmbedded or UdiEmbedded => MonitorOutput.Internal,
        Hdmi => MonitorOutput.Hdmi,
        DisplayPortExternal => MonitorOutput.DisplayPort,
        DisplayPortUsbTunnel => MonitorOutput.UsbTunnel,
        Dvi => MonitorOutput.Dvi,
        Hd15 => MonitorOutput.Vga,
        IndirectWired => MonitorOutput.Indirect,
        IndirectVirtual => MonitorOutput.Virtual,
        Miracast => MonitorOutput.Wireless,
        _ => MonitorOutput.Other,
    };

    /// <summary>
    /// True for a display on one of the computer's own outputs outside the computer (see <see cref="MonitorOutputs.IsExternal"/>).
    /// A virtual machine's screen can report as external this way (the CI runner reports one display).
    /// </summary>
    public static bool IsExternal(uint outputTechnology) => Kind(outputTechnology).IsExternal();
}
