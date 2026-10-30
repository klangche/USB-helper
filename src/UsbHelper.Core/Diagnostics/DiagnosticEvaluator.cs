using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// The diagnostic rules. Evaluates the detected tree against a computer profile and the devices missing this
/// session. Pure: a connection that recovers is simply evaluated again. Never reads speed or bandwidth.
/// </summary>
public static class DiagnosticEvaluator
{
    /// <summary>Subject id of findings about the computer as a whole.</summary>
    public const string ComputerId = "computer";

    // A value set in a partially verified profile is verified; its unknown values are null.
    private static readonly string[] VerifiedStatuses = ["verified", "partial"];

    /// <summary>
    /// Evaluates the tree against every checked profile. Each line shows the worst result over the profiles; with no
    /// profile checked, everything is unknown (grey).
    /// </summary>
    public static SystemsReport EvaluateAll(
        IReadOnlyList<UsbNode> tree, IReadOnlyList<ResolvedProfile> profiles, IReadOnlyList<MissingDevice> missing, int? externalDisplays = null,
        IReadOnlyCollection<string>? reconnected = null, IReadOnlyDictionary<string, int>? extraHops = null)
    {
        if (profiles.Count == 0)
        {
            var unknown = Evaluate(tree, null, missing, externalDisplays, reconnected, extraHops);
            return new SystemsReport(unknown.Overall, unknown.Connections, [], unknown);
        }
        var results = profiles.Select(profile => new ProfileResult(profile, Evaluate(tree, profile, missing, externalDisplays, reconnected, extraHops))).ToList();
        var connections = results
            .SelectMany(result => result.Report.Connections)
            .GroupBy(line => line.Key, line => line.Value)
            .ToDictionary(group => group.Key, group => group.Max());
        return new SystemsReport(results.Max(result => result.Report.Overall), connections, results, results[0].Report);
    }

    /// <param name="profile">The resolved computer or platform profile, or null when none is selected.</param>
    /// <param name="missing">From <see cref="SessionHistory.Missing"/>.</param>
    /// <param name="externalDisplays">Active external displays as the operating system reports them; null when unknown.</param>
    /// <param name="reconnected">Ids from <see cref="SessionHistory.Reconnected"/>.</param>
    /// <param name="extraHops">The user's marks for active cables and extenders: extra hops per port id.</param>
    public static DiagnosticReport Evaluate(
        IReadOnlyList<UsbNode> tree, ResolvedProfile? profile, IReadOnlyList<MissingDevice> missing, int? externalDisplays = null,
        IReadOnlyCollection<string>? reconnected = null, IReadOnlyDictionary<string, int>? extraHops = null)
    {
        var findings = new List<Finding>();
        var computer = profile?.Find("model")?.Scalar as string ?? "This computer";
        if (profile == null)
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer, "No computer profile is selected, so no limits are known."));
        }

        var unreadable = TopologyCounts.Unreadable(tree).ToList();
        foreach (var node in unreadable)
        {
            findings.Add(new Finding(FactKind.Unknown, node.Id, node.Name,
                "What is connected behind it cannot be read, so the hub count and depths are at least what is shown."));
        }
        foreach (var hub in TopologyCounts.PossiblyBuiltIn(tree))
        {
            findings.Add(new Finding(FactKind.Unknown, hub.Id, hub.Name,
                "The operating system does not say whether this hub's port is built into the computer, so it is counted as an external hub."));
        }
        var incomplete = unreadable.Count > 0;
        var depths = TopologyCounts.Depths(tree, extraHops);
        var hubs = Check(profile, "limits.max_hubs", TopologyCounts.HubCount(tree), incomplete);
        var tiers = Check(profile, "limits.max_tiers", depths.Values.Select(depth => depth.Tier).DefaultIfEmpty(0).Max(), incomplete);
        var maxHops = depths.Values.Select(depth => depth.Hops).DefaultIfEmpty(0).Max();
        var hops = Check(profile, "limits.max_hops", maxHops, incomplete);
        foreach (var limit in new[] { hubs, tiers, hops })
        {
            findings.Add(LimitFinding(limit, computer));
        }
        // Optional: an unknown recommended depth is not reported and never makes anything grey.
        var recommendedHops = Check(profile, "limits.recommended_hops", maxHops, incomplete);
        if (recommendedHops.Limit != null)
        {
            findings.Add(LimitFinding(recommendedHops, computer));
        }
        if (hubs.State == LimitState.Exceeded)
        {
            findings.Add(new Finding(FactKind.PredictedRisk, ComputerId, computer,
                $"{hubs.Actual} external hubs are connected; {hubs.Field} is {hubs.Limit}{Unverified(hubs)}."));
        }

        // Owner decision in #39: stability is judged on the limits the profile knows; an unknown limit is a finding,
        // and only a profile without any limit leaves the lines unknown.
        var anyLimitKnown = new[] { hubs, tiers, hops, recommendedHops }.Any(limit => limit.Limit != null);
        var connections = new Dictionary<string, ConnectionStatus>();
        foreach (var (_, port) in Ports(tree))
        {
            foreach (var node in port.Children)
            {
                // A hub built into the computer has no depth and is not counted, so only its errors are judged.
                var depth = depths.GetValueOrDefault(node.Id);
                var countedHub = node.Kind == UsbNodeKind.Hub && depth != null;
                // A hub cannot be judged against a known hub limit when the hub count could not be read completely.
                var status = anyLimitKnown && (!countedHub || hubs.Limit == null || hubs.State != LimitState.Unknown)
                    ? ConnectionStatus.Green
                    : ConnectionStatus.Grey;
                if (depth?.Tier > tiers.Limit)
                {
                    status = ConnectionStatus.Purple;
                    findings.Add(new Finding(FactKind.PredictedRisk, node.Id, node.Name,
                        $"Tier depth {depth.Tier} exceeds {tiers.Field} {tiers.Limit}{Unverified(tiers)}."));
                }
                if (depth?.Hops > hops.Limit)
                {
                    status = ConnectionStatus.Purple;
                    findings.Add(new Finding(FactKind.PredictedRisk, node.Id, node.Name,
                        $"Hop depth {depth.Hops} exceeds {hops.Field} {hops.Limit}{Unverified(hops)}."));
                }
                else if (depth?.Hops > recommendedHops.Limit)
                {
                    status = Max(status, ConnectionStatus.Orange);
                    var maximum = hops.Limit == null ? "unknown" : $"{hops.Limit}{Unverified(hops)}";
                    findings.Add(new Finding(FactKind.PredictedRisk, node.Id, node.Name,
                        $"Hop depth {depth.Hops} exceeds the recommended {recommendedHops.Field} {recommendedHops.Limit}{Unverified(recommendedHops)}; {hops.Field} is {maximum}."));
                }
                if (countedHub && hubs.State == LimitState.Exceeded)
                {
                    status = ConnectionStatus.Purple; // Every hub counts toward the exceeded hub count.
                }
                if (node.Problem != null || port.Problem != null)
                {
                    status = ConnectionStatus.Purple;
                }
                // Owner decision in #54: a connection that dropped and came back is unstable, however short the drop.
                if (reconnected?.Contains(node.Id) == true)
                {
                    status = ConnectionStatus.Purple;
                    findings.Add(new Finding(FactKind.DetectedFact, node.Id, node.Name, "Disconnected and reconnected during this session."));
                }
                connections[node.Id] = status;
            }
        }

        // Behind something that cannot be read, a device may still be there: unknown, not disconnected.
        var disconnected = new List<MissingDevice>();
        foreach (var device in missing)
        {
            var hiddenBehind = unreadable.FirstOrDefault(node => device.AncestorIds.Contains(node.Id));
            if (hiddenBehind != null)
            {
                connections[device.Node.Id] = ConnectionStatus.Grey;
                findings.Add(new Finding(FactKind.Unknown, device.Node.Id, device.Node.Name,
                    $"It was behind {hiddenBehind.Name}, which cannot be read; it may still be connected."));
                continue;
            }
            disconnected.Add(device);
            connections[device.Node.Id] = ConnectionStatus.Red;
            findings.Add(new Finding(FactKind.DetectedFact, device.Node.Id, device.Node.Name,
                $"Disconnected: it was connected to {device.PortName} and is no longer present."));
        }

        foreach (var (hub, port) in Ports(tree).Where(pair => pair.Port.Problem != null))
        {
            findings.Add(new Finding(FactKind.DetectedFact, port.Id, $"{port.Name} of {hub.Name}", port.Problem!));
        }
        foreach (var node in AllNodes(tree).Where(node => node.Kind != UsbNodeKind.Port && node.Problem != null))
        {
            findings.Add(new Finding(FactKind.DetectedFact, node.Id, node.Name, node.Problem!));
        }

        foreach (var exception in profile?.Find("exceptions")?.Items ?? [])
        {
            var verified = IsVerified(profile, exception);
            findings.Add(new Finding(verified ? FactKind.VerifiedDatabaseFact : FactKind.Unknown, ComputerId, computer,
                verified ? (string)exception.Scalar! : $"{exception.Scalar} (unverified, {exception.File})"));
        }

        var (displays, displayContribution) = DisplayEvaluator.Evaluate(tree, profile, externalDisplays, computer, findings);

        var overall = connections.Values
            .Concat(new[] { hubs, tiers, hops }.Select(Contribution))
            .Append(anyLimitKnown ? ConnectionStatus.Green : ConnectionStatus.Grey)
            .Append(AllNodes(tree).Any(node => node.Problem != null) ? ConnectionStatus.Purple : ConnectionStatus.Green)
            .Append(displayContribution)
            .Max();
        return new DiagnosticReport(overall, connections, hubs, tiers, hops, displays, disconnected, findings);
    }

    private static LimitCheck Check(ResolvedProfile? profile, string field, int actual, bool incomplete)
    {
        var value = profile?.Find(field);
        var limit = value?.Scalar as long?;
        var state = limit == null ? LimitState.Unknown
            : actual > limit ? LimitState.Exceeded
            : incomplete ? LimitState.Unknown
            : LimitState.Within;
        return new LimitCheck(field, actual, limit, limit != null && IsVerified(profile, value!), value?.File, state);
    }

    private static Finding LimitFinding(LimitCheck limit, string computer)
    {
        var source = limit.Source == null ? "" : $" ({limit.Source})";
        return limit.Limit == null ? new Finding(FactKind.Unknown, ComputerId, computer, $"{limit.Field} is unknown{source}.")
            : !limit.Verified ? new Finding(FactKind.Unknown, ComputerId, computer,
                $"{limit.Field} = {limit.Limit} comes from an unverified profile{source}; it is not confirmed.")
            : new Finding(FactKind.VerifiedDatabaseFact, ComputerId, computer, $"{limit.Field} = {limit.Limit}{source}.");
    }

    // An unknown limit adds nothing; a known one that cannot be checked completely (a hub could not be read) is unknown.
    private static ConnectionStatus Contribution(LimitCheck limit) =>
        limit.Limit == null ? ConnectionStatus.Green
        : limit.State == LimitState.Exceeded ? ConnectionStatus.Purple
        : limit.State == LimitState.Unknown ? ConnectionStatus.Grey
        : ConnectionStatus.Green;

    private static ConnectionStatus Max(ConnectionStatus a, ConnectionStatus b) => a > b ? a : b;

    private static string Unverified(LimitCheck limit) => limit.Verified ? "" : " (unverified limit)";

    // A value is verified when the file that set it is itself verified (or partial). A value inherited from an
    // unverified platform profile stays unverified even if the computer profile is verified.
    internal static bool IsVerified(ResolvedProfile? profile, ProfileValue value) =>
        profile?.Find("verification.status") is { Scalar: string status } statusValue
        && VerifiedStatuses.Contains(status)
        && statusValue.File == value.File;

    internal static IEnumerable<UsbNode> AllNodes(IReadOnlyList<UsbNode> nodes) =>
        nodes.SelectMany(node => AllNodes(node.Children).Prepend(node));

    // Every hub port with the hub it belongs to, root hubs included.
    private static IEnumerable<(UsbNode Hub, UsbNode Port)> Ports(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(controller => controller.Children).SelectMany(PortsBelow);

    private static IEnumerable<(UsbNode Hub, UsbNode Port)> PortsBelow(UsbNode hub) =>
        hub.Children.SelectMany(port => port.Children.Where(node => node.Kind == UsbNodeKind.Hub).SelectMany(PortsBelow).Prepend((hub, port)));
}
