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

    // A value in a partially verified profile is verified; its unknown values are null.
    private static readonly string[] VerifiedStatuses = ["verified", "partial"];

    /// <param name="profile">The resolved computer profile, or null when none is selected.</param>
    /// <param name="missing">From <see cref="SessionHistory.Missing"/>.</param>
    public static DiagnosticReport Evaluate(IReadOnlyList<UsbNode> tree, ResolvedProfile? profile, IReadOnlyList<MissingDevice> missing)
    {
        var findings = new List<Finding>();
        var computer = profile?.Find("model")?.Scalar as string ?? "This computer";
        if (profile == null)
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer, "No computer profile is selected, so no limits are known."));
        }

        // A hub that could not be read hides what is behind it, so the counts are then minimums.
        var unreadableHubs = AllNodes(tree).Where(node => node.Kind == UsbNodeKind.Hub && node.Problem != null && node.Children.Count == 0).ToList();
        foreach (var hub in unreadableHubs)
        {
            findings.Add(new Finding(FactKind.Unknown, hub.Id, hub.Name,
                "Devices behind this hub cannot be read, so the hub count and depths are at least what is shown."));
        }
        var incomplete = unreadableHubs.Count > 0;
        var depths = TopologyCounts.Depths(tree);
        var hubs = Check(profile, "limits.max_hubs", TopologyCounts.HubCount(tree), incomplete);
        var tiers = Check(profile, "limits.max_tiers", depths.Values.Select(depth => depth.Tier).DefaultIfEmpty(0).Max(), incomplete);
        var hops = Check(profile, "limits.max_hops", depths.Values.Select(depth => depth.Hops).DefaultIfEmpty(0).Max(), incomplete);
        foreach (var limit in new[] { hubs, tiers, hops })
        {
            findings.Add(LimitFinding(limit, computer));
        }
        if (hubs.State == LimitState.Exceeded)
        {
            findings.Add(new Finding(FactKind.PredictedRisk, ComputerId, computer,
                $"{hubs.Actual} external hubs are connected; {hubs.Field} is {hubs.Limit}{Unverified(hubs)}."));
        }

        var limitsKnown = new[] { hubs, tiers, hops }.All(limit => limit.Limit != null && limit.Verified);
        var connections = new Dictionary<string, ConnectionStatus>();
        foreach (var (_, port) in Ports(tree))
        {
            foreach (var node in port.Children)
            {
                var depth = depths[node.Id];
                var status = limitsKnown ? ConnectionStatus.Green : ConnectionStatus.Grey;
                if (depth.Tier > tiers.Limit)
                {
                    status = ConnectionStatus.Yellow;
                    findings.Add(new Finding(FactKind.PredictedRisk, node.Id, node.Name,
                        $"Tier depth {depth.Tier} exceeds {tiers.Field} {tiers.Limit}{Unverified(tiers)}."));
                }
                if (depth.Hops > hops.Limit)
                {
                    status = ConnectionStatus.Yellow;
                    findings.Add(new Finding(FactKind.PredictedRisk, node.Id, node.Name,
                        $"Hop depth {depth.Hops} exceeds {hops.Field} {hops.Limit}{Unverified(hops)}."));
                }
                if (node.Kind == UsbNodeKind.Hub && hubs.State == LimitState.Exceeded)
                {
                    status = ConnectionStatus.Yellow; // Every hub counts toward the exceeded hub count.
                }
                if (node.Problem != null || port.Problem != null)
                {
                    status = ConnectionStatus.Purple;
                }
                connections[node.Id] = status;
            }
        }

        foreach (var device in missing)
        {
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

        var verifiedProfile = IsVerified(profile);
        foreach (var exception in profile?.Find("exceptions")?.Items ?? [])
        {
            findings.Add(new Finding(verifiedProfile ? FactKind.VerifiedDatabaseFact : FactKind.Unknown, ComputerId, computer,
                verifiedProfile ? (string)exception.Scalar! : $"{exception.Scalar} (unverified, {exception.File})"));
        }

        var overall = connections.Values
            .Concat(new[] { hubs, tiers, hops }.Select(Contribution))
            .Append(AllNodes(tree).Any(node => node.Problem != null) ? ConnectionStatus.Purple : ConnectionStatus.Green)
            .Max();
        return new DiagnosticReport(overall, connections, hubs, tiers, hops, missing, findings);
    }

    private static LimitCheck Check(ResolvedProfile? profile, string field, int actual, bool incomplete)
    {
        var value = profile?.Find(field);
        var limit = value?.Scalar as long?;
        var state = limit == null ? LimitState.Unknown
            : actual > limit ? LimitState.Exceeded
            : incomplete ? LimitState.Unknown
            : LimitState.Within;
        return new LimitCheck(field, actual, limit, limit != null && IsVerified(profile), value?.File, state);
    }

    private static Finding LimitFinding(LimitCheck limit, string computer)
    {
        var source = limit.Source == null ? "" : $" ({limit.Source})";
        return limit.Limit == null ? new Finding(FactKind.Unknown, ComputerId, computer, $"{limit.Field} is unknown{source}.")
            : !limit.Verified ? new Finding(FactKind.Unknown, ComputerId, computer,
                $"{limit.Field} = {limit.Limit} comes from an unverified profile{source}; it is not confirmed.")
            : new Finding(FactKind.VerifiedDatabaseFact, ComputerId, computer, $"{limit.Field} = {limit.Limit}{source}.");
    }

    private static ConnectionStatus Contribution(LimitCheck limit) =>
        limit.State == LimitState.Exceeded ? ConnectionStatus.Yellow
        : limit.State == LimitState.Unknown || !limit.Verified ? ConnectionStatus.Grey
        : ConnectionStatus.Green;

    private static string Unverified(LimitCheck limit) => limit.Verified ? "" : " (unverified limit)";

    private static bool IsVerified(ResolvedProfile? profile) =>
        profile?.Find("verification.status")?.Scalar is string status && VerifiedStatuses.Contains(status);

    private static IEnumerable<UsbNode> AllNodes(IReadOnlyList<UsbNode> nodes) =>
        nodes.SelectMany(node => AllNodes(node.Children).Prepend(node));

    // Every hub port with the hub it belongs to, root hubs included.
    private static IEnumerable<(UsbNode Hub, UsbNode Port)> Ports(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(controller => controller.Children).SelectMany(PortsBelow);

    private static IEnumerable<(UsbNode Hub, UsbNode Port)> PortsBelow(UsbNode hub) =>
        hub.Children.SelectMany(port => port.Children.Where(node => node.Kind == UsbNodeKind.Hub).SelectMany(PortsBelow).Prepend((hub, port)));
}
