using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

public class DiagnosticEvaluatorTests
{
    // Root port 1: hub with a keyboard (tier 3, 1 hop); root port 2: mouse (tier 2, 0 hops). One external hub.
    private static readonly IReadOnlyList<UsbNode> Desk = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD")), Device("MOUSE")))];

    private static ResolvedProfile Profile(
        string hubs = "5", string tiers = "7", string hops = "5", string status = "verified", string extra = "", string recommendedHops = "null")
    {
        using var db = new TestDatabase();
        db.Write("computers/c.yaml", $"""
            id: test.c
            inherits: test-platform
            manufacturer: Test
            model: Test laptop
            limits:
              max_hubs: {hubs}
              max_tiers: {tiers}
              max_hops: {hops}
              recommended_hops: {recommendedHops}
            verification:
              status: {status}
            {extra}
            """);
        return db.Load().Computers["test.c"];
    }

    // No external displays: these tests are about topology. Display rules are in DisplayEvaluatorTests.
    private static DiagnosticReport Evaluate(IReadOnlyList<UsbNode> tree, ResolvedProfile? profile, params MissingDevice[] missing) =>
        DiagnosticEvaluator.Evaluate(tree, profile, missing, externalDisplays: 0);

    [Fact]
    public void AllLimitsKnownVerifiedAndMetIsGreen()
    {
        var report = Evaluate(Desk, Profile());

        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.All(report.Connections.Values, status => Assert.Equal(ConnectionStatus.Green, status));
        Assert.Equal(["HUB", "KEYBOARD", "MOUSE"], report.Connections.Keys.Order());
        Assert.Equal(new LimitCheck("limits.max_hubs", 1, 5, true, "computers/c.yaml", LimitState.Within), report.Hubs);
        Assert.Equal((3, LimitState.Within), (report.Tiers.Actual, report.Tiers.State));
        Assert.Equal((1, LimitState.Within), (report.Hops.Actual, report.Hops.State));
        Assert.Equal(3, report.Findings.Count(finding => finding.Kind == FactKind.VerifiedDatabaseFact));
    }

    [Fact]
    public void PartiallyVerifiedProfileValuesCountAsVerified()
    {
        Assert.Equal(ConnectionStatus.Green, Evaluate(Desk, Profile(status: "partial")).Overall);
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
    public void UnknownLimitIsGreyAndNamed()
    {
        var report = Evaluate(Desk, Profile(hubs: "null"));

        Assert.Equal(ConnectionStatus.Grey, report.Overall);
        Assert.Equal(LimitState.Unknown, report.Hubs.State);
        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Test laptop", "limits.max_hubs is unknown (computers/c.yaml)."), report.Findings);
    }

    [Fact]
    public void UnverifiedLimitsAreGreyEvenWhenMet()
    {
        var report = Evaluate(Desk, Profile(status: "unverified"));

        Assert.Equal(ConnectionStatus.Grey, report.Overall);
        Assert.False(report.Tiers.Verified);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.StartsWith("limits.max_tiers = 7 comes from an unverified profile"));
        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FactKind.VerifiedDatabaseFact);
    }

    [Fact]
    public void TierAboveLimitIsYellowOnlyWhereExceeded()
    {
        var report = Evaluate(Desk, Profile(tiers: "2"));

        Assert.Equal(ConnectionStatus.Yellow, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["HUB"]); // Tier 2: within the limit.
        Assert.Equal(LimitState.Exceeded, report.Tiers.State);
        Assert.Equal(ConnectionStatus.Yellow, report.Overall);
        Assert.Contains(new Finding(FactKind.PredictedRisk, "KEYBOARD", "KEYBOARD", "Tier depth 3 exceeds limits.max_tiers 2."), report.Findings);
    }

    [Fact]
    public void HopsAboveLimitIsYellow()
    {
        var report = Evaluate(Desk, Profile(hops: "0"));

        Assert.Equal(ConnectionStatus.Yellow, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Contains(report.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message == "Hop depth 1 exceeds limits.max_hops 0.");
    }

    [Fact]
    public void HopsAboveRecommendedIsYellowWithRiskFinding()
    {
        var report = Evaluate(Desk, Profile(recommendedHops: "0"));

        Assert.Equal(ConnectionStatus.Yellow, report.Overall);
        Assert.Equal(ConnectionStatus.Yellow, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["MOUSE"]);
        Assert.Contains(new Finding(FactKind.PredictedRisk, "KEYBOARD", "KEYBOARD",
            "Hop depth 1 exceeds the recommended limits.recommended_hops 0; limits.max_hops is 5."), report.Findings);
        Assert.Contains(new Finding(FactKind.VerifiedDatabaseFact, DiagnosticEvaluator.ComputerId, "Test laptop",
            "limits.recommended_hops = 0 (computers/c.yaml)."), report.Findings);
    }

    [Fact]
    public void HopsAboveMaximumReportsOnlyTheMaximum()
    {
        var report = Evaluate(Desk, Profile(hops: "0", recommendedHops: "0"));

        Assert.Equal(ConnectionStatus.Yellow, report.Connections["KEYBOARD"]);
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

        Assert.Equal(ConnectionStatus.Yellow, report.Connections["KEYBOARD"]);
        Assert.Contains(report.Findings, finding => finding.Message
            == "Hop depth 1 exceeds the recommended limits.recommended_hops 0 (unverified limit); limits.max_hops is 5 (unverified limit).");
    }

    [Fact]
    public void TooManyHubsMarksEveryHubYellow()
    {
        var report = Evaluate(Desk, Profile(hubs: "0"));

        Assert.Equal(ConnectionStatus.Yellow, report.Connections["HUB"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["KEYBOARD"]);
        Assert.Contains(new Finding(FactKind.PredictedRisk, DiagnosticEvaluator.ComputerId, "Test laptop",
            "1 external hubs are connected; limits.max_hubs is 0."), report.Findings);
    }

    [Fact]
    public void ExceededUnverifiedLimitIsYellowAndSaysUnverified()
    {
        var report = Evaluate(Desk, Profile(tiers: "2", status: "unverified"));

        Assert.Equal(ConnectionStatus.Yellow, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Grey, report.Connections["MOUSE"]);
        Assert.Contains(report.Findings, finding => finding.Message == "Tier depth 3 exceeds limits.max_tiers 2 (unverified limit).");
    }

    [Fact]
    public void DeviceProblemIsPurpleAndADetectedFact()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Device("MOUSE", "Windows stopped this device (code 43).")))];

        var report = Evaluate(tree, Profile());

        Assert.Equal(ConnectionStatus.Purple, report.Connections["MOUSE"]);
        Assert.Equal(ConnectionStatus.Purple, report.Overall);
        Assert.Contains(new Finding(FactKind.DetectedFact, "MOUSE", "MOUSE", "Windows stopped this device (code 43)."), report.Findings);
    }

    [Fact]
    public void PortErrorIsPurpleAndNamesThePort()
    {
        var port = Port("ROOT", 1, Device("UNKNOWN")) with { Problem = "The device failed enumeration." };
        IReadOnlyList<UsbNode> tree = [Controller("C1", new UsbNode("ROOT", UsbNodeKind.Hub, "USB Root Hub", [port]))];

        var report = Evaluate(tree, Profile());

        Assert.Equal(ConnectionStatus.Purple, report.Connections["UNKNOWN"]);
        Assert.Contains(new Finding(FactKind.DetectedFact, "ROOT|port1", "Port 1 of USB Root Hub", "The device failed enumeration."), report.Findings);
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
        Assert.Contains(new Finding(FactKind.DetectedFact, "MOUSE", "MOUSE", "Disconnected: it was connected to Port 1 of ROOT and is no longer present."), report.Findings);
    }

    [Fact]
    public void StatusesArePrecedenceOrdered()
    {
        // Owner decision in #2: red > purple > yellow > grey > green.
        Assert.Equal(
            [ConnectionStatus.Red, ConnectionStatus.Purple, ConnectionStatus.Yellow, ConnectionStatus.Grey, ConnectionStatus.Green],
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
        db.Write("platforms/test-platform.yaml", TestDatabase.Platform + "\nexceptions:\n  - \"Platform-wide note.\"\n");
        db.Write("computers/c.yaml", """
            id: test.c
            inherits: test-platform
            manufacturer: Test
            model: Test laptop
            limits:
              max_hubs: 5
              max_tiers: 7
              max_hops: 5
            verification:
              status: verified
            """);

        var report = Evaluate(Desk, db.Load().Computers["test.c"]);

        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.StartsWith("Platform-wide note."));
        Assert.True(report.Hubs.Verified);
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

    public static TheoryData<string, string, string, string> IncompleteProfiles => new()
    {
        { "null", "7", "5", "verified" },
        { "5", "null", "5", "verified" },
        { "5", "7", "null", "verified" },
        { "5", "7", "5", "unverified" },
        { "null", "null", "null", "partial" },
    };

    [Theory]
    [MemberData(nameof(IncompleteProfiles))]
    public void NeverGreenWhenInformationIsMissing(string hubs, string tiers, string hops, string status)
    {
        var report = Evaluate(Desk, Profile(hubs, tiers, hops, status));

        Assert.NotEqual(ConnectionStatus.Green, report.Overall);
        Assert.DoesNotContain(ConnectionStatus.Green, report.Connections.Values);
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
}
