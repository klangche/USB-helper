using System.Globalization;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

public class DiagnosticEvaluatorTests
{
    // Root port 1: hub with a keyboard (tier 3, 1 hop); root port 2: mouse (tier 2, 0 hops). One external hub.
    private static readonly IReadOnlyList<UsbNode> Desk = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD")), Device("MOUSE")))];

    /// <summary>A computer profile and the guide to judge it by (#284).</summary>
    private sealed record Judge(ResolvedProfile Profile, Guide? Guide);

    private const string HopsSource = "usb.max_tiers 7 in base.yaml; built_in_hub_levels 0 in platforms/test.yaml";

    // The USB maximum gives the hop limit (usb.max_tiers = hops + 2, no built-in hub levels); a guide gives the hub limit
    // and the recommendation (recommended_tiers = recommended hops + 2). No guide when it would set neither. Every file
    // has the status given.
    private static Judge Profile(string hubs = "5", string hops = "5", string status = "verified", string extra = "", string recommendedHops = "null")
    {
        using var db = new TestDatabase()
            .Write("base.yaml", $"schema_version: 2\nusb:\n  max_tiers: {Tiers(hops)}\nverification:\n  status: {status}\n")
            .Write(TestDatabase.PlatformFile, TestDatabase.Platform + $"\nverification:\n  status: {status}\n");
        db.Write("computers/c.yaml", $"""
            platform: test
            chip_family: platform
            manufacturer: Test
            model: Test laptop
            verification:
              status: {status}
            {extra}
            """);
        var withGuide = hubs != "null" || recommendedHops != "null";
        if (withGuide)
        {
            db.WriteGuide($"  max_hubs: {hubs}\n  recommended_tiers: {Tiers(recommendedHops)}", status: status);
        }
        var loaded = db.Load();
        return new Judge(loaded.Computers.Values.Single(), withGuide ? loaded.Guides["guide"] : null);
    }

    private static string Tiers(string hops) => hops == "null" ? "null" : (long.Parse(hops, CultureInfo.InvariantCulture) + 2).ToString(CultureInfo.InvariantCulture);

    // Platform profiles of one database, one per chip with the built-in hub levels given, and a guide with
    // <paramref name="guideLimits"/> (YAML lines under limits:).
    private static (IReadOnlyList<ResolvedProfile> Profiles, Guide Guide) Chips(string guideLimits, params long[] levels)
    {
        using var db = new TestDatabase()
            .Write("platforms/chips.yaml", "name: Chips\nchips:\n"
                + string.Concat(levels.Select((level, i) => $"  c{i}: {{ name: C{i}, built_in_hub_levels: {level} }}\n")))
            .WriteGuide(guideLimits);
        var loaded = db.Load();
        return ([.. levels.Select((_, i) => loaded.Platforms[$"chips-c{i}"])], loaded.Guides["guide"]);
    }

    // No external displays: these tests are about topology. Display rules are in DisplayEvaluatorTests.
    private static DiagnosticReport Evaluate(IReadOnlyList<UsbNode> tree, Judge? judge, params MissingDevice[] missing) =>
        DiagnosticEvaluator.Evaluate(tree, judge?.Profile, missing, externalDisplays: 0, guide: judge?.Guide);

    [Fact]
    public void AllLimitsKnownVerifiedAndMetIsGreen()
    {
        var report = Evaluate(Desk, Profile());

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.All(report.Connections.Values, status => Assert.Equal(ConnectionStatus.Green, status));
        Assert.Equal(["HUB", "KEYBOARD", "MOUSE"], report.Connections.Keys.Order());
        Assert.Equal(new LimitCheck("limits.max_hubs", 1, 5, true, "Guide: max_hubs 5 in guides/guide.yaml", LimitState.Within), report.Hubs);
        Assert.Equal(3, report.Tiers.Actual);
        Assert.Equal(new LimitCheck("limits.max_hops", 1, 5, true, HopsSource, LimitState.Within), report.Hops);
        Assert.Equal(2, report.Findings.Count(finding => finding.Kind == FactKind.VerifiedDatabaseFact));
    }

    [Fact]
    public void PartiallyVerifiedProfileValuesCountAsVerified()
    {
        Assert.Equal(ConnectionStatus.Green, Evaluate(Desk, Profile(status: "partial")).Overall);
    }

    // A partially verified base and platform, and a computer of it with the lines given.
    private static ResolvedProfile ComputerOfPartialPlatform(string computerLines)
    {
        using var db = new TestDatabase()
            .Write("base.yaml", TestDatabase.Base.Replace("unverified", "partial"))
            .Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\nverification:\n  status: partial\n")
            .WriteComputer("computers/c.yaml", "Test laptop", computerLines);
        return db.Load().Computers.Values.Single();
    }

    [Fact]
    public void ComputerWithoutFactsOfItsOwnUsesThePlatformsAsPartial()
    {
        // Unverified like the template and the export: the inherited facts keep the platform's status.
        var report = DiagnosticEvaluator.Evaluate(Desk, ComputerOfPartialPlatform("verification:\n  status: unverified"), [], externalDisplays: 0);

        Assert.Equal(new LimitCheck("limits.max_hops", 1, 5, true, HopsSource, LimitState.Within), report.Hops);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.VerifiedDatabaseFact
            && finding.Message == $"limits.max_hops = 5 ({HopsSource}).");
        Assert.Equal(ConnectionStatus.Green, report.Overall);
    }

    [Fact]
    public void ComputerWithoutStatusDoesNotTakeThePlatformsStatusForItsOwnFacts()
    {
        var report = DiagnosticEvaluator.Evaluate(Desk, ComputerOfPartialPlatform("built_in_hub_levels: 1"), [], externalDisplays: 0);

        Assert.Equal(4L, report.Hops.Limit);
        Assert.False(report.Hops.Verified);
        Assert.Equal("usb.max_tiers 7 in base.yaml; built_in_hub_levels 1 in computers/c.yaml", report.Hops.Source);
    }

    [Fact]
    public void NullInTheComputerHidesThePlatformsHubLevels()
    {
        var report = DiagnosticEvaluator.Evaluate(Desk, ComputerOfPartialPlatform("built_in_hub_levels: null"), [], externalDisplays: 0);

        Assert.Null(report.Hops.Limit);
        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Test laptop",
            "limits.max_hops is unknown (usb.max_tiers 7 in base.yaml; built_in_hub_levels unknown in computers/c.yaml)."), report.Findings);
    }

    [Fact]
    public void NoProfileIsGreyAndSaysSo()
    {
        var report = Evaluate(Desk, null);

        Assert.Equal(ConnectionStatus.Grey, report.Overall);
        Assert.All(report.Connections.Values, status => Assert.Equal(ConnectionStatus.Grey, status));
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.Contains("No computer profile"));
    }

    [Fact]
    public void UnknownLimitIsNamedButOnlyKnownLimitsAreJudged()
    {
        var report = Evaluate(Desk, Profile(hubs: "null"));

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.Equal(LimitState.Unknown, report.Hubs.State);
        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Test laptop", "limits.max_hubs is unknown."), report.Findings);
    }

    [Fact]
    public void FindingsNameTheComputerByItsNameWhenSet()
    {
        var report = Evaluate(Desk, Profile(hubs: "null", extra: "name: Neo"));

        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Neo", "limits.max_hubs is unknown."), report.Findings);
    }

    [Fact]
    public void UnverifiedLimitsAreJudgedAndSaidToBeUnverified()
    {
        var report = Evaluate(Desk, Profile(status: "unverified"));

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.False(report.Hops.Verified);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.StartsWith("limits.max_hops = 5 comes from an unverified profile"));
        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FactKind.VerifiedDatabaseFact);
    }

    [Fact]
    public void BuiltInHubLevelsLowerTheMaximumOnlyWhereExceeded()
    {
        // Seven tiers, five built-in hub levels: no hub in front of a device.
        var report = Evaluate(Desk, Profile(extra: "built_in_hub_levels: 5"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["HUB"]); // 0 hubs in front of it: within the limit.
        Assert.Equal(LimitState.Exceeded, report.Hops.State);
        Assert.Equal(ConnectionStatus.Purple, report.Overall);
        Assert.Contains(new Finding(FactKind.PredictedRisk, "KEYBOARD", "KEYBOARD", "Hop depth 1 exceeds limits.max_hops 0."), report.Findings);
    }

    [Fact]
    public void HopsAboveLimitIsUnstable()
    {
        var report = Evaluate(Desk, Profile(hops: "0"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Contains(report.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message == "Hop depth 1 exceeds limits.max_hops 0.");
    }

    [Fact]
    public void HopsAboveRecommendedIsPotentiallyUnstable()
    {
        var report = Evaluate(Desk, Profile(recommendedHops: "0"));

        Assert.Equal(ConnectionStatus.Orange, report.Overall);
        Assert.Equal(ConnectionStatus.Orange, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        const string Source = "Guide: recommended_tiers 2 in guides/guide.yaml; built_in_hub_levels 0 in platforms/test.yaml";
        Assert.Equal(new LimitCheck("limits.recommended_hops", 1, 0, true, Source, LimitState.Exceeded), report.RecommendedHops);
        Assert.Contains(new Finding(FactKind.PredictedRisk, "KEYBOARD", "KEYBOARD",
            "Hop depth 1 exceeds the recommended limits.recommended_hops 0; limits.max_hops is 5."), report.Findings);
        Assert.Contains(new Finding(FactKind.VerifiedDatabaseFact, DiagnosticEvaluator.ComputerId, "Test laptop",
            $"limits.recommended_hops = 0 ({Source})."), report.Findings);
    }

    [Fact]
    public void HopsAboveMaximumReportsOnlyTheMaximum()
    {
        var report = Evaluate(Desk, Profile(hops: "0", recommendedHops: "0"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Single(report.Findings, finding => finding.SubjectId == "KEYBOARD");
    }

    [Fact]
    public void UnknownRecommendedHopsIsNeitherGreyNorNamed()
    {
        var report = Evaluate(Desk, Profile());

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.DoesNotContain(report.Findings, finding => finding.Message.Contains("recommended_hops"));
    }

    [Fact]
    public void ExceededUnverifiedRecommendedHopsSaysUnverified()
    {
        var report = Evaluate(Desk, Profile(status: "unverified", recommendedHops: "0"));

        Assert.Equal(ConnectionStatus.Orange, report.Connections["KEYBOARD"]);
        Assert.Contains(report.Findings, finding => finding.Message
            == "Hop depth 1 exceeds the recommended limits.recommended_hops 0 (unverified limit); limits.max_hops is 5 (unverified limit)."
            && finding.Brief == "Hop depth 1 exceeds the recommended limits.recommended_hops 0; limits.max_hops is 5.");
    }

    [Fact]
    public void TooManyHubsMarksEveryHubUnstable()
    {
        var report = Evaluate(Desk, Profile(hubs: "0"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["HUB"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["KEYBOARD"]);
        Assert.Contains(new Finding(FactKind.PredictedRisk, DiagnosticEvaluator.ComputerId, "Test laptop",
            "1 external hubs are connected; limits.max_hubs is 0."), report.Findings);
    }

    // Owner decision in #100: the window shows the brief text, without "unverified"; the log keeps the message.
    [Fact]
    public void TooManyHubsOnAnUnverifiedLimitHasABriefWithoutUnverified()
    {
        var report = Evaluate(Desk, Profile(hubs: "0", status: "unverified"));

        var finding = Assert.Single(report.Findings, finding => finding.Kind == FactKind.PredictedRisk);
        Assert.Equal("1 external hubs are connected; limits.max_hubs is 0 (unverified limit).", finding.Message);
        Assert.Equal("1 external hubs are connected; limits.max_hubs is 0.", finding.Brief);
    }

    [Fact]
    public void BriefIsTheMessageWhenItSaysNothingAboutVerification()
    {
        var finding = new Finding(FactKind.DetectedFact, "KEYBOARD", "Keyboard", "code 43");

        Assert.Equal("code 43", finding.Brief);
    }

    [Fact]
    public void OneControllersTreeIsJudgedOnTheWholeComputersHubCount()
    {
        // Owner decision on #73: the status for one controller uses the hub count of all controllers.
        IReadOnlyList<UsbNode> computer = [.. Desk, Controller("C2", Hub("ROOT2", Hub("DOCK", Device("PHONE"))))];

        var judge = Profile(hubs: "1");
        var report = DiagnosticEvaluator.Evaluate(Desk, judge.Profile, [], externalDisplays: 0, wholeTree: computer, guide: judge.Guide);

        Assert.Equal((2, LimitState.Exceeded), (report.Hubs.Actual, report.Hubs.State));
        Assert.Equal(ConnectionStatus.Purple, report.Connections["HUB"]);
        Assert.False(report.Connections.ContainsKey("DOCK")); // Only this controller's lines.
        Assert.Equal(3, report.Tiers.Actual); // Tiers and hops are this controller's.
    }

    [Fact]
    public void AHubThatCannotBeReadOnAnotherControllerMakesTheHubCountAMinimum()
    {
        IReadOnlyList<UsbNode> computer = [.. Desk, Controller("C2", Hub("ROOT2", Hub("BROKEN") with { Problem = "code 43" }))];

        var judge = Profile(hubs: "5");
        var report = DiagnosticEvaluator.Evaluate(Desk, judge.Profile, [], externalDisplays: 0, wholeTree: computer, guide: judge.Guide);

        Assert.Equal((2, LimitState.Unknown), (report.Hubs.Actual, report.Hubs.State));
        Assert.Equal(LimitState.Within, report.Hops.State);
    }

    [Fact]
    public void BuiltInHubIsNotCountedAndNotJudgedAsHub()
    {
        // Root port 1 holds the laptop's built-in hub; its port 1 is a laptop socket with a mouse.
        IReadOnlyList<UsbNode> laptop = [Controller("C1", Hub("ROOT", Hub("INTERNAL", Device("MOUSE")).Connectable(true)).Connectable(false))];

        var report = Evaluate(laptop, Profile(hubs: "0", hops: "0"));

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.Equal(ConnectionStatus.Green, report.Connections["INTERNAL"]);
        Assert.Equal((0, 2, 0), (report.Hubs.Actual, report.Tiers.Actual, report.Hops.Actual));
        Assert.DoesNotContain(report.Findings, finding => finding.SubjectId == "INTERNAL");
    }

    [Fact]
    public void HubOnPortWindowsSaysNothingAboutIsCountedAndSaysSo()
    {
        var report = Evaluate(Desk, Profile());

        Assert.Equal(1, report.Hubs.Actual);
        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.Contains(new Finding(FactKind.Unknown, "HUB", "HUB",
            "The operating system does not say whether this hub's port is built into the computer, so it is counted as an external hub."), report.Findings);
    }

    [Fact]
    public void ExceededUnverifiedLimitIsUnstableAndSaysUnverified()
    {
        var report = Evaluate(Desk, Profile(hops: "0", status: "unverified"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Contains(report.Findings, finding => finding.Message == "Hop depth 1 exceeds limits.max_hops 0 (unverified limit)."
            && finding.Brief == "Hop depth 1 exceeds limits.max_hops 0.");
    }

    [Fact]
    public void DeviceProblemIsPurpleAndADetectedFact()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Device("MOUSE", "Windows stopped this device (code 43).")))];

        var report = Evaluate(tree, Profile());

        Assert.Equal(ConnectionStatus.Purple, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Purple, report.Overall);
        Assert.Contains(new Finding(FactKind.DetectedFact, "MOUSE", "MOUSE", "Windows stopped this device (code 43).") { IsProblem = true }, report.Findings);
    }

    [Fact]
    public void PortErrorIsPurpleAndNamesThePort()
    {
        var port = Port("ROOT", 1, Device("UNKNOWN")) with { Problem = "The device failed enumeration." };
        IReadOnlyList<UsbNode> tree = [Controller("C1", new UsbNode("ROOT", UsbNodeKind.Hub, "USB Root Hub", [port]))];

        var report = Evaluate(tree, Profile());

        Assert.Equal(ConnectionStatus.Purple, report.Connections["UNKNOWN"]);
        Assert.Contains(new Finding(FactKind.DetectedFact, "ROOT|port1", "Port 1 of USB Root Hub", "The device failed enumeration.") { IsProblem = true }, report.Findings);
    }

    [Fact]
    public void ErrorBeatsExceededLimit()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD", "code 43"))))];

        Assert.Equal(ConnectionStatus.Purple, Evaluate(tree, Profile(hops: "0")).Connections["KEYBOARD"]);
    }

    [Fact]
    public void MissingDeviceIsRedWithItsLastPort()
    {
        var mouse = Device("MOUSE");
        IReadOnlyList<UsbNode> before = [Controller("C1", Hub("ROOT", mouse))];
        IReadOnlyList<UsbNode> after = [Controller("C1", Hub("ROOT", (UsbNode?)null))];
        var history = new SessionHistory();
        history.Record(before);

        var report = Evaluate(after, Profile(), [.. history.Missing(after)]);

        Assert.Equal(ConnectionStatus.Red, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Red, report.Overall);
        Assert.Equal("ROOT|port1", Assert.Single(report.Missing).PortId);
        Assert.Contains(new Finding(FactKind.DetectedFact, "MOUSE", "MOUSE", "Disconnected: it was connected to Port 1 of ROOT and is no longer present.") { IsProblem = true }, report.Findings);
    }

    [Fact]
    public void StatusesArePrecedenceOrdered()
    {
        // Owner decisions in #2 and #39: red > purple (unstable, OS error) > orange (potentially unstable) > grey > green.
        Assert.Equal(
            [ConnectionStatus.Red, ConnectionStatus.Purple, ConnectionStatus.Orange, ConnectionStatus.Grey, ConnectionStatus.Green],
            Enum.GetValues<ConnectionStatus>().OrderDescending());
    }

    [Fact]
    public void UnreadableHubMakesCountsUnknownUnlessAlreadyExceeded()
    {
        var unreadable = new UsbNode("HUB", UsbNodeKind.Hub, "HUB", []) { Problem = "Could not read the hub: Access is denied." };
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", unreadable))];

        var within = Evaluate(tree, Profile());
        var exceeded = Evaluate(tree, Profile(hubs: "0"));

        Assert.Equal(LimitState.Unknown, within.Hubs.State);
        Assert.Equal(LimitState.Unknown, within.Tiers.State);
        Assert.Contains(within.Findings, finding => finding.Kind == FactKind.Unknown && finding.SubjectId == "HUB");
        Assert.Equal(ConnectionStatus.Purple, within.Connections["HUB"]);
        Assert.Equal(LimitState.Exceeded, exceeded.Hubs.State);
    }

    // #185: the ASUS run where the hub on port 2 failed enumeration showed "Hubs: 3" as if it were exact.
    [Fact]
    public void UnidentifiedDeviceMakesCountsUnknown()
    {
        var failed = Device("UNKNOWN", problem: "Windows stopped this device because it reported problems (code 43).");
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Hub("HUB", Hub("HUB2"), failed)))];

        var report = Evaluate(tree, Profile());

        Assert.Equal(LimitState.Unknown, report.Hubs.State);
        Assert.Equal(LimitState.Unknown, report.Tiers.State);
        Assert.Contains(new Finding(FactKind.Unknown, "UNKNOWN", "UNKNOWN",
            "This device could not be identified, so it may be a hub; the hub count and depths are at least what is shown."), report.Findings);
    }

    [Fact]
    public void DevicesBehindUnreadableHubAreUnknownNotDisconnected()
    {
        var history = new SessionHistory();
        history.Record(Desk);
        var unreadable = new UsbNode("HUB", UsbNodeKind.Hub, "HUB", []) { Problem = "Could not read the hub: The device is not ready." };
        IReadOnlyList<UsbNode> now = [Controller("C1", Hub("ROOT", unreadable, Device("MOUSE")))];

        var report = Evaluate(now, Profile(), [.. history.Missing(now)]);

        Assert.Equal(ConnectionStatus.Grey, report.Connections["KEYBOARD"]);
        Assert.Empty(report.Missing);
        Assert.Contains(new Finding(FactKind.Unknown, "KEYBOARD", "KEYBOARD", "It was behind HUB, which cannot be read; it may still be connected."), report.Findings);
        Assert.DoesNotContain(report.Findings, finding => finding.Message.StartsWith("Disconnected"));
    }

    [Fact]
    public void DeviceOnUnreadablePortIsUnknownNotDisconnected()
    {
        var history = new SessionHistory();
        history.Record(Desk);
        var unreadablePort = Port("ROOT", 2, null) with { Problem = "Could not read the port: The device is not ready." };
        var rootHub = new UsbNode("ROOT", UsbNodeKind.Hub, "ROOT", [Port("ROOT", 1, Hub("HUB", Device("KEYBOARD"))), unreadablePort]);
        IReadOnlyList<UsbNode> now = [Controller("C1", rootHub)];

        var report = Evaluate(now, Profile(), [.. history.Missing(now)]);

        Assert.Equal(ConnectionStatus.Grey, report.Connections["MOUSE"]);
        Assert.Empty(report.Missing);
        Assert.Equal(LimitState.Unknown, report.Hubs.State);
    }

    [Fact]
    public void HubWithoutPortsIsIncompleteEvenWithoutProblem()
    {
        var report = Evaluate([Controller("C1", Hub("ROOT", new UsbNode("HUB", UsbNodeKind.Hub, "HUB", [])))], Profile());

        Assert.Equal(LimitState.Unknown, report.Hubs.State);
        Assert.Equal(ConnectionStatus.Grey, report.Connections["HUB"]);
        Assert.NotEqual(ConnectionStatus.Green, report.Overall);
    }

    [Fact]
    public void ControllerWithoutRootHubIsIncomplete()
    {
        var report = Evaluate([Controller("C1", Hub("ROOT", Device("MOUSE"))), new UsbNode("C2", UsbNodeKind.Controller, "C2", [])], Profile());

        Assert.Equal(LimitState.Unknown, report.Hubs.State);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.SubjectId == "C2");
        Assert.NotEqual(ConnectionStatus.Green, report.Overall);
    }

    [Fact]
    public void MissingHubAndItsDevicesAreRedAndBeatAnError()
    {
        var history = new SessionHistory();
        history.Record(Desk);
        IReadOnlyList<UsbNode> now = [Controller("C1", Hub("ROOT", null, Device("MOUSE", "code 43")))];

        var report = Evaluate(now, Profile(), [.. history.Missing(now)]);

        Assert.Equal(ConnectionStatus.Red, report.Connections["HUB"]);
        Assert.Equal(ConnectionStatus.Red, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Purple, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Red, report.Overall);
    }

    [Fact]
    public void ValueInheritedFromUnverifiedPlatformIsNotVerified()
    {
        using var db = new TestDatabase();
        db.Write(TestDatabase.PlatformFile, TestDatabase.Platform + "\nexceptions:\n  - \"Platform-wide note.\"\n");
        db.WriteComputer("computers/c.yaml", "Test laptop", "verification:\n  status: verified");

        var report = DiagnosticEvaluator.Evaluate(Desk, db.Load().Computers.Values.Single(), [], externalDisplays: 0);

        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.StartsWith("Platform-wide note."));
        Assert.False(report.Hops.Verified);
    }

    [Fact]
    public void ReconnectedDeviceIsPurpleWithAFindingEvenWithoutAProfile()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Device("MOUSE"), Device("KEYBOARD")))];

        foreach (var profile in new[] { Profile(), null })
        {
            var report = DiagnosticEvaluator.Evaluate(tree, profile?.Profile, [], externalDisplays: 0, reconnected: ["MOUSE"], guide: profile?.Guide);

            Assert.Equal(ConnectionStatus.Purple, report.Connections["MOUSE"]);
            Assert.NotEqual(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
            Assert.Equal(ConnectionStatus.Purple, report.Overall);
            Assert.Contains(new Finding(FactKind.DetectedFact, "MOUSE", "MOUSE", "Disconnected and reconnected during this session.") { IsProblem = true }, report.Findings);
        }
    }

    [Fact]
    public void ReconnectedDeviceIsPurpleInEveryCheckedProfile()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Device("MOUSE")))];

        var report = DiagnosticEvaluator.EvaluateAll(tree, Chips("  recommended_tiers: 4", 0, 1).Profiles, [], 0, ["MOUSE"]);

        Assert.All(report.Profiles, result => Assert.Equal(ConnectionStatus.Purple, result.Report.Connections["MOUSE"]));
        Assert.Equal(ConnectionStatus.Purple, report.Connections["MOUSE"]);
    }

    [Fact]
    public void RecoveredConnectionIsGreenAgain()
    {
        IReadOnlyList<UsbNode> broken = [Controller("C1", Hub("ROOT", Device("MOUSE", "code 43")))];
        IReadOnlyList<UsbNode> recovered = [Controller("C1", Hub("ROOT", Device("MOUSE")))];
        var history = new SessionHistory();
        history.Record(broken);

        Assert.Equal(ConnectionStatus.Purple, Evaluate(broken, Profile()).Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Green, Evaluate(recovered, Profile(), [.. history.Missing(recovered)]).Connections["MOUSE"]);
    }

    [Fact]
    public void ModelExceptionsAreDatabaseFactsOnlyWhenVerified()
    {
        const string exceptions = "exceptions:\n  - \"Rear ports share one hub.\"";

        var verified = Evaluate(Desk, Profile(extra: exceptions));
        var unverified = Evaluate(Desk, Profile(status: "unverified", extra: exceptions));

        Assert.Contains(new Finding(FactKind.VerifiedDatabaseFact, DiagnosticEvaluator.ComputerId, "Test laptop", "Rear ports share one hub."), verified.Findings);
        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Test laptop", "Rear ports share one hub. (unverified, computers/c.yaml)"), unverified.Findings);
    }

    public static TheoryData<string, string, string> PartlyKnownProfiles => new()
    {
        { "5", "null", "verified" },
        { "null", "5", "partial" }, // Like a profile without a guide: only the USB maximum.
        { "5", "5", "unverified" },
    };

    // Owner decision in #39: stability is judged on the limits a profile knows.
    [Theory]
    [MemberData(nameof(PartlyKnownProfiles))]
    public void KnownLimitsAloneJudgeStability(string hubs, string hops, string status)
    {
        var report = Evaluate(Desk, Profile(hubs, hops, status));

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.All(report.Connections.Values, line => Assert.Equal(ConnectionStatus.Green, line));
    }

    [Fact]
    public void ProfileWithoutAnyLimitIsUnknown()
    {
        var report = Evaluate(Desk, Profile("null", "null", "partial"));

        Assert.Equal(ConnectionStatus.Grey, report.Overall);
        Assert.All(report.Connections.Values, line => Assert.Equal(ConnectionStatus.Grey, line));
    }

    [Fact]
    public void SeveralProfilesTakeTheWorstPerLine()
    {
        // One guide (one hub recommended) for three chips: no, one and five built-in hub levels.
        var (profiles, guide) = Chips("  recommended_tiers: 3\n  min_recommended_hubs: 0", 0, 1, 5);

        var report = DiagnosticEvaluator.EvaluateAll(Desk, profiles, [], externalDisplays: 0, guide: guide);

        Assert.Equal(ConnectionStatus.Purple, report.Overall);
        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]); // Worst: tight.
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Equal([ConnectionStatus.Green, ConnectionStatus.Orange, ConnectionStatus.Purple],
            report.Profiles.Select(result => result.Report.Overall));
    }

    [Fact]
    public void MarkedHopsCountInEveryProfilesChecks()
    {
        // +2 hops on the hub's port: the hub is tier 4 with 2 hops, the keyboard tier 5 with 3 hops.
        var marks = new Dictionary<string, int> { ["ROOT|port1"] = 2 };

        // Three built-in hub levels leave two hubs; none leave five, with two recommended.
        var (profiles, guide) = Chips("  recommended_tiers: 4\n  min_recommended_hubs: 0", 3, 0);
        var report = DiagnosticEvaluator.EvaluateAll(Desk, profiles, [], externalDisplays: 0, extraHops: marks, guide: guide);

        Assert.All(report.Profiles, result => Assert.Equal((5, 3), (result.Report.Tiers.Actual, result.Report.Hops.Actual)));
        Assert.All(report.Profiles, result => Assert.Equal(1, result.Report.Hubs.Actual));
        Assert.Equal([ConnectionStatus.Purple, ConnectionStatus.Orange],
            report.Profiles.Select(result => result.Report.Connections["KEYBOARD"]));
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
    }

    [Fact]
    public void OnlyARecommendationKnownIsJudged()
    {
        var report = Evaluate(Desk, Profile("null", "null", recommendedHops: "0"));

        Assert.Equal(ConnectionStatus.Orange, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
    }

    [Fact]
    public void HubInIncompleteTreeIsJudgedWhenNoHubLimitIsKnown()
    {
        var report = Evaluate([Controller("C1", Hub("ROOT", new UsbNode("HUB", UsbNodeKind.Hub, "HUB", [])))], Profile(hubs: "null"));

        Assert.Equal(ConnectionStatus.Green, report.Connections["HUB"]);
    }

    [Fact]
    public void SeveralProfilesKeepErrorsAndDisconnections()
    {
        var history = new SessionHistory();
        history.Record(Desk);
        IReadOnlyList<UsbNode> now = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD", "code 43")), null))];

        var report = DiagnosticEvaluator.EvaluateAll(now, Chips("  recommended_tiers: 4", 0, 5).Profiles, [.. history.Missing(now)], externalDisplays: 0);

        Assert.Equal(ConnectionStatus.Red, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Red, report.Overall);
    }

    [Fact]
    public void NoSelectedProfileIsUnknown()
    {
        var report = DiagnosticEvaluator.EvaluateAll(Desk, [], [], externalDisplays: 0);

        Assert.Equal(ConnectionStatus.Grey, report.Overall);
        Assert.Empty(report.Profiles);
        Assert.All(report.Connections.Values, line => Assert.Equal(ConnectionStatus.Grey, line));
    }

    [Fact]
    public void EmptyTreeWithKnownLimitsIsGreen()
    {
        var report = Evaluate([Controller("C1", Hub("ROOT", (UsbNode?)null))], Profile());

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.Empty(report.Connections);
    }

    [Fact]
    public void DiagnosticsHaveNoSpeedOrBandwidthFields()
    {
        var names = typeof(DiagnosticEvaluator).Assembly.GetTypes()
            .Where(type => type.Namespace is "UsbHelper.Core.Diagnostics" or "UsbHelper.Core.Topology")
            .SelectMany(type => type.GetProperties().Select(property => property.Name).Append(type.Name));

        Assert.DoesNotContain(names, name => name.Contains("Speed") || name.Contains("Bandwidth") || name.Contains("Throughput"));
    }

    [Fact]
    public void HubLimitAppliesPerBus()
    {
        // #195: 8 hubs in all, 6 on the USB 2 bus. A limit of 6 per bus is met; 5 is exceeded by the USB 2 bus.
        var withinSix = Evaluate(TesterMac(), Profile(hubs: "6")).Hubs;
        Assert.Equal((6, LimitState.Within), (withinSix.Actual, withinSix.State));
        Assert.Equal(LimitState.Exceeded, Evaluate(TesterMac(), Profile(hubs: "5")).Hubs.State);
    }

    [Fact]
    public void OnlyHubsOnTheBusOverTheLimitAreUnstable()
    {
        // Review of #216: USB 2 has 6 hubs and is over a limit of 5; USB 3 has 2 and is within.
        var report = Evaluate(TesterMac(), Profile(hubs: "5"));

        Assert.All(new[] { "H1", "H2", "H3", "H4", "H5", "H6" }, hub => Assert.Equal(ConnectionStatus.Purple, report.Connections[hub]));
        Assert.All(new[] { "S1", "S2" }, hub => Assert.NotEqual(ConnectionStatus.Purple, report.Connections[hub]));
        Assert.Contains(report.Findings, finding => finding.Message.StartsWith("6 hubs are on the USB 2 bus; limits.max_hubs is 5", StringComparison.Ordinal));
    }

    // A root hub with a USB 2 hub, a USB 3 hub, and on port 3, whose bus is not reported, a hub with a mouse.
    private static IReadOnlyList<UsbNode> WithUnknownBus()
    {
        var root = RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK")));
        return [Controller("C1", root with { Children = [.. root.Children, Port("ROOT", 3, Hub("UNKNOWN-BUS", Device("MOUSE")))] })];
    }

    [Fact]
    public void UnknownBusIsWithinWhenAllHubsTogetherAreWithin()
    {
        // 3 hubs in all can never put one bus over 5.
        var report = Evaluate(WithUnknownBus(), Profile(hubs: "5"));

        Assert.Equal((1, LimitState.Within), (report.Hubs.Actual, report.Hubs.State));
        Assert.DoesNotContain(report.Findings, finding => finding.Message.Contains("which bus", StringComparison.Ordinal));
        Assert.Equal(ConnectionStatus.Green, report.Connections["UNKNOWN-BUS"]);
    }

    [Fact]
    public void UnknownBusLeavesTheHubLimitOpenWhenAllHubsTogetherCouldBeOver()
    {
        var report = Evaluate(WithUnknownBus(), Profile(hubs: "2"));

        Assert.Equal((1, LimitState.Unknown), (report.Hubs.Actual, report.Hubs.State));
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.Contains("which bus", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportCarriesEveryDepth()
    {
        Assert.Equal(new ConnectionDepth(3, 1), Evaluate(Desk, Profile()).Depths["KEYBOARD"]);
    }
}
