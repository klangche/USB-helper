using System.Globalization;
using Avalonia.Collections;
using UsbHelper.App.Resources;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.ViewModels;

/// <summary>
/// A limit marking on a row (#371): "Ochno: max 5 hubs · 2 more fit behind". Orange for a recommendation, purple for a
/// maximum, as the statuses; a solid outline for a known count, dashed for an "at least" count, whose hover says why.
/// </summary>
public sealed record LimitBadge(string Text, string Tip, string Color, bool Dashed)
{
    /// <summary>The outline's dashes: none for a known count.</summary>
    public AvaloniaList<double>? DashArray => Dashed ? [3, 2] : null;

    /// <param name="withBus">Name the bus of the row's half: the two halves of a hub shown as one have different markings.</param>
    public static LimitBadge Of(LimitMark mark, bool withBus = false)
    {
        var level = mark.Level == LimitLevel.Max ? Strings.LimitMarkMax : Strings.LimitMarkRecommended;
        var unit = mark.Unit switch
        {
            LimitUnit.Hubs => Strings.LimitUnitHubs,
            LimitUnit.Tiers => Strings.LimitUnitTiers,
            LimitUnit.Routers => Strings.LimitUnitRouters,
            _ => "",
        };
        var scope = string.Join(", ", new[]
        {
            mark.Devices.Count > 0 ? string.Join(" + ", mark.Devices) : null,
            BusText(mark.Bus switch { "usb2" => UsbBus.Usb2, "usb3" => UsbBus.Usb3, _ => withBus ? mark.NodeBus : null }),
        }.OfType<string>());
        var label = string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkLabel, mark.Guide, mark.Measure, level, mark.Limit, unit).TrimEnd()
            + (scope.Length > 0 ? $" ({scope})" : "");
        var place = mark.Place switch
        {
            LimitPlace.Reached => Strings.LimitMarkReached,
            LimitPlace.Passed => string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkPassed, mark.Count),
            LimitPlace.RoomInFront => string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkRoomInFront, mark.Room),
            _ => string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkRoom, mark.Room),
        };
        var text = $"{label} · {place}";
        var tip = mark.AtLeast
            ? string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkAtLeast, text, string.Join(Environment.NewLine, mark.Unknown))
            : text;
        return new LimitBadge(text, tip, StatusColors.Hex(mark.Level == LimitLevel.Max ? ConnectionStatus.Purple : ConnectionStatus.Orange), mark.AtLeast);
    }

    private static string? BusText(UsbBus? bus) => bus switch { UsbBus.Usb2 => Strings.GuideLineUsb2, UsbBus.Usb3 => Strings.GuideLineUsb3, _ => null };

    /// <summary>
    /// The badges of a row from the markings of its hub and, for a hub shown with its other half, of that half (#370): each
    /// once, and with the bus of its half where the halves' markings of one rule differ.
    /// </summary>
    public static IReadOnlyList<LimitBadge> For(IEnumerable<LimitMark> marks)
    {
        var list = marks.ToList();
        bool Differs(LimitMark mark) => list
            .Where(other => other.Measure == mark.Measure && other.Level == mark.Level && other.Devices.SequenceEqual(mark.Devices) && other.Bus == mark.Bus)
            .Select(other => Of(other).Text).Distinct().Count() > 1;
        return [.. list.Select(mark => Of(mark, Differs(mark))).Distinct()];
    }
}
