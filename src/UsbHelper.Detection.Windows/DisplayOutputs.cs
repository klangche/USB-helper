namespace UsbHelper.Detection.Windows;

/// <summary>Which display output technologies (DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY, wingdi.h) count as external displays.</summary>
internal static class DisplayOutputs
{
    private const uint Lvds = 6;
    private const uint DisplayPortEmbedded = 11;
    private const uint UdiEmbedded = 13;
    private const uint Miracast = 15;
    private const uint IndirectWired = 16; // For example DisplayLink.
    private const uint IndirectVirtual = 17;
    private const uint Internal = 0x80000000;

    /// <summary>
    /// True for a display on one of the computer's own outputs outside the computer (HDMI, DisplayPort, DVI,
    /// DisplayPort over USB4 or Thunderbolt, ...). The built-in panel, DisplayLink and wireless displays are not counted.
    /// "Other" (0xFFFFFFFF) counts: leaving it out could hide a display and so hide an exceeded limit. A virtual
    /// machine's screen can report as external this way (the CI runner reports one display).
    /// </summary>
    public static bool IsExternal(uint outputTechnology) =>
        outputTechnology is not (Lvds or DisplayPortEmbedded or UdiEmbedded or Internal or Miracast or IndirectWired or IndirectVirtual);
}
