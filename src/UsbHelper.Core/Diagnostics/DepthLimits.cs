using System.Globalization;
using UsbHelper.Core.Database;

namespace UsbHelper.Core.Diagnostics;

/// <summary>A limit worked out from the database files, with every number it comes from.</summary>
/// <param name="Value">Hubs in front of a device (or external hubs, for <c>max_hubs</c>); null when unknown or not set.</param>
/// <param name="Verified">Every file it comes from is verified or partially verified.</param>
/// <param name="Source">
/// The numbers and the files they come from, for example
/// <c>usb.max_tiers 7 in base.yaml; built_in_hub_levels 1 in platforms/macos.yaml</c>; null when nothing is known.
/// </param>
public sealed record DepthLimit(long? Value, bool Verified, string? Source);

/// <summary>
/// Turns tiers into hubs in front of a device (#284): tiers − 2 − the chip's (or computer's) <c>built_in_hub_levels</c>.
/// A device in the computer's own port is on tier 2 with 0 hubs in front of it. The maximum comes from
/// <c>usb.max_tiers</c> in <c>base.yaml</c>, or a guide's stricter <c>max_tiers</c>; recommendations come only from a
/// guide, never below its <c>min_recommended_hubs</c>. Without a guide only the maximum applies.
/// </summary>
public static class DepthLimits
{
    private static readonly DepthLimit Unknown = new(null, false, null);

    private static readonly string[] VerifiedStatuses = ["verified", "partial"];

    /// <summary>The most hubs in front of a device.</summary>
    public static DepthLimit MaxHops(ResolvedProfile? profile, Guide? guide)
    {
        if (profile == null)
        {
            return Unknown;
        }
        var stricter = guide?.Find("limits.max_tiers")?.Scalar is long;
        return stricter
            ? Hubs(guide!.Name, GuidePart(guide, "max_tiers"), Levels(profile), floor: null)
            : Hubs(null, ProfilePart(profile, "usb.max_tiers"), Levels(profile), floor: null);
    }

    /// <summary>The recommended most hubs in front of a device; unknown without a guide.</summary>
    public static DepthLimit RecommendedHops(ResolvedProfile? profile, Guide? guide) => Recommended(profile, guide, "recommended_tiers");

    /// <summary>
    /// The recommended most hubs in front of a camera or an audio device; unknown without a guide or when the guide sets
    /// none (then the general recommendation applies).
    /// </summary>
    public static DepthLimit CameraAudioRecommendedHops(ResolvedProfile? profile, Guide? guide) =>
        Recommended(profile, guide, "camera_audio_recommended_tiers");

    /// <summary>The most external hubs on one bus; only a guide sets it.</summary>
    public static DepthLimit MaxHubs(Guide? guide)
    {
        if (guide == null || guide.Find("limits.max_hubs")?.Scalar is not long max)
        {
            return Unknown;
        }
        var part = GuidePart(guide, "max_hubs");
        return new DepthLimit(max, part.Verified, $"{guide.Name}: {part.Text}");
    }

    private static DepthLimit Recommended(ResolvedProfile? profile, Guide? guide, string field)
    {
        if (profile == null || guide == null || guide.Find($"limits.{field}")?.Scalar is not long)
        {
            return Unknown;
        }
        var floor = guide.Find("limits.min_recommended_hubs")?.Scalar is long ? GuidePart(guide, "min_recommended_hubs") : (Part?)null;
        return Hubs(guide.Name, GuidePart(guide, field), Levels(profile), floor);
    }

    private static DepthLimit Hubs(string? guideName, Part tiers, Part levels, Part? floor)
    {
        var value = tiers.Value - 2 - levels.Value;
        if (floor is { Value: long minimum } && value < minimum)
        {
            value = minimum;
        }
        var parts = new List<Part> { tiers, levels };
        if (floor is { } shown)
        {
            parts.Add(shown);
        }
        var source = (guideName == null ? "" : $"{guideName}: ") + string.Join("; ", parts.Select(part => part.Text));
        return new DepthLimit(value, value != null && parts.All(part => part.Verified), source);
    }

    private static Part Levels(ResolvedProfile profile) => ProfilePart(profile, "built_in_hub_levels");

    private static Part ProfilePart(ResolvedProfile profile, string field)
    {
        var value = profile.Find(field);
        var number = value?.Scalar as long?;
        var file = value?.File ?? profile.Files[^1];
        return new Part($"{field} {number?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} in {file}", number,
            value != null && DiagnosticEvaluator.IsVerified(profile, value));
    }

    private static Part GuidePart(Guide guide, string field)
    {
        var number = guide.Find($"limits.{field}")?.Scalar as long?;
        return new Part($"{field} {number?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} in {guide.File}", number,
            guide.Status is string status && VerifiedStatuses.Contains(status));
    }

    private readonly record struct Part(string Text, long? Value, bool Verified);
}
