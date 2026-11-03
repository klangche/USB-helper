namespace UsbHelper.Core.Detection;

/// <summary>How a monitor is connected, as the operating system reports it.</summary>
public enum MonitorOutput
{
    /// <summary>The computer's own panel.</summary>
    Internal,
    Hdmi,
    /// <summary>DisplayPort, DisplayPort Alt Mode over USB-C included.</summary>
    DisplayPort,
    /// <summary>DisplayPort tunnelled over USB4 or Thunderbolt.</summary>
    UsbTunnel,
    Dvi,
    Vga,
    /// <summary>An indirect display on a wired adapter, for example DisplayLink.</summary>
    Indirect,
    /// <summary>A wireless display, for example Miracast.</summary>
    Wireless,
    /// <summary>A virtual display without a monitor behind it.</summary>
    Virtual,
    /// <summary>Any other output, or one the operating system does not name.</summary>
    Other,
}

/// <summary>An active monitor (#133). <see cref="Id"/> stays the same while it is connected; <see cref="Name"/> is null when the monitor gives none.</summary>
public sealed record DisplayMonitor(string Id, string? Name, MonitorOutput Output)
{
    /// <summary>The active external displays among <paramref name="monitors"/>; null when the monitors are unknown.</summary>
    public static int? ExternalCount(IReadOnlyList<DisplayMonitor>? monitors) => monitors?.Count(monitor => monitor.Output.IsExternal());

    /// <summary>"DELL U2723QE (HDMI)", in English, for the log.</summary>
    public override string ToString() => $"{Name ?? "unnamed"} ({Output.Text()})";
}

public static class MonitorOutputs
{
    /// <summary>
    /// A display on one of the computer's own outputs outside the computer: it counts toward displays.max_external. The
    /// built-in panel, DisplayLink and other indirect, wireless and virtual displays do not. "Other" counts: leaving it
    /// out could hide a display and so hide an exceeded limit.
    /// </summary>
    public static bool IsExternal(this MonitorOutput output) =>
        output is not (MonitorOutput.Internal or MonitorOutput.Indirect or MonitorOutput.Wireless or MonitorOutput.Virtual);

    /// <summary>The output type in English, for the log.</summary>
    public static string Text(this MonitorOutput output) => output switch
    {
        MonitorOutput.Internal => "built-in",
        MonitorOutput.Hdmi => "HDMI",
        MonitorOutput.DisplayPort => "DisplayPort",
        MonitorOutput.UsbTunnel => "DisplayPort over USB4/Thunderbolt",
        MonitorOutput.Dvi => "DVI",
        MonitorOutput.Vga => "VGA",
        MonitorOutput.Indirect => "indirect, for example DisplayLink",
        MonitorOutput.Wireless => "wireless",
        MonitorOutput.Virtual => "virtual",
        _ => "other",
    };
}
