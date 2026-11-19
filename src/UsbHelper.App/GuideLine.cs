using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.Core.Database;

namespace UsbHelper.App;

/// <summary>
/// The line under a guide in the menu (#327), generated from its rules, never written by hand. Each rule is one part:
/// what it applies to, the measure, then its recommendation and maximum, for example
/// "hub chain max 5; USB 2: hub chain max 2; cameras and audio: hub chain max 3". Two rules that differ only in camera
/// and audio are one part. Every word comes from the strings file.
/// </summary>
public static class GuideLine
{
    /// <param name="platforms">The platform profiles, for the names of a rule's platforms and chips.</param>
    public static string For(Guide guide, IReadOnlyDictionary<string, ResolvedProfile>? platforms = null)
    {
        if (guide.NeedsNewerApp)
        {
            return Strings.GuideNeedsNewer;
        }
        var parts = new List<string>();
        var rules = guide.Rules.ToList();
        while (rules.Count > 0)
        {
            var rule = rules[0];
            rules.RemoveAt(0);
            var devices = rule.Devices.ToList();
            // A camera rule and an audio rule with the same numbers are one part.
            if (devices is ["camera"] or ["audio"] && rules.FirstOrDefault(other => SameExceptKind(rule, other)) is { } twin)
            {
                rules.Remove(twin);
                devices = ["camera", "audio"];
            }
            List<string> scope = [.. rule.On.Select(on => PlaceName(on, platforms))];
            if (rule.Bus != null)
            {
                scope.Add(rule.Bus == "usb2" ? Strings.GuideLineUsb2 : Strings.GuideLineUsb3);
            }
            if (devices.Count > 0)
            {
                scope.Add(Join(devices.Select(KindName).ToList()));
            }
            var text = $"{MeasureName(rule.Measure)} {Values(rule)}";
            parts.Add(scope.Count == 0 ? text : string.Format(CultureInfo.CurrentCulture, Strings.GuideLineScope, string.Join(", ", scope), text));
        }
        return string.Join(Strings.GuideLineSeparator, parts);
    }

    private static bool SameExceptKind(GuideRule rule, GuideRule other) =>
        other.Devices.Count == 1 && other.Devices[0] != rule.Devices[0] && other.Devices[0] is "camera" or "audio"
        && other.Measure == rule.Measure && other.Bus == rule.Bus && other.On.SequenceEqual(rule.On)
        && other.Recommended == rule.Recommended && other.Max == rule.Max
        && other.RecommendedNone == rule.RecommendedNone && other.MaxNone == rule.MaxNone;

    // "recommended 4, max 7", "max 5" or "no limit".
    private static string Values(GuideRule rule)
    {
        var values = new List<string>();
        if (rule.Recommended is { } recommended)
        {
            values.Add(string.Format(CultureInfo.CurrentCulture, Strings.GuideLineRecommended, recommended));
        }
        if (rule.Max is { } max)
        {
            values.Add(string.Format(CultureInfo.CurrentCulture, Strings.GuideLineMax, max));
        }
        return values.Count == 0 ? Strings.GuideLineNone : string.Join(", ", values);
    }

    private static string MeasureName(string measure) => measure switch
    {
        "chain" => Strings.GuideMeasureChain,
        "tiers" => Strings.GuideMeasureTiers,
        "room" => Strings.GuideMeasureRoom,
        _ => string.Format(CultureInfo.CurrentCulture, Strings.GuideMeasureOther, measure),
    };

    private static string KindName(string kind) => kind switch
    {
        "camera" => Strings.GuideLineCameras,
        "audio" => Strings.GuideLineAudio,
        "hid" => Strings.GuideLineHid,
        _ => kind, // Not reached: the loader accepts only the kinds above.
    };

    // "cameras and audio".
    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : string.Format(CultureInfo.CurrentCulture, Strings.GuideLineAnd, string.Join(", ", names.Take(names.Count - 1)), names[^1]);

    // "windows/amd" is "Windows AMD", by the platform profile's names.
    private static string PlaceName(string on, IReadOnlyDictionary<string, ResolvedProfile>? platforms)
    {
        var parts = on.Split('/');
        var profile = platforms?.Values.FirstOrDefault(profile => profile.Platform == parts[0] && (parts.Length == 1 || profile.Chip == parts[1]));
        return profile == null ? on : parts.Length == 1 ? profile.PlatformName : $"{profile.PlatformName} {profile.ChipName}";
    }
}
