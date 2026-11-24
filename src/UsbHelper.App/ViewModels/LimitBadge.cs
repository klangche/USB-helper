using System.Globalization;
using Avalonia.Collections;
using UsbHelper.App.Resources;
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

    public static LimitBadge Of(LimitMark mark)
    {
        var level = mark.Level == LimitLevel.Max ? Strings.LimitMarkMax : Strings.LimitMarkRecommended;
        var scope = string.Join(", ", new[] { mark.Devices.Count > 0 ? string.Join(" + ", mark.Devices) : null, BusText(mark.Bus) }.OfType<string>());
        var label = string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkLabel, mark.Guide, level, mark.Limit, mark.Unit)
            + (scope.Length > 0 ? $" ({scope})" : "");
        var place = mark.Place switch
        {
            LimitPlace.Reached => Strings.LimitMarkReached,
            LimitPlace.Passed => string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkPassed, mark.Count),
            _ => string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkRoom, mark.Room),
        };
        var text = $"{label} · {place}";
        var tip = mark.AtLeast
            ? string.Format(CultureInfo.CurrentCulture, Strings.LimitMarkAtLeast, text, string.Join(Environment.NewLine, mark.Unknown))
            : text;
        return new LimitBadge(text, tip, mark.Level == LimitLevel.Max ? "#8E24AA" : "#EF6C00", mark.AtLeast);
    }

    private static string? BusText(string? bus) => bus switch { "usb2" => "USB 2", "usb3" => "USB 3", _ => null };
}
