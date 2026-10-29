using UsbHelper.App.Resources;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>Colour and text for each connection status; the same on every operating system.</summary>
public static class StatusColors
{
    /// <summary>For tree items that have no connection line of their own (controllers, root hubs, ports).</summary>
    public const string None = "#00000000";

    public static string Hex(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Green => "#2E7D32",
        ConnectionStatus.Grey => "#9E9E9E",
        ConnectionStatus.Yellow => "#F9A825",
        ConnectionStatus.Purple => "#8E24AA",
        ConnectionStatus.Red => "#C62828",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string Text(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Green => Strings.StatusGreen,
        ConnectionStatus.Grey => Strings.StatusGrey,
        ConnectionStatus.Yellow => Strings.StatusYellow,
        ConnectionStatus.Purple => Strings.StatusPurple,
        ConnectionStatus.Red => Strings.StatusRed,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
