using System.Globalization;
using UsbHelper.Core.Database;

namespace UsbHelper.Core.Diagnostics;

/// <summary>A limit from a guide file for one platform and chip, with where it comes from.</summary>
/// <param name="Value">The limit; null when no guide states one (<c>none</c>) or no guide is selected.</param>
/// <param name="Verified">The guide file that set it is verified or partially verified.</param>
/// <param name="Source">For example <c>PureLink: limits.tiers.recommended 4 in guides/purelink.yaml</c>; null without a guide.</param>
/// <param name="Note">The <c>exceeded_note</c> to show when the limit is exceeded; null for none.</param>
public sealed record GuideLimit(long? Value, bool Verified, string? Source, string? Note);

/// <summary>
/// Reads the limits of the selected guide (#285): the guide's chip, platform and all-platforms values, then
/// USB-Helper's the same way. Hubs and tiers are each checked on their own; nothing is worked out between them.
/// </summary>
public static class GuideLimits
{
    /// <param name="path">For example <c>usb2.tiers.max</c> or <c>camera_audio.tiers.recommended</c>.</param>
    public static GuideLimit Of(ResolvedProfile? profile, Guide? guide, string path)
    {
        if (profile == null || guide?.Limit(profile.Platform, profile.Chip, path) is not { } found)
        {
            return new GuideLimit(null, false, null, null);
        }
        var text = found.Number?.ToString(CultureInfo.InvariantCulture) ?? Guide.None;
        return new GuideLimit(found.Number, found.Guide.Verified, $"{found.Guide.Name}: {found.Path} {text} in {found.Value.File}",
            found.Guide.ExceededNote(profile.Platform, profile.Chip));
    }

    /// <summary>
    /// The name a limit has in findings and the log, whatever the bus: <c>tiers.max</c> is <c>limits.max_tiers</c>,
    /// <c>camera_audio.tiers.recommended</c> is <c>limits.camera_audio_recommended_tiers</c>.
    /// </summary>
    public static string Field(string path)
    {
        var parts = path.Split('.');
        return $"limits.{(parts.Length == 3 ? "camera_audio_" : "")}{parts[^1]}_{parts[^2]}";
    }

    /// <summary>
    /// A limit such as <c>tiers.max</c> on one bus (<c>usb2</c> or <c>usb3</c>). For a bus the operating system does not
    /// report (null), the lowest of the values stated for USB 2 and USB 3 applies (owner decision 2026-11-17, #285).
    /// </summary>
    public static GuideLimit OnBus(ResolvedProfile? profile, Guide? guide, string? bus, string path) =>
        bus != null ? Of(profile, guide, $"{bus}.{path}")
            : new[] { "usb2", "usb3" }.Select(each => Of(profile, guide, $"{each}.{path}")).Where(limit => limit.Value != null).MinBy(limit => limit.Value)
                ?? Of(profile, guide, $"usb2.{path}");
}
