using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// The diagnostic rules. Evaluates the detected tree against a platform or computer profile, at most one guide, and
/// the devices missing this session. Pure: a connection that recovers is simply evaluated again. Never reads speed or bandwidth.
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
    /// <param name="wholeTree">See <see cref="Evaluate"/>.</param>
    /// <param name="guide">See <see cref="Evaluate"/>; the same guide for every profile.</param>
    public static SystemsReport EvaluateAll(
        IReadOnlyList<UsbNode> tree, IReadOnlyList<ResolvedProfile> profiles, IReadOnlyList<MissingDevice> missing, int? externalDisplays = null,
        IReadOnlyCollection<string>? reconnected = null, IReadOnlyDictionary<string, int>? extraHops = null, IReadOnlyList<UsbNode>? wholeTree = null,
        IReadOnlyList<DisplayMonitor>? monitors = null, Guide? guide = null)
    {
        if (profiles.Count == 0)
        {
            var unknown = Evaluate(tree, null, missing, externalDisplays, reconnected, extraHops, wholeTree, monitors, guide);
            return new SystemsReport(unknown.Overall, unknown.Connections, [], unknown);
        }
        var results = profiles.Select(profile => new ProfileResult(profile, Evaluate(tree, profile, missing, externalDisplays, reconnected, extraHops, wholeTree, monitors, guide))).ToList();
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
    /// <param name="wholeTree">
    /// All controllers, when <paramref name="tree"/> is only some of them: the hub count is the whole computer's (owner
    /// decision on #73). Everything else is judged on <paramref name="tree"/>. Null: <paramref name="tree"/> is all.
    /// </param>
    /// <param name="monitors">The active monitors, for the MST rule (#144); their statuses join the connections by monitor id.</param>
    /// <param name="guide">
    /// The guide whose limits and recommendations apply (#284); null for none: then only the USB maximum applies and
    /// nothing is potentially unstable by depth.
    /// </param>
    public static DiagnosticReport Evaluate(
        IReadOnlyList<UsbNode> tree, ResolvedProfile? profile, IReadOnlyList<MissingDevice> missing, int? externalDisplays = null,
        IReadOnlyCollection<string>? reconnected = null, IReadOnlyDictionary<string, int>? extraHops = null, IReadOnlyList<UsbNode>? wholeTree = null,
        IReadOnlyList<DisplayMonitor>? monitors = null, Guide? guide = null)
    {
        var findings = new List<Finding>();
        var computer = profile?.Find("name")?.Scalar as string ?? profile?.Find("model")?.Scalar as string ?? "This computer";
        if (profile == null)
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer, "No computer profile is selected, so no limits are known."));
        }

        var unreadable = TopologyCounts.Unreadable(tree).ToList();
        foreach (var node in unreadable)
        {
            findings.Add(new Finding(FactKind.Unknown, node.Id, node.Name, TopologyCounts.IsUnidentified(node)
                ? "This device could not be identified, so it may be a hub; the hub count and depths are at least what is shown."
                : "What is connected behind it cannot be read, so the hub count and depths are at least what is shown."));
        }
        foreach (var hub in TopologyCounts.PossiblyBuiltIn(tree))
        {
            findings.Add(new Finding(FactKind.Unknown, hub.Id, hub.Name,
                "The operating system does not say whether this hub's port is built into the computer, so it is counted as an external hub."));
        }
        var incomplete = unreadable.Count > 0;
        var depths = TopologyCounts.Depths(tree, extraHops);
        var computerTree = wholeTree ?? tree;
        // Every limit comes from the guide and is checked on its own (#285): hubs, tiers and hops, each recommended and
        // max, and the same for cameras and audio devices. No guide: no limits.
        var notes = new Dictionary<string, string?>();
        LimitCheck Judge(string path, int actual, bool open)
        {
            var limit = GuideLimits.Of(profile, guide, path);
            var field = GuideLimits.Field(path);
            notes[field] = limit.Note;
            return Check(field, limit, actual, open);
        }

        // Hubs apply per bus (#195): the two halves of a USB 3 hub are one hub on each bus, not two hubs. Checked against
        // the bus with the most hubs. A hub on a port whose bus is unknown is in neither bus; the total of all hubs is an
        // upper bound for each bus, so the result is open only when that total is above the limit. Without any known bus,
        // all hubs are counted together.
        var perBus = TopologyCounts.PerBus(computerTree);
        var totalHubs = TopologyCounts.HubCount(computerTree);
        var lowestHubLimit = new[] { GuideLimits.Of(profile, guide, "hubs.max").Value, GuideLimits.Of(profile, guide, "hubs.recommended").Value }.Min();
        var busOpen = perBus.AnyBusKnown && perBus.BusUnknown && totalHubs > lowestHubLimit;
        if (busOpen)
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer,
                "The operating system does not say which bus some ports are on, so the hubs on them are in neither bus's count; the counts per bus are at least what is shown."));
        }
        var hubCount = perBus.AnyBusKnown ? Math.Max(perBus.Usb2.Hubs, perBus.Usb3.Hubs) : totalHubs;
        var hubsOpen = busOpen || TopologyCounts.Unreadable(computerTree).Any();
        var hubs = Judge("hubs.max", hubCount, hubsOpen);
        var hubsRecommended = Judge("hubs.recommended", hubCount, hubsOpen);
        // The buses over a hub limit, so only their hubs are marked; null means every counted hub (no bus known).
        List<(UsbBus Bus, BusCounts Counts)>? BusesOver(LimitCheck limit) => !perBus.AnyBusKnown ? null
            : new[] { (Bus: UsbBus.Usb2, Counts: perBus.Usb2), (Bus: UsbBus.Usb3, Counts: perBus.Usb3) }
                .Where(bus => bus.Counts.Hubs > limit.Limit).ToList();
        var busesOver = BusesOver(hubs);
        var busesOverRecommended = BusesOver(hubsRecommended);
        var hubBuses = TopologyCounts.Buses(computerTree);

        // A device's tier as the USB specification counts it (#285): the counted tier plus the chip's built-in hub
        // levels. Unknown levels count as none, so the tier is then at least what is counted.
        var levelsValue = profile?.Find("built_in_hub_levels");
        var levels = (int)(levelsValue?.Scalar as long? ?? 0);
        var tiersOpen = incomplete || (profile != null && levelsValue?.Scalar is not long);
        int TierOf(ConnectionDepth depth) => depth.Tier + levels;
        var cameraAudioIds = AllNodes(tree).Where(IsCameraOrAudio).Select(node => node.Id).ToHashSet();
        int Deepest(Func<ConnectionDepth, int> measure, bool cameraAudio) =>
            depths.Where(depth => !cameraAudio || cameraAudioIds.Contains(depth.Key)).Select(depth => measure(depth.Value)).DefaultIfEmpty(0).Max();
        var tiers = Judge("tiers.max", Deepest(TierOf, false), tiersOpen);
        var tiersRecommended = Judge("tiers.recommended", Deepest(TierOf, false), tiersOpen);
        var hops = Judge("hops.max", Deepest(depth => depth.Hops, false), incomplete);
        var hopsRecommended = Judge("hops.recommended", Deepest(depth => depth.Hops, false), incomplete);
        var cameraTiers = Judge("camera_audio.tiers.max", Deepest(TierOf, true), tiersOpen);
        var cameraTiersRecommended = Judge("camera_audio.tiers.recommended", Deepest(TierOf, true), tiersOpen);
        var cameraHops = Judge("camera_audio.hops.max", Deepest(depth => depth.Hops, true), incomplete);
        var cameraHopsRecommended = Judge("camera_audio.hops.recommended", Deepest(depth => depth.Hops, true), incomplete);
        LimitCheck[] all = [hubs, hubsRecommended, tiers, tiersRecommended, hops, hopsRecommended,
            cameraTiers, cameraTiersRecommended, cameraHops, cameraHopsRecommended];
        // The maximums are always named in the log; a recommendation or camera limit only when known.
        foreach (var limit in all.Where(limit => limit.Limit != null || limit == hubs || limit == tiers || limit == hops))
        {
            findings.Add(LimitFinding(limit, computer));
        }
        string Note(LimitCheck limit) => notes.GetValueOrDefault(limit.Field) is { } note ? $" {note}" : "";
        void HubFindings(LimitCheck limit, List<(UsbBus Bus, BusCounts Counts)>? over, string recommended)
        {
            if (over == null)
            {
                findings.Add(Risk(ComputerId, computer, unverified =>
                    $"{limit.Actual} external hubs are connected; {recommended}{limit.Field} is {limit.Limit}{unverified(limit)}.{Note(limit)}"));
            }
            foreach (var (bus, counts) in over ?? [])
            {
                findings.Add(Risk(ComputerId, computer, unverified =>
                    $"{counts.Hubs} hubs are on the {(bus == UsbBus.Usb2 ? "USB 2" : "USB 3")} bus; {recommended}{limit.Field} is {limit.Limit}{unverified(limit)}.{Note(limit)}"));
            }
        }
        if (hubs.State == LimitState.Exceeded)
        {
            HubFindings(hubs, busesOver, "");
        }
        else if (hubsRecommended.State == LimitState.Exceeded)
        {
            HubFindings(hubsRecommended, busesOverRecommended, "the recommended ");
        }

        // A 7-bit device address: more devices than usb.max_devices_per_controller on one controller is unstable.
        var devicesOver = false;
        if (profile?.Find("usb.max_devices_per_controller") is { Scalar: long maxDevices } devicesLimit)
        {
            foreach (var controller in tree)
            {
                var count = AllNodes([.. controller.Children.SelectMany(rootHub => rootHub.Children)])
                    .Count(node => node.Kind is UsbNodeKind.Device or UsbNodeKind.Hub);
                if (count > maxDevices)
                {
                    devicesOver = true;
                    findings.Add(new Finding(FactKind.PredictedRisk, controller.Id, controller.Name,
                        $"{count} devices are connected to this controller; usb.max_devices_per_controller is {maxDevices} ({devicesLimit.File})."));
                }
            }
        }

        // Owner decision in #39: stability is judged on the limits the profile knows; an unknown limit is a finding,
        // and only a profile without any limit leaves the lines unknown.
        var anyLimitKnown = all.Any(limit => limit.Limit != null);
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
                if (depth != null)
                {
                    status = Max(status, JudgeDepth(node, TierOf(depth), depth.Hops, IsCameraOrAudio(node)));
                }
                // Only hubs on a bus over the limit; a hub whose bus is unknown may be on it.
                bool OnBusOver(List<(UsbBus Bus, BusCounts Counts)>? over) =>
                    over == null || hubBuses.GetValueOrDefault(node.Id) is not { } hubBus || over.Any(bus => bus.Bus == hubBus);
                if (countedHub && hubs.State == LimitState.Exceeded && OnBusOver(busesOver))
                {
                    status = ConnectionStatus.Purple; // This hub counts toward a bus over the hub limit.
                }
                else if (countedHub && hubsRecommended.State == LimitState.Exceeded && OnBusOver(busesOverRecommended))
                {
                    status = Max(status, ConnectionStatus.Orange);
                }
                if (node.Problem != null || port.Problem != null)
                {
                    status = ConnectionStatus.Purple;
                }
                // Owner decision in #54: a connection that dropped and came back is unstable, however short the drop.
                if (reconnected?.Contains(node.Id) == true)
                {
                    status = ConnectionStatus.Purple;
                    findings.Add(new Finding(FactKind.DetectedFact, node.Id, node.Name, "Disconnected and reconnected during this session.") { IsProblem = true });
                }
                connections[node.Id] = status;
            }
        }

        // Above any maximum is unstable; else above any recommendation is potentially unstable. Each exceeded limit is a
        // finding with its guide's exceeded_note.
        ConnectionStatus JudgeDepth(UsbNode node, int tier, int hop, bool cameraAudio)
        {
            var checks = new List<(LimitCheck Max, LimitCheck Recommended, string Kind, int Actual, bool Camera)>
            {
                (tiers, tiersRecommended, "Tier", tier, false),
                (hops, hopsRecommended, "Hop", hop, false),
            };
            if (cameraAudio)
            {
                checks.Add((cameraTiers, cameraTiersRecommended, "Tier", tier, true));
                checks.Add((cameraHops, cameraHopsRecommended, "Hop", hop, true));
            }
            static string For(bool camera) => camera ? " for cameras and audio devices" : "";
            var overMax = checks.Where(check => check.Actual > check.Max.Limit).ToList();
            foreach (var (max, _, kind, actual, camera) in overMax)
            {
                findings.Add(Risk(node.Id, node.Name, unverified =>
                    $"{kind} depth {actual} exceeds {max.Field} {max.Limit}{unverified(max)}{For(camera)}.{Note(max)}"));
            }
            if (overMax.Count > 0)
            {
                return ConnectionStatus.Purple;
            }
            var overRecommended = checks.Where(check => check.Actual > check.Recommended.Limit).ToList();
            foreach (var (max, recommended, kind, actual, camera) in overRecommended)
            {
                findings.Add(Risk(node.Id, node.Name, unverified =>
                    $"{kind} depth {actual} exceeds the recommended {recommended.Field} {recommended.Limit}{unverified(recommended)}{For(camera)}; "
                    + $"{max.Field} is {(max.Limit == null ? "unknown" : $"{max.Limit}{unverified(max)}")}.{Note(recommended)}"));
            }
            return overRecommended.Count > 0 ? ConnectionStatus.Orange : ConnectionStatus.Green;
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
                $"Disconnected: it was connected to {device.PortName} and is no longer present.")
            { IsProblem = true });
        }

        foreach (var (hub, port) in Ports(tree).Where(pair => pair.Port.Problem != null))
        {
            findings.Add(new Finding(FactKind.DetectedFact, port.Id, $"{port.Name} of {hub.Name}", port.Problem!) { IsProblem = true });
        }
        foreach (var node in AllNodes(tree).Where(node => node.Kind != UsbNodeKind.Port && node.Problem != null))
        {
            findings.Add(new Finding(FactKind.DetectedFact, node.Id, node.Name, node.Problem!) { IsProblem = true });
        }

        foreach (var exception in profile?.Find("exceptions")?.Items ?? [])
        {
            var verified = IsVerified(profile, exception);
            findings.Add(new Finding(verified ? FactKind.VerifiedDatabaseFact : FactKind.Unknown, ComputerId, computer,
                verified ? (string)exception.Scalar! : $"{exception.Scalar} (unverified, {exception.File})"));
        }

        var (displays, displayContribution) = DisplayEvaluator.Evaluate(tree, profile, externalDisplays, computer, findings);
        foreach (var (monitor, status) in DisplayEvaluator.Mst(profile, monitors ?? [], computer, findings, guide))
        {
            connections[monitor] = status;
        }

        var overall = connections.Values
            .Concat(new[] { hubs, tiers, hops }.Select(Contribution))
            .Append(devicesOver ? ConnectionStatus.Purple : ConnectionStatus.Green)
            .Append(anyLimitKnown ? ConnectionStatus.Green : ConnectionStatus.Grey)
            .Append(AllNodes(tree).Any(node => node.Problem != null) ? ConnectionStatus.Purple : ConnectionStatus.Green)
            .Append(displayContribution)
            .Max();
        return new DiagnosticReport(overall, connections, hubs, tiers, hops, displays, disconnected, findings) { Depths = depths, RecommendedHops = hopsRecommended };
    }

    private static LimitCheck Check(string field, GuideLimit limit, int actual, bool incomplete)
    {
        var state = limit.Value == null ? LimitState.Unknown
            : actual > limit.Value ? LimitState.Exceeded
            : incomplete ? LimitState.Unknown
            : LimitState.Within;
        return new LimitCheck(field, actual, limit.Value, limit.Verified, limit.Source, state);
    }

    // A camera, an audio device or both, as the operating system reports it (#274). Never guessed.
    private static bool IsCameraOrAudio(UsbNode node) =>
        node.DeviceKind is UsbDeviceKind.Camera or UsbDeviceKind.Audio or UsbDeviceKind.CameraAndAudio;

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

    // A risk whose text names limits: the message says which are unverified, the brief does not (owner decision in #100).
    private static Finding Risk(string subjectId, string subjectName, Func<Func<LimitCheck, string>, string> text) =>
        new(FactKind.PredictedRisk, subjectId, subjectName, text(Unverified)) { Brief = text(_ => "") };

    // A value is verified when the file that set it is itself verified (or partial), whatever the profile's own
    // status: a value inherited from an unverified platform stays unverified in a verified computer, and one
    // inherited from a partial platform stays verified in an unverified computer.
    internal static bool IsVerified(ResolvedProfile? profile, ProfileValue value) =>
        profile?.StatusOf(value.File) is string status && VerifiedStatuses.Contains(status);

    internal static IEnumerable<UsbNode> AllNodes(IReadOnlyList<UsbNode> nodes) =>
        nodes.SelectMany(node => AllNodes(node.Children).Prepend(node));

    // Every hub port with the hub it belongs to, root hubs included.
    private static IEnumerable<(UsbNode Hub, UsbNode Port)> Ports(IReadOnlyList<UsbNode> tree) =>
        tree.SelectMany(controller => controller.Children).SelectMany(PortsBelow);

    private static IEnumerable<(UsbNode Hub, UsbNode Port)> PortsBelow(UsbNode hub) =>
        hub.Children.SelectMany(port => port.Children.Where(node => node.Kind == UsbNodeKind.Hub).SelectMany(PortsBelow).Prepend((hub, port)));
}
