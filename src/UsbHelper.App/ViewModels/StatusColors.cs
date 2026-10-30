using UsbHelper.App.Resources;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>Colour and text for each connection status; the same on every operating system.</summary>
public static class StatusColors
{
    /// <summary>For tree items that have no connection line of their own (controllers, root hubs, ports).</summary>
    public const string None = "#00000000";

    /// <summary>The thin tree lines between items, and the elbow to each port.</summary>
    public const string Trunk = "#BDBDBD";

    public static string Hex(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Green => "#2E7D32",
        ConnectionStatus.Grey => "#9E9E9E",
        ConnectionStatus.Orange => "#EF6C00",
        ConnectionStatus.Purple => "#8E24AA",
        ConnectionStatus.Red => "#C62828",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>The result word in the status field.</summary>
    public static string Word(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Green => Strings.WordStable,
        ConnectionStatus.Grey => Strings.WordUnknown,
        ConnectionStatus.Orange => Strings.WordPotentiallyUnstable,
        ConnectionStatus.Purple => Strings.WordUnstable,
        ConnectionStatus.Red => Strings.WordDisconnected,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>The explanation shown as a tool tip.</summary>
    public static string Text(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Green => Strings.StatusGreen,
        ConnectionStatus.Grey => Strings.StatusGrey,
        ConnectionStatus.Orange => Strings.StatusOrange,
        ConnectionStatus.Purple => Strings.StatusPurple,
        ConnectionStatus.Red => Strings.StatusRed,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
