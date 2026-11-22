using System.Globalization;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// Judges a hub or device by the rules of a v4 guide (#327). Every rule that matches it applies (its measure, bus, device
/// kinds and platform); the strictest result wins. A count with an unknown part is "at least": over a limit it is
/// exceeded, within one it is unknown. Nothing comes from another guide.
/// </summary>
internal static class RuleJudge
{
    /// <summary>The device kinds of a node as base.yaml names them (#274): camera, audio or both. Never guessed.</summary>
    public static IReadOnlyList<string> KindsOf(UsbNode node) => node.DeviceKind switch
    {
        UsbDeviceKind.Camera => ["camera"],
        UsbDeviceKind.Audio => ["audio"],
        UsbDeviceKind.CameraAndAudio => ["camera", "audio"],
        _ => [],
    };

    /// <summary>The rules of <paramref name="guide"/> that apply to <paramref name="node"/> on <paramref name="bus"/>.</summary>
    public static IEnumerable<GuideRule> Matching(Guide guide, ResolvedProfile profile, UsbNode node, UsbBus? bus) =>
        guide.Rules.Where(rule => rule.AppliesOn(profile.Platform, profile.Chip)
            && (rule.Bus == null || bus == null || rule.Bus == (bus == UsbBus.Usb2 ? "usb2" : "usb3"))
            && (rule.Devices.Count == 0 || KindsOf(node).Intersect(rule.Devices).Any()));

    /// <summary>The guide has a number for this profile, so its results are known.</summary>
    public static bool AnyLimit(Guide guide, ResolvedProfile profile) =>
        guide.Rules.Any(rule => rule.AppliesOn(profile.Platform, profile.Chip) && (rule.Max ?? rule.Recommended) != null);

    /// <summary>The parts of <paramref name="measure"/> for a node, each with its origin (#326), weighted as the measure says.</summary>
    public static IReadOnlyList<CountPart> Parts(GuideMeasure measure, Guide guide, ResolvedProfile profile, DevicePath path, UsbBus? bus)
    {
        var parts = new List<CountPart>();
        void Add(string element, int? count, string origin)
        {
            var weight = measure.Weight(element);
            if (weight > 0 && count != 0)
            {
                parts.Add(new CountPart(element, count * weight, origin));
            }
        }
        Add(PathElements.RootHub, 1, "detected");
        if (measure.Weight(PathElements.ComputerHub) > 0)
        {
            // Hubs the system reports inside the computer (#330) take the place of the database value on this path, so
            // they are never counted twice.
            var computerHub = path.ComputerHubs.Count > 0 && measure.ComputerHubFrom.Contains("detected")
                ? new CountPart(PathElements.ComputerHub, path.ComputerHubs.Count, $"detected: {string.Join(", ", path.ComputerHubs)}")
                : ComputerHub(measure, guide, profile, bus);
            parts.Add(computerHub with { Count = computerHub.Count * measure.Weight(PathElements.ComputerHub) });
        }
        foreach (var part in path.Parts(tiers: true))
        {
            Add(part.Element, part.Count, part.Origin);
        }
        // An active cable with a length (#331): the measure says how many metres make a tier on its bus.
        foreach (var (port, mark) in path.Marks.Where(each => each.Mark.Metres != null))
        {
            var cableBus = mark.Bus == UsbBus.Usb3 ? "usb3" : "usb2";
            if (measure.CableMetresPerTier.TryGetValue(cableBus, out var metresPerTier))
            {
                Add(PathElements.CableLength, mark.TiersFor(metresPerTier),
                    string.Create(CultureInfo.InvariantCulture, $"your mark on {port}: {mark}, a tier per {metresPerTier:0.#} m after the first"));
            }
        }
        if (measure.Weight(PathElements.Usb4Router) > 0)
        {
            parts.Add(new CountPart(PathElements.Usb4Router, null, "not measured yet (#267)"));
        }
        Add(PathElements.Device, 1, "detected");
        return parts;
    }

    /// <summary>
    /// The computer's own hubs on a bus: the profile's <c>computer_hubs</c> when it comes from evidence the measure
    /// accepts (a computer file or a chip's platform file), else the guide's assumption for unknown, else unknown.
    /// </summary>
    public static CountPart ComputerHub(GuideMeasure measure, Guide guide, ResolvedProfile profile, UsbBus? bus)
    {
        var value = profile.Find("computer_hubs");
        var evidence = value == null ? null : value.File.StartsWith("computers/", StringComparison.Ordinal) ? "computer" : "chip";
        if (value != null && evidence != null && measure.ComputerHubFrom.Contains(evidence))
        {
            var key = bus switch { UsbBus.Usb2 => "usb2", UsbBus.Usb3 => "usb3", _ => null };
            long? count = value.Kind == ValueKind.Mapping
                ? key != null ? value.Find(key)?.Scalar as long? : value.Fields.Values.Select(each => each.Scalar as long?).Max()
                : value.Scalar as long?;
            var origin = evidence == "chip" ? $"{value.File} {profile.Chip}" : $"{value.File} computer_hubs";
            if (count != null)
            {
                return new CountPart(PathElements.ComputerHub, (int)count, origin);
            }
        }
        return measure.ComputerHubUnknown is { } assumed
            ? new CountPart(PathElements.ComputerHub, assumed, $"{guide.File}:{measure.ComputerHubUnknownLine} when unknown")
            : new CountPart(PathElements.ComputerHub, null,
                value == null ? "not in the database"
                : evidence != null && measure.ComputerHubFrom.Contains(evidence)
                    ? $"{value.File} {(evidence == "chip" ? profile.Chip : "computer_hubs")}: unknown"
                    : $"{value.File}: not evidence {guide.Name} accepts");
    }

    /// <summary>The node's status by every matching rule, and a finding for each limit it exceeds.</summary>
    public static ConnectionStatus Judge(
        Guide guide, ResolvedProfile profile, UsbNode node, UsbBus? bus, DevicePath path, List<Finding> findings)
    {
        var status = ConnectionStatus.Green;
        var overMax = new List<Finding>();
        var overRecommended = new List<Finding>();
        foreach (var rule in Matching(guide, profile, node, bus))
        {
            var measure = guide.MeasureDefinitions[rule.Measure];
            var parts = Parts(measure, guide, profile, path, bus);
            var actual = parts.Sum(part => part.Count ?? 0);
            var open = parts.Any(part => part.Count == null);
            var atLeast = open ? "at least " : "";
            var quote = rule.Quote == null ? "" : $" \"{rule.Quote}\"";
            var source = rule.Source != null && guide.SourceDefinitions.GetValueOrDefault(rule.Source) is { } found
                ? $" ({found.Title ?? found.Id}{(found.Version == null ? "" : $", {found.Version}")})" : "";
            if (rule.Max is { } max && actual > max)
            {
                overMax.Add(new Finding(FactKind.PredictedRisk, node.Id, node.Name, string.Create(CultureInfo.InvariantCulture,
                    $"{guide.Name}: {rule.Measure} {atLeast}{actual} exceeds the maximum {max}.{quote}{source}"))
                { CountParts = parts });
            }
            else if (rule.Recommended is { } recommended && actual > recommended)
            {
                overRecommended.Add(new Finding(FactKind.PredictedRisk, node.Id, node.Name, string.Create(CultureInfo.InvariantCulture,
                    $"{guide.Name}: {rule.Measure} {atLeast}{actual} exceeds the recommended {recommended}.{quote}{source}"))
                { CountParts = parts });
            }
            else if (open && (rule.Max ?? rule.Recommended) != null)
            {
                status = Max(status, ConnectionStatus.Grey);
            }
        }
        if (overMax.Count > 0)
        {
            findings.AddRange(overMax);
            return ConnectionStatus.Purple;
        }
        findings.AddRange(overRecommended);
        return overRecommended.Count > 0 ? Max(status, ConnectionStatus.Orange) : status;
    }

    private static ConnectionStatus Max(ConnectionStatus a, ConnectionStatus b) => a > b ? a : b;
}
