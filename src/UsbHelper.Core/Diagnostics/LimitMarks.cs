using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Diagnostics;

/// <summary>A limit of a philosophy: its recommendation or its maximum (#371).</summary>
public enum LimitLevel
{
    Recommended,
    Max,
}

/// <summary>Where on a path a limit falls (#371).</summary>
public enum LimitPlace
{
    /// <summary>The count is exactly the limit at this row.</summary>
    Reached,

    /// <summary>The count goes over the limit at this row, the row above it was within it.</summary>
    Passed,

    /// <summary>
    /// The deepest hub of a path, or a device of a device rule, still within the limit: <see cref="LimitMark.Room"/> more
    /// fit behind it.
    /// </summary>
    Room,
}

/// <summary>
/// One marking in the tree (#371): where the selected philosophy's recommended or max of one measure is reached on a
/// path, in that philosophy's own measure. Only what the philosophy states is marked.
/// </summary>
/// <param name="Guide">The philosophy's name, such as "Ochno".</param>
/// <param name="Unit">What the measure counts, for the label: <c>hubs</c>, <c>tiers</c> or <c>routers</c>; else the measure's id.</param>
/// <param name="Count">The count at this row; for <see cref="LimitPlace.Room"/> behind it, on a device plugged into the hub.</param>
/// <param name="AtLeast">A part of the count is unknown, so the count is a minimum and the marking may come sooner.</param>
/// <param name="Unknown">For <see cref="AtLeast"/>: the unknown parts and why, in English, for the marking's hover.</param>
/// <param name="Devices">The device kinds of a device rule, such as <c>camera</c>; empty for every hub and device.</param>
public sealed record LimitMark(string Guide, string Measure, string Unit, LimitLevel Level, long Limit, LimitPlace Place, int Count, bool AtLeast,
    IReadOnlyList<string> Unknown, IReadOnlyList<string> Devices, string? Bus)
{
    /// <summary>How many more the limit allows behind this row; 0 when reached or passed.</summary>
    public long Room => Math.Max(0, Limit - Count);
}

/// <summary>
/// Where the recommended and max of a v4 philosophy are reached along each path of the tree (#371), with the counts of
/// one result: this computer, measured, or a ticked system with database values.
/// </summary>
public static class LimitMarks
{
    /// <summary>The markings by hub or device id, in the philosophy's rule order, recommended before max.</summary>
    /// <param name="measured">True for this computer, measured (#368); false for a ticked system.</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<LimitMark>> Of(IReadOnlyList<UsbNode> tree, Guide? guide, ResolvedProfile? profile,
        bool measured, IReadOnlyDictionary<string, CableMark>? marks = null)
    {
        var result = new Dictionary<string, List<LimitMark>>();
        if (guide is not { Format: >= 4 } || profile == null)
        {
            return new Dictionary<string, IReadOnlyList<LimitMark>>();
        }
        var paths = TopologyCounts.Paths(tree, marks);
        var buses = TopologyCounts.Buses(tree);

        (int Count, bool Open, IReadOnlyList<string> Unknown) Value(GuideRule rule, DevicePath path, UsbBus? bus)
        {
            var parts = RuleJudge.Parts(guide.MeasureDefinitions[rule.Measure], guide, profile, path, bus, measured);
            return (parts.Sum(part => part.Count ?? 0), parts.Any(part => part.Count == null),
                [.. parts.Where(part => part.Count == null).Select(part => $"{part.Element}: {part.Origin}")]);
        }

        void Add(string id, GuideRule rule, LimitLevel level, long limit, LimitPlace place, (int Count, bool Open, IReadOnlyList<string> Unknown) value)
        {
            if (!result.TryGetValue(id, out var list))
            {
                result[id] = list = [];
            }
            list.Add(new LimitMark(guide.Name, rule.Measure, Unit(guide.MeasureDefinitions[rule.Measure]), level, limit, place, value.Count, value.Open,
                value.Unknown, rule.Devices, rule.Bus));
        }

        // Top down, with the counted node above each one, so a limit is marked where its count first gets there.
        void Walk(UsbNode node, UsbNode? above)
        {
            var counted = paths.TryGetValue(node.Id, out var path) && node.Kind is UsbNodeKind.Hub or UsbNodeKind.Device;
            if (counted)
            {
                var bus = buses.GetValueOrDefault(node.Id);
                var deepestHub = node.Kind == UsbNodeKind.Hub && !node.Children.SelectMany(port => port.Children).Any(child => child.Kind == UsbNodeKind.Hub);
                foreach (var rule in RuleJudge.Matching(guide, profile, node, bus))
                {
                    var here = Value(rule, path!, bus);
                    var parentMatches = above != null && rule.Devices.Count == 0 && RuleJudge.Matching(guide, profile, above, buses.GetValueOrDefault(above.Id)).Contains(rule);
                    var before = parentMatches ? Value(rule, paths[above!.Id], buses.GetValueOrDefault(above.Id)).Count : (int?)null;
                    // A device plugged into this hub: the count behind it.
                    var behind = deepestHub ? Value(rule, path! with { Hubs = [.. path!.Hubs, node.Name] }, bus) : default;
                    foreach (var (level, limit) in new[] { (LimitLevel.Recommended, rule.Recommended), (LimitLevel.Max, rule.Max) })
                    {
                        if (limit is not { } value)
                        {
                            continue;
                        }
                        if (here.Count >= value && (before == null || before < value))
                        {
                            Add(node.Id, rule, level, value, here.Count == value ? LimitPlace.Reached : LimitPlace.Passed, here);
                        }
                        else if (here.Count < value && rule.Devices.Count > 0)
                        {
                            Add(node.Id, rule, level, value, LimitPlace.Room, here);
                        }
                        else if (here.Count < value && deepestHub && behind.Count < value)
                        {
                            Add(node.Id, rule, level, value, LimitPlace.Room, behind);
                        }
                    }
                }
            }
            foreach (var child in node.Children.Where(child => child.Kind == UsbNodeKind.Port).SelectMany(port => port.Children)
                .Concat(node.Children.Where(child => child.Kind != UsbNodeKind.Port)))
            {
                Walk(child, counted ? node : above);
            }
        }
        foreach (var controller in tree)
        {
            Walk(controller, null);
        }
        return result.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<LimitMark>)entry.Value);
    }

    /// <summary>What a measure counts, for its label: tiers when it counts the root hub or the device, else hubs, else routers.</summary>
    public static string Unit(GuideMeasure measure) =>
        measure.Counts.Contains(PathElements.Device) || measure.Counts.Contains(PathElements.RootHub) ? "tiers"
        : measure.Counts.Any(element => element is PathElements.Hub or PathElements.ComputerHub or PathElements.MarkedHub) ? "hubs"
        : measure.Counts.Contains(PathElements.Usb4Router) ? "routers"
        : measure.Id;
}
