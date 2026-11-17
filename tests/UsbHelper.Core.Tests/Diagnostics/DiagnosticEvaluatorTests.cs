using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

public class DiagnosticEvaluatorTests
{
    // Root port 1: hub with a keyboard (tier 3); root port 2: mouse (tier 2). One external hub.
    private static readonly IReadOnlyList<UsbNode> Desk = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD")), Device("MOUSE")))];

    /// <summary>A computer profile and the guide to judge it by (#285).</summary>
    private sealed record Judge(ResolvedProfile Profile, Guide? Guide);

    private const string TiersSource = "USB-Helper: limits.tiers.max 7 in guides/usb-helper.yaml";

    // USB-Helper's guide with the hub and tier limits given ("null" states none) and the status given, for every file.
    private static Judge Profile(string hubs = "5", string tiers = "7", string status = "verified", string extra = "", string recommendedTiers = "null")
    {
        using var db = new TestDatabase { UsbHelperStatus = status };
        db.UsbHelper["hubs.max"] = None(hubs);
        db.UsbHelper["tiers.max"] = None(tiers);
        db.UsbHelper["tiers.recommended"] = None(recommendedTiers);
        db.Write("computers/c.yaml", $"""
            platform: test
            chip_family: platform
            manufacturer: Test
            model: Test laptop
            verification:
              status: {status}
            {extra}
            """);
        var loaded = db.Load();
        return new Judge(loaded.Computers.Values.Single(), loaded.DefaultGuide);
    }

    private static string None(string value) => value == "null" ? Guide.None : value;

    // Platform profiles of one database, one per chip, each with its own tiers.max and tiers.recommended in USB-Helper's
    // guide, and that guide.
    private static (IReadOnlyList<ResolvedProfile> Profiles, Guide Guide) Chips(params (string Max, string Recommended)[] tiers)
    {
        using var db = new TestDatabase().Write("platforms/chips.yaml", "name: Chips\nchips:\n"
            + string.Concat(tiers.Select((_, i) => $"  c{i}: {{ name: C{i}, built_in_hub_levels: 0 }}\n")));
        for (var i = 0; i < tiers.Length; i++)
        {
            db.UsbHelper[$"platforms.chips.chips.c{i}.limits.tiers.max"] = tiers[i].Max;
            db.UsbHelper[$"platforms.chips.chips.c{i}.limits.tiers.recommended"] = tiers[i].Recommended;
        }
        var loaded = db.Load();
        return ([.. tiers.Select((_, i) => loaded.Platforms[$"chips-c{i}"])], loaded.DefaultGuide);
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
        Assert.Equal(new LimitCheck("limits.max_hubs", 1, 5, true, "USB-Helper: limits.buses.usb2.hubs.max 5 in guides/usb-helper.yaml", LimitState.Within), report.Hubs);
        Assert.Equal(new LimitCheck("limits.max_tiers", 3, 7, true, TiersSource, LimitState.Within), report.Tiers);
        Assert.Equal(2, report.Findings.Count(finding => finding.Kind == FactKind.VerifiedDatabaseFact));
    }

    [Fact]
    public void PartiallyVerifiedProfileValuesCountAsVerified()
    {
        Assert.Equal(ConnectionStatus.Green, Evaluate(Desk, Profile(status: "partial")).Overall);
    }

    [Theory]
    [InlineData("partial", true)]
    [InlineData("verified", true)]
    [InlineData("unverified", false)]
    public void TheGuidesStatusDecidesWhetherItsLimitsAreVerified(string status, bool verified)
    {
        var report = Evaluate(Desk, Profile(status: status));

        Assert.Equal(verified, report.Tiers.Verified);
        Assert.Equal(verified, report.Findings.Any(finding => finding.Kind == FactKind.VerifiedDatabaseFact && finding.Message == $"limits.max_tiers = 7 ({TiersSource})."));
    }

    // #285: tiers as the USB specification counts them, the chip's built-in hub levels included (here an A-series chip,
    // two levels).
    [Fact]
    public void TiersCountTheBuiltInHubLevels()
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform.Replace("built_in_hub_levels: 0", "built_in_hub_levels: 2"));
        db.UsbHelper["tiers.max"] = "4";
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(Desk, loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.DefaultGuide);

        Assert.Equal(5, report.Tiers.Actual); // The keyboard: tier 3 counted from the port, plus two built-in levels.
        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["HUB"]); // Tier 4.
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]); // Tier 4.
        Assert.Contains(report.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Brief == "Tier depth 5 exceeds limits.max_tiers 4.");
    }

    [Fact]
    public void UnknownBuiltInHubLevelsLeaveTheTierCheckOpen()
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform.Replace("built_in_hub_levels: 0", "built_in_hub_levels: null"));
        db.UsbHelper["tiers.max"] = "7";
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(Desk, loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.DefaultGuide);

        Assert.Equal(LimitState.Unknown, report.Tiers.State);
        Assert.Equal(3, report.Tiers.Actual); // At least what is counted.
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
        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Test laptop", "limits.max_hubs is unknown (USB-Helper: limits.buses.usb2.hubs.max none in guides/usb-helper.yaml)."), report.Findings);
    }

    [Fact]
    public void FindingsNameTheComputerByItsNameWhenSet()
    {
        var report = Evaluate(Desk, Profile(hubs: "null", extra: "name: Neo"));

        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Neo", "limits.max_hubs is unknown (USB-Helper: limits.buses.usb2.hubs.max none in guides/usb-helper.yaml)."), report.Findings);
    }

    [Fact]
    public void UnverifiedLimitsAreJudgedAndSaidToBeUnverified()
    {
        var report = Evaluate(Desk, Profile(status: "unverified"));

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.False(report.Tiers.Verified);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.StartsWith("limits.max_tiers = 7 comes from an unverified profile"));
        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FactKind.VerifiedDatabaseFact);
    }

    [Fact]
    public void TiersAboveLimitIsUnstableOnlyWhereExceeded()
    {
        var report = Evaluate(Desk, Profile(tiers: "2"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["HUB"]); // Tier 2: within the limit.
        Assert.Equal(LimitState.Exceeded, report.Tiers.State);
        Assert.Equal(ConnectionStatus.Purple, report.Overall);
    }

    [Fact]
    public void TiersAboveLimitIsUnstable()
    {
        var report = Evaluate(Desk, Profile(tiers: "2"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Contains(report.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message == "Tier depth 3 exceeds limits.max_tiers 2.");
    }

    [Fact]
    public void TiersAboveRecommendedIsPotentiallyUnstable()
    {
        var report = Evaluate(Desk, Profile(recommendedTiers: "2"));

        Assert.Equal(ConnectionStatus.Orange, report.Overall);
        Assert.Equal(ConnectionStatus.Orange, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        const string Source = "USB-Helper: limits.tiers.recommended 2 in guides/usb-helper.yaml";
        Assert.Contains(new Finding(FactKind.PredictedRisk, "KEYBOARD", "KEYBOARD",
            "Tier depth 3 exceeds the recommended limits.recommended_tiers 2; limits.max_tiers is 7."), report.Findings);
        Assert.Contains(new Finding(FactKind.VerifiedDatabaseFact, DiagnosticEvaluator.ComputerId, "Test laptop",
            $"limits.recommended_tiers = 2 ({Source})."), report.Findings);
    }

    [Fact]
    public void TiersAboveMaximumReportsOnlyTheMaximum()
    {
        var report = Evaluate(Desk, Profile(tiers: "2", recommendedTiers: "2"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Single(report.Findings, finding => finding.SubjectId == "KEYBOARD");
    }

    [Fact]
    public void UnknownRecommendedTiersIsNeitherGreyNorNamed()
    {
        var report = Evaluate(Desk, Profile());

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.DoesNotContain(report.Findings, finding => finding.Message.Contains("recommended_tiers"));
    }

    [Fact]
    public void ExceededUnverifiedRecommendedTiersSaysUnverified()
    {
        var report = Evaluate(Desk, Profile(status: "unverified", recommendedTiers: "2"));

        Assert.Equal(ConnectionStatus.Orange, report.Connections["KEYBOARD"]);
        Assert.Contains(report.Findings, finding => finding.Message
            == "Tier depth 3 exceeds the recommended limits.recommended_tiers 2 (unverified limit); limits.max_tiers is 7 (unverified limit)."
            && finding.Brief == "Tier depth 3 exceeds the recommended limits.recommended_tiers 2; limits.max_tiers is 7.");
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
        Assert.Equal(3, report.Tiers.Actual); // Tiers are this controller's.
    }

    [Fact]
    public void AHubThatCannotBeReadOnAnotherControllerMakesTheHubCountAMinimum()
    {
        IReadOnlyList<UsbNode> computer = [.. Desk, Controller("C2", Hub("ROOT2", Hub("BROKEN") with { Problem = "code 43" }))];

        var judge = Profile(hubs: "5");
        var report = DiagnosticEvaluator.Evaluate(Desk, judge.Profile, [], externalDisplays: 0, wholeTree: computer, guide: judge.Guide);

        Assert.Equal((2, LimitState.Unknown), (report.Hubs.Actual, report.Hubs.State));
        Assert.Equal(LimitState.Within, report.Tiers.State);
    }

    [Fact]
    public void BuiltInHubIsNotCountedAndNotJudgedAsHub()
    {
        // Root port 1 holds the laptop's built-in hub; its port 1 is a laptop socket with a mouse.
        IReadOnlyList<UsbNode> laptop = [Controller("C1", Hub("ROOT", Hub("INTERNAL", Device("MOUSE")).Connectable(true)).Connectable(false))];

        var report = Evaluate(laptop, Profile(hubs: "0", tiers: "2"));

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.Equal(ConnectionStatus.Green, report.Connections["INTERNAL"]);
        Assert.Equal((0, 2), (report.Hubs.Actual, report.Tiers.Actual));
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
        var report = Evaluate(Desk, Profile(tiers: "2", status: "unverified"));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Contains(report.Findings, finding => finding.Message == "Tier depth 3 exceeds limits.max_tiers 2 (unverified limit)."
            && finding.Brief == "Tier depth 3 exceeds limits.max_tiers 2.");
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

        Assert.Equal(ConnectionStatus.Purple, Evaluate(tree, Profile(tiers: "2")).Connections["KEYBOARD"]);
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
        Assert.False(report.Tiers.Verified);
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

        var report = DiagnosticEvaluator.EvaluateAll(tree, Chips(("7", "4"), ("6", "4")).Profiles, [], 0, ["MOUSE"]);

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
        { "null", "7", "partial" }, // Like a profile without a guide: only the USB maximum.
        { "5", "7", "unverified" },
    };

    // Owner decision in #39: stability is judged on the limits a profile knows.
    [Theory]
    [MemberData(nameof(PartlyKnownProfiles))]
    public void KnownLimitsAloneJudgeStability(string hubs, string tiers, string status)
    {
        var report = Evaluate(Desk, Profile(hubs, tiers, status));

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
        // Three chips: within, above the recommendation, above the maximum for the keyboard behind one hub.
        var (profiles, guide) = Chips(("7", "3"), ("7", "2"), ("2", "2"));

        var report = DiagnosticEvaluator.EvaluateAll(Desk, profiles, [], externalDisplays: 0, guide: guide);

        Assert.Equal(ConnectionStatus.Purple, report.Overall);
        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]); // Worst: tight.
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Equal([ConnectionStatus.Green, ConnectionStatus.Orange, ConnectionStatus.Purple],
            report.Profiles.Select(result => result.Report.Overall));
    }

    [Fact]
    public void MarksCountInEveryProfilesChecks()
    {
        // A mark of 2 on the hub's port: the hub is tier 4, the keyboard tier 5, and the mark adds 2 hubs (#297).
        var marks = new Dictionary<string, int> { ["ROOT|port1"] = 2 };

        var (profiles, guide) = Chips(("4", "2"), ("7", "4"));
        var report = DiagnosticEvaluator.EvaluateAll(Desk, profiles, [], externalDisplays: 0, marks: marks, guide: guide);

        Assert.All(report.Profiles, result => Assert.Equal(5, result.Report.Tiers.Actual));
        Assert.All(report.Profiles, result => Assert.Equal(3, result.Report.Hubs.Actual));
        Assert.Equal([ConnectionStatus.Purple, ConnectionStatus.Orange],
            report.Profiles.Select(result => result.Report.Connections["KEYBOARD"]));
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
    }

    [Fact]
    public void OnlyARecommendationKnownIsJudged()
    {
        var report = Evaluate(Desk, Profile("null", "null", recommendedTiers: "2"));

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

        var report = DiagnosticEvaluator.EvaluateAll(now, Chips(("7", "4"), ("2", "2")).Profiles, [.. history.Missing(now)], externalDisplays: 0);

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

    // #285: hubs on a port whose bus is not reported get the lower of the USB 2 and USB 3 values, and the log says so
    // when they differ. Only hubs have values per bus; tiers are the same on every bus.
    [Fact]
    public void UnknownBusIsJudgedByTheLowerBusValue()
    {
        using var db = new TestDatabase();
        db.UsbHelper["usb2.hubs.max"] = "0";
        db.UsbHelper["usb3.hubs.max"] = "5";
        db.WriteComputer("computers/c.yaml", "Test laptop");
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(WithUnknownBus(), loaded.Computers.Values.Single(), [], externalDisplays: 0, guide: loaded.DefaultGuide);

        Assert.Equal((0L, LimitState.Exceeded), (report.Hubs.Limit, report.Hubs.State));
        Assert.Contains(report.Findings, finding => finding.Message.Contains("which bus", StringComparison.Ordinal));
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
        Assert.Equal(new ConnectionDepth(3), Evaluate(Desk, Profile()).Depths["KEYBOARD"]);
    }

    // #297: a cable mark adds min(N, 2) hubs to the hub count of its port's bus.
    [Fact]
    public void MarkedHubsCountTowardTheHubLimit()
    {
        var marks = new Dictionary<string, int> { ["ROOT|port1"] = 3 };
        var judge = Profile(hubs: "2");

        var report = DiagnosticEvaluator.Evaluate(Desk, judge.Profile, [], externalDisplays: 0, marks: marks, guide: judge.Guide);

        Assert.Equal((3, LimitState.Exceeded), (report.Hubs.Actual, report.Hubs.State));
        Assert.Equal(ConnectionStatus.Purple, report.Connections["HUB"]);
        Assert.Equal(6, report.Tiers.Actual); // The keyboard: tier 3, plus 3 from the mark.
        Assert.Contains(report.Findings, finding => finding.Message == "3 external hubs are connected; limits.max_hubs is 2.");
    }

    [Fact]
    public void MarkedHubsCountOnlyOnTheirOwnBus()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK"))))];
        var marks = new Dictionary<string, int> { ["ROOT|port2"] = 1 };
        var judge = Profile(hubs: "1");

        var report = DiagnosticEvaluator.Evaluate(tree, judge.Profile, [], externalDisplays: 0, marks: marks, guide: judge.Guide);

        Assert.Equal(LimitState.Exceeded, report.Hubs.State);
        Assert.Equal(ConnectionStatus.Purple, report.Connections["HUB-USB3"]);
        Assert.NotEqual(ConnectionStatus.Purple, report.Connections["HUB-USB2"]);
        Assert.Contains(report.Findings, finding => finding.Message.StartsWith("2 hubs are on the USB 3 bus; limits.max_hubs is 1", StringComparison.Ordinal));
    }
}
