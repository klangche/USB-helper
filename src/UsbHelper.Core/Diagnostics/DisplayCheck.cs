namespace UsbHelper.Core.Diagnostics;

/// <summary>External displays compared with the computer profile. Kept apart from the topology limits.</summary>
/// <param name="ExternalDisplays">
/// Active external displays driven by the computer's own outputs (DisplayPort, Thunderbolt, USB4, HDMI, ...),
/// as the operating system reports them; null when unknown. DisplayLink and wireless displays are not included.
/// </param>
/// <param name="MaxExternal"><c>displays.max_external</c> from the profile; null when unknown.</param>
/// <param name="DisplayLinkAdapters">Ids of DisplayLink adapters found in the USB tree.</param>
public sealed record DisplayCheck(
    int? ExternalDisplays,
    long? MaxExternal,
    bool Verified,
    string? Source,
    LimitState State,
    IReadOnlyList<string> DisplayLinkAdapters);
