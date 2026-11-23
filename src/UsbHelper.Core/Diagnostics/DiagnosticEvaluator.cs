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
    /// <param name="thisComputer">
    /// The connected computer's profile (#368), judged with its measured values into <see cref="SystemsReport.ThisComputer"/>;
    /// the ticked <paramref name="profiles"/> are judged with database values only.
    /// </param>
    public static SystemsReport EvaluateAll(
        IReadOnlyList<UsbNode> tree, IReadOnlyList<ResolvedProfile> profiles, IReadOnlyList<MissingDevice> missing, int? externalDisplays = null,
        IReadOnlyCollection<string>? reconnected = null, IReadOnlyDictionary<string, CableMark>? marks = null, IReadOnlyList<UsbNode>? wholeTree = null,
        IReadOnlyList<DisplayMonitor>? monitors = null, Guide? guide = null, ResolvedProfile? thisComputer = null)
    {
        // #368: the connected computer is judged with what it measures; each ticked profile with database values only.
        var here = thisComputer == null ? null
            : new ProfileResult(thisComputer, Evaluate(tree, thisComputer, missing, externalDisplays, reconnected, marks, wholeTree, monitors, guide, measured: true));
        if (profiles.Count == 0)
        {
            var unknown = Evaluate(tree, null, missing, externalDisplays, reconnected, marks, wholeTree, monitors, guide);
            return new SystemsReport(unknown.Overall, unknown.Connections, [], unknown) { ThisComputer = here };
        }
        var results = profiles.Select(profile => new ProfileResult(profile,
            Evaluate(tree, profile, missing, externalDisplays, reconnected, marks, wholeTree, monitors, guide, measured: false))).ToList();
        var connections = results
            .SelectMany(result => result.Report.Connections)
            .GroupBy(line => line.Key, line => line.Value)
            .ToDictionary(group => group.Key, group => group.Max());
        return new SystemsReport(results.Max(result => result.Report.Overall), connections, results, results[0].Report) { ThisComputer = here };
    }

    /// <param name="profile">The resolved computer or platform profile, or null when none is selected.</param>
    /// <param name="missing">From <see cref="SessionHistory.Missing"/>.</param>
    /// <param name="externalDisplays">Active external displays as the operating system reports them; null when unknown.</param>
    /// <param name="reconnected">Ids from <see cref="SessionHistory.Reconnected"/>.</param>
    /// <param name="marks">
    /// The user's marks for active cables and extenders per port id: each adds its hubs and tiers
    /// (<see cref="TopologyCounts"/>).
    /// </param>
    /// <param name="wholeTree">
    /// All controllers, when <paramref name="tree"/> is only some of them: the hub count is the whole computer's (owner
    /// decision on #73). Everything else is judged on <paramref name="tree"/>. Null: <paramref name="tree"/> is all.
    /// </param>
    /// <param name="monitors">The active monitors, for the MST rule (#144); their statuses join the connections by monitor id.</param>
    /// <param name="measured">True for the connected computer (#368): the hubs it reports inside itself count where a measure accepts <c>detected</c>.</param>
    /// <param name="guide">
    /// The guide whose limits and recommendations apply (#285); null for none: every limit is in the guides, so then
    /// nothing is judged by a limit.
    /// </param>
    public static DiagnosticReport Evaluate(
        IReadOnlyList<UsbNode> tree, ResolvedProfile? profile, IReadOnlyList<MissingDevice> missing, int? externalDisplays = null,
        IReadOnlyCollection<string>? reconnected = null, IReadOnlyDictionary<string, CableMark>? marks = null, IReadOnlyList<UsbNode>? wholeTree = null,
        IReadOnlyList<DisplayMonitor>? monitors = null, Guide? guide = null, bool measured = true)
    {
        var findings = new List<Finding>();
        var computer = profile?.Find("name")?.Scalar as string ?? profile?.Find("model")?.Scalar as string ?? "This computer";
        if (profile == null)
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer, "No computer profile is selected, so no limits are known."));
        }

        // A v4 guide judges by its rules (#327); one that needs a newer app judges nothing.
        var rulesGuide = guide is { Format: >= 4 } ? guide : null;
        if (rulesGuide is { NeedsNewerApp: true })
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer,
                $"{rulesGuide.Name} needs a newer USB-Helper ({string.Join(", ", rulesGuide.Unsupported)}); nothing is judged by it."));
            rulesGuide = null;
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
        var depths = TopologyCounts.Depths(tree, marks);
        var computerTree = wholeTree ?? tree;
        // Every limit comes from the guide and is checked on its own (#285): hubs, tiers and chain on each bus, each
        // recommended and max, and tiers and chain for cameras and audio devices (#297). Only what the guide counts is
        // judged (#310). No guide: no limits. A hub or device on a port whose bus is not reported is judged by the lowest
        // of the values stated for USB 2 and USB 3.
        var notes = new Dictionary<string, string?>();
        LimitCheck Judge(GuideLimit limit, string path, int actual, bool open)
        {
            var field = GuideLimits.Field(path);
            notes[field] = limit.Note;
            return Check(field, limit, actual, open);
        }
        static string? BusKey(UsbBus? bus) => bus switch { UsbBus.Usb2 => "usb2", UsbBus.Usb3 => "usb3", _ => null };

        var perBus = TopologyCounts.PerBus(computerTree, marks);
        var nodeBuses = TopologyCounts.Buses(computerTree);
        // Said in the log only when it matters: some limit differs between USB 2 and USB 3.
        string[] busPaths = ["hubs.max", "hubs.recommended", "tiers.max", "tiers.recommended"];
        if (perBus.BusUnknown && busPaths.Any(path => GuideLimits.Of(profile, guide, $"usb2.{path}").Value != GuideLimits.Of(profile, guide, $"usb3.{path}").Value))
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer,
                "The operating system does not say which bus some ports are on, so what is on them is judged by the lowest of the USB 2 and USB 3 values."));
        }

        // Hubs per bus: the two halves of a USB 3 hub are one hub on each bus, not two hubs (#195). Without any known
        // bus, all hubs are counted together. A hub on a port whose bus is unknown is in neither bus; the total of all
        // hubs is an upper bound for each bus, so a count is open only when that total is above its limit. The hubs a
        // cable mark adds count on its port's bus, like the hubs on that port.
        var totalHubs = TopologyCounts.HubCount(computerTree, marks);
        var computerOpen = TopologyCounts.Unreadable(computerTree).Any();
        List<(UsbBus? Bus, LimitCheck Check)> HubChecks(string path)
        {
            var checks = new List<(UsbBus? Bus, LimitCheck Check)>();
            if (!perBus.AnyBusKnown)
            {
                checks.Add((null, Judge(GuideLimits.OnBus(profile, guide, null, path), path, totalHubs, computerOpen)));
                return checks;
            }
            foreach (var (bus, counts) in new[] { (UsbBus.Usb2, perBus.Usb2), (UsbBus.Usb3, perBus.Usb3) })
            {
                var limit = GuideLimits.OnBus(profile, guide, BusKey(bus), path);
                checks.Add((bus, Judge(limit, path, counts.Hubs, computerOpen || (perBus.BusUnknown && totalHubs > limit.Value))));
            }
            return checks;
        }
        var hubsMax = HubChecks("hubs.max");
        var hubsRecommended = HubChecks("hubs.recommended");
        if (perBus.AnyBusKnown && perBus.BusUnknown && !computerOpen
            && hubsMax.Concat(hubsRecommended).Any(each => each.Check.State == LimitState.Unknown && each.Check.Limit != null))
        {
            findings.Add(new Finding(FactKind.Unknown, ComputerId, computer,
                "The operating system does not say which bus some ports are on, so the hubs on them are in neither bus's count; the counts per bus are at least what is shown."));
        }
        // The check shown for all buses: an exceeded one, else the one with the lowest limit.
        static LimitCheck Summary(IReadOnlyList<LimitCheck> checks) =>
            checks.FirstOrDefault(check => check.State == LimitState.Exceeded)
            ?? checks.Where(check => check.Limit != null).MinBy(check => check.Limit) ?? checks.First();
        var hubs = Summary([.. hubsMax.Select(each => each.Check)]);

        // A device's tier as the USB specification counts it (#285) and its chain (#310), each a sum of path elements
        // (#326). Tier: root_hub 1, computer_hub, the detected hubs, the marked hubs and cable lengths, and the device
        // itself. Chain: the hubs daisy-chained in front of it, computer_hub included; root hubs do not count. An
        // unknown computer_hub counts as none, so the tier and the chain are then at least what is counted.
        var paths = TopologyCounts.Paths(tree, marks);
        var levelsValue = profile?.Find("built_in_hub_levels");
        var computerHub = ComputerHubPart(profile, levelsValue);
        var tiersOpen = incomplete || (profile != null && levelsValue?.Scalar is not long);
        IReadOnlyList<CountPart> PartsOf(string measure, string id)
        {
            var path = paths.GetValueOrDefault(id) ?? new DevicePath([], []);
            return measure == Guide.Chain
                ? [computerHub, .. path.Parts(tiers: false)]
                : [new CountPart(PathElements.RootHub, 1, "detected"), computerHub, .. path.Parts(tiers: true), new CountPart(PathElements.Device, 1, "detected")];
        }
        int Sum(IReadOnlyList<CountPart> parts) => parts.Sum(part => part.Count ?? 0);
        int Measured(string path, string id) => Sum(PartsOf(GuideLimits.Measure(path) == Guide.Chain ? Guide.Chain : Guide.Tiers, id));
        var cameraAudioIds = AllNodes(tree).Where(IsCameraOrAudio).Select(node => node.Id).ToHashSet();
        // The deepest tier or longest chain on a bus (null: every bus), or of cameras and audio devices.
        int Deepest(string path, UsbBus? bus, bool cameraAudio) => depths
            .Where(depth => (!cameraAudio || cameraAudioIds.Contains(depth.Key)) && (bus == null || nodeBuses.GetValueOrDefault(depth.Key) == bus))
            .Select(depth => Measured(path, depth.Key)).DefaultIfEmpty(0).Max();
        LimitCheck Depth(string path)
        {
            if (!perBus.AnyBusKnown)
            {
                return Judge(GuideLimits.OnBus(profile, guide, null, path), path, Deepest(path, null, false), tiersOpen);
            }
            return Summary([.. new[] { UsbBus.Usb2, UsbBus.Usb3 }.Select(bus =>
                Judge(GuideLimits.OnBus(profile, guide, BusKey(bus), path), path, Deepest(path, bus, false), tiersOpen || perBus.BusUnknown))]);
        }
        var tiers = Depth("tiers.max");
        var tiersRecommended = Depth("tiers.recommended");
        var chain = Depth("chain.max");
        var chainRecommended = Depth("chain.recommended");
        LimitCheck Camera(string path) => Judge(GuideLimits.Of(profile, guide, path), path, Deepest(path, null, true), tiersOpen);
        LimitCheck[] all = [hubs, Summary([.. hubsRecommended.Select(each => each.Check)]), tiers, tiersRecommended,
            Camera("camera_audio.tiers.max"), Camera("camera_audio.tiers.recommended"), chain, chainRecommended,
            Camera("camera_audio.chain.max"), Camera("camera_audio.chain.recommended")];
        // What is counted (#310); without a guide nothing is known, and the log names hubs and tiers as unknown.
        var counted = guide?.Count ?? [Guide.Tiers, Guide.Hubs];
        // A v4 guide has no limits of this kind: its rules are judged per device below.
        var countedLimits = guide is { Format: >= 4 } ? [] : counted;
        // The maximums of what is counted are always named in the log; a recommendation or camera limit only when known.
        (LimitCheck Check, string Measure)[] maximums = [(hubs, Guide.Hubs), (tiers, Guide.Tiers), (chain, Guide.Chain)];
        foreach (var limit in all.Where(limit => limit.Limit != null || maximums.Any(each => each.Check == limit && countedLimits.Contains(each.Measure))))
        {
            findings.Add(LimitFinding(limit, computer));
        }
        string Note(LimitCheck limit) => notes.GetValueOrDefault(limit.Field) is { } note ? $" {note}" : "";
        // #298: the hubs of all buses as the app shows them, "Hubs: 3 (2+1)", after a bus's own count.
        var allHubs = $"Hubs: {(computerOpen ? "at least " : "")}{perBus.HubsText}: USB 2, USB 3.{(perBus.RoutersText is { } routers ? $" {routers}." : "")}";
        // The parts of a hub count (#326): the detected hubs and the hubs the user's marks add, on the bus (null: all buses).
        var countedNodes = AllNodes(computerTree).Where(node => nodeBuses.ContainsKey(node.Id)).ToList();
        IReadOnlyList<CountPart> HubParts(UsbBus? bus)
        {
            bool OnBus(UsbNode node) => bus == null || nodeBuses[node.Id] == bus;
            var detected = countedNodes.Where(node => node.Kind == UsbNodeKind.Hub && OnBus(node)).Select(node => node.Name).ToList();
            List<CountPart> parts = [new CountPart(PathElements.Hub, detected.Count, $"detected: {string.Join(", ", detected)}")];
            foreach (var (hub, port) in Ports(computerTree))
            {
                if (marks?.GetValueOrDefault(port.Id) is { Hubs: > 0 } mark && port.Children.Any(node => nodeBuses.ContainsKey(node.Id) && OnBus(node)))
                {
                    parts.Add(new CountPart(PathElements.MarkedHub, mark.Hubs, $"your mark on {port.Name} of {hub.Name}"));
                }
            }
            return parts;
        }
        void HubFindings(List<(UsbBus? Bus, LimitCheck Check)> checks, string recommended)
        {
            foreach (var (bus, check) in checks.Where(each => each.Check.State == LimitState.Exceeded))
            {
                findings.Add(Risk(ComputerId, computer, unverified => bus == null
                    ? $"{check.Actual} external hubs are connected; {recommended}{check.Field} is {check.Limit}{unverified(check)}.{Note(check)}"
                    : $"{check.Actual} hubs are on the {(bus == UsbBus.Usb2 ? "USB 2" : "USB 3")} bus; {recommended}{check.Field} is {check.Limit}{unverified(check)}.{Note(check)} {allHubs}") with
                { CountParts = HubParts(bus) });
            }
        }
        var hubsExceeded = hubsMax.Any(each => each.Check.State == LimitState.Exceeded);
        if (hubsExceeded)
        {
            HubFindings(hubsMax, "");
        }
        else
        {
            HubFindings(hubsRecommended, "the recommended ");
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
        var anyLimitKnown = all.Any(limit => limit.Limit != null) || (rulesGuide != null && profile != null && RuleJudge.AnyLimit(rulesGuide, profile));
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
                var bus = nodeBuses.GetValueOrDefault(node.Id);
                if (depth != null)
                {
                    status = Max(status, JudgeDepth(node, BusKey(bus), IsCameraOrAudio(node)));
                    if (rulesGuide != null && profile != null)
                    {
                        status = Max(status, RuleJudge.Judge(rulesGuide, profile, node, bus, paths[node.Id], findings, measured));
                    }
                }
                // Only hubs on a bus over a hub limit; a hub whose bus is unknown may be on any of them.
                bool OnBusOver(List<(UsbBus? Bus, LimitCheck Check)> checks) => checks.Any(each =>
                    each.Check.State == LimitState.Exceeded && (each.Bus == null || bus == null || each.Bus == bus));
                if (countedHub && OnBusOver(hubsMax))
                {
                    status = ConnectionStatus.Purple; // This hub counts toward a bus over the hub limit.
                }
                else if (countedHub && !hubsExceeded && OnBusOver(hubsRecommended))
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
        // finding with its guide's exceeded_note. Tiers and chain are each judged on the node's bus.
        ConnectionStatus JudgeDepth(UsbNode node, string? bus, bool cameraAudio)
        {
            var checks = new List<(LimitCheck Max, LimitCheck Recommended, bool Camera, string Text, IReadOnlyList<CountPart> Parts)>();
            foreach (var measure in new[] { Guide.Tiers, Guide.Chain })
            {
                var parts = PartsOf(measure, node.Id);
                var actual = Sum(parts);
                var text = measure == Guide.Chain ? $"Hub chain {actual}" : $"Tier depth {actual}";
                LimitCheck On(string path) => Judge(GuideLimits.OnBus(profile, guide, bus, path), path, actual, false);
                LimitCheck OnCamera(string path) => Judge(GuideLimits.Of(profile, guide, path), path, actual, false);
                checks.Add((On($"{measure}.max"), On($"{measure}.recommended"), false, text, parts));
                if (cameraAudio)
                {
                    checks.Add((OnCamera($"camera_audio.{measure}.max"), OnCamera($"camera_audio.{measure}.recommended"), true, text, parts));
                }
            }
            static string For(bool camera) => camera ? " for cameras and audio devices" : "";
            var overMax = checks.Where(check => check.Max.State == LimitState.Exceeded).ToList();
            foreach (var (max, _, camera, text, parts) in overMax)
            {
                findings.Add(Risk(node.Id, node.Name, unverified =>
                    $"{text} exceeds {max.Field} {max.Limit}{unverified(max)}{For(camera)}.{Note(max)}") with
                { CountParts = parts });
            }
            if (overMax.Count > 0)
            {
                return ConnectionStatus.Purple;
            }
            var overRecommended = checks.Where(check => check.Recommended.State == LimitState.Exceeded).ToList();
            foreach (var (max, recommended, camera, text, parts) in overRecommended)
            {
                findings.Add(Risk(node.Id, node.Name, unverified =>
                    $"{text} exceeds the recommended {recommended.Field} {recommended.Limit}{unverified(recommended)}{For(camera)}; "
                    + $"{max.Field} is {(max.Limit == null ? "unknown" : $"{max.Limit}{unverified(max)}")}.{Note(recommended)}") with
                { CountParts = parts });
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
        var noMst = new HashSet<string>();
        foreach (var (monitor, status) in DisplayEvaluator.Mst(profile, monitors ?? [], computer, findings, guide))
        {
            connections[monitor] = status;
            if (findings.Any(finding => finding.SubjectId == monitor && finding.Brief.StartsWith("⚠ No MST", StringComparison.Ordinal)))
            {
                noMst.Add(monitor);
            }
        }

        var overall = connections.Values
            .Concat(new[] { hubs, tiers, chain }.Select(Contribution))
            .Append(devicesOver ? ConnectionStatus.Purple : ConnectionStatus.Green)
            .Append(anyLimitKnown ? ConnectionStatus.Green : ConnectionStatus.Grey)
            .Append(AllNodes(tree).Any(node => node.Problem != null) ? ConnectionStatus.Purple : ConnectionStatus.Green)
            .Append(displayContribution)
            .Max();
        return new DiagnosticReport(overall, connections, hubs, tiers, displays, disconnected, findings)
        {
            Depths = depths,
            NoMstMonitors = noMst,
            HubsBus = hubsMax.First(each => ReferenceEquals(each.Check, hubs)).Bus,
            Chain = chain,
            Counted = counted,
        };
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

    // The computer's own hubs the operating system does not show (#326), from the profile; unknown when it has no number.
    private static CountPart ComputerHubPart(ResolvedProfile? profile, ProfileValue? value)
    {
        var origin = value == null ? "not in the database"
            : value.File.StartsWith("platforms/", StringComparison.Ordinal) && profile!.Chip.Length > 0 ? $"{value.File} {profile.Chip}"
            : $"{value.File} built_in_hub_levels";
        return new CountPart(PathElements.ComputerHub, value?.Scalar is long levels ? (int)levels : null, origin);
    }

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
