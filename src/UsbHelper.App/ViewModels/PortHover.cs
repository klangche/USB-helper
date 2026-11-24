using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// The hover of a port (#266): a small connector drawing and what the app knows about it. What it does not know is
/// shown as not detected, never guessed.
/// </summary>
/// <param name="Lines">One line per fact, in the order shown.</param>
public sealed record PortHover(string Title, IReadOnlyList<string> Lines, bool? UsbC, bool? Usb2InUse, bool? Usb3InUse)
{
    /// <summary>The drawing's USB 2 pair: green when in use, grey when not, dashed outline only when not detected.</summary>
    public string Usb2Fill => Fill(Usb2InUse);

    /// <summary>The drawing's USB 3 lanes, as <see cref="Usb2Fill"/>.</summary>
    public string Usb3Fill => Fill(Usb3InUse);

    /// <summary>The drawing's outline: rounded for USB-C, square otherwise or when not detected.</summary>
    public double CornerRadius => UsbC == true ? 8 : 1;

    private static string Fill(bool? inUse) => inUse switch { true => "#2E7D32", false => "#669E9E9E", null => "#00000000" };

    public static PortHover Of(PortConnector connector)
    {
        string Lines(bool? inUse) => inUse switch { true => Strings.PortLinesInUse, false => Strings.PortLinesNotInUse, null => Strings.PortNotDetected };
        string Bus(UsbBus? bus) => bus switch { UsbBus.Usb2 => "USB 2", UsbBus.Usb3 => "USB 3", _ => Strings.PortNotDetected };
        var lines = new List<string>
        {
            connector.UsbC switch { true => Strings.PortConnectorUsbC, false => Strings.PortConnectorNotUsbC, null => Strings.PortConnectorUnknown },
            string.Format(CultureInfo.CurrentCulture, Strings.PortUsb2Pair, Lines(connector.Usb2InUse)),
            string.Format(CultureInfo.CurrentCulture, Strings.PortUsb3Lanes, Lines(connector.Usb3InUse)),
            connector.Partner is { } partner
                ? string.Format(CultureInfo.CurrentCulture, Strings.PortOtherHalf, partner.Name, Bus(partner.Bus))
                : Strings.PortOtherHalfUnknown,
            connector.Place switch
            {
                PortPlace.ComputerPort => Strings.PortPlaceComputer,
                PortPlace.Internal => Strings.PortPlaceInternal,
                PortPlace.NotSaid => Strings.PortPlaceNotSaid,
                _ => Strings.PortPlaceHub,
            },
            Strings.PortNeverDetected,
        };
        // #370: the controllers and root hubs are no rows of the tree any more; the hover says which the port is on.
        lines.InsertRange(lines.Count - 1, connector.Controllers.Select(each => string.Format(CultureInfo.CurrentCulture, Strings.PortOnController, each.Controller, each.RootHub, Bus(each.Bus))));
        return new PortHover(string.Format(CultureInfo.CurrentCulture, Strings.PortHoverTitle, connector.Name, Bus(connector.Bus)), lines,
            connector.UsbC, connector.Usb2InUse, connector.Usb3InUse);
    }
}
