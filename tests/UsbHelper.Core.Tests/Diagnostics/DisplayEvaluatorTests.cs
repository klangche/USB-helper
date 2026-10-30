using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

public class DisplayEvaluatorTests
{
    private static readonly IReadOnlyList<UsbNode> Desk = [Controller("C1", Hub("ROOT", Device("MOUSE")))];

    private static ResolvedProfile Profile(string maxExternal = "2", string status = "verified", string extra = "", string displays = "")
    {
        using var db = new TestDatabase();
        db.Write("computers/c.yaml", $"""
            inherits: test-platform
            manufacturer: Test
            model: Test laptop
            limits:
              max_hubs: 5
              max_tiers: 7
              max_hops: 5
            displays:
              max_external: {maxExternal}
            {displays}
            verification:
              status: {status}
            {extra}
            """);
        return db.Load().Computers.Values.Single();
    }

    private static DiagnosticReport Evaluate(IReadOnlyList<UsbNode> tree, ResolvedProfile? profile, int? displays) =>
        DiagnosticEvaluator.Evaluate(tree, profile, [], displays);

    [Fact]
    public void UnknownDisplayCountIsNeverGreen()
    {
        var report = Evaluate(Desk, Profile(), null);

        Assert.Equal(LimitState.Unknown, report.Displays.State);
        Assert.NotEqual(ConnectionStatus.Green, report.Overall);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message == "The number of external displays is not known.");
    }

    [Fact]
    public void NoExternalDisplayNeedsNoDisplayLimit()
    {
        var report = Evaluate(Desk, Profile(maxExternal: "null"), 0);

        Assert.Equal(LimitState.Within, report.Displays.State);
        Assert.Equal(ConnectionStatus.Green, report.Overall);
    }

    [Fact]
    public void DisplaysWithinVerifiedLimitAreGreen()
    {
        var report = Evaluate(Desk, Profile(), 2);

        Assert.Equal(new DisplayCheck(2, 2, true, "computers/c.yaml", LimitState.Within, []), report.Displays with { DisplayLinkAdapters = [] });
        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.DetectedFact && finding.Message.StartsWith("2 external displays are active"));
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.VerifiedDatabaseFact && finding.Message == "displays.max_external = 2 (computers/c.yaml).");
    }

    [Fact]
    public void TooManyDisplaysIsYellowWithARisk()
    {
        var report = Evaluate(Desk, Profile(maxExternal: "1"), 2);

        Assert.Equal(LimitState.Exceeded, report.Displays.State);
        Assert.Equal(ConnectionStatus.Yellow, report.Overall);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.PredictedRisk && finding.Message == "2 external displays are active; displays.max_external is 1.");
    }

    [Fact]
    public void DisplaysWithUnknownLimitAreGrey()
    {
        var report = Evaluate(Desk, Profile(maxExternal: "null"), 1);

        Assert.Equal(LimitState.Unknown, report.Displays.State);
        Assert.Equal(ConnectionStatus.Grey, report.Overall);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.StartsWith("displays.max_external is unknown"));
    }

    [Fact]
    public void UnverifiedDisplayLimitIsGreyEvenWhenMet()
    {
        var report = Evaluate(Desk, Profile(status: "unverified"), 1);

        Assert.Equal(LimitState.Within, report.Displays.State);
        Assert.False(report.Displays.Verified);
        Assert.Equal(ConnectionStatus.Grey, report.Overall);
    }

    [Fact]
    public void PortCapabilitiesAreListedOnlyAsTheProfileStatesThem()
    {
        const string ports = """
            ports:
              left_front:
                location: left side, front
                usb: usb-c
                thunderbolt: 4
                displayport_alt_mode: true
                displayport_mst: null
              right:
                usb: usb-c
                displayport_alt_mode: false
                limitations:
                  - "Charging only below 15 W."
            """;

        var report = Evaluate(Desk, Profile(extra: ports), 0);

        Assert.Contains(new Finding(FactKind.VerifiedDatabaseFact, DiagnosticEvaluator.ComputerId, "Port left_front (left side, front)",
            "USB: usb-c; Thunderbolt 4; DisplayPort Alt Mode: yes."), report.Findings);
        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Port left_front (left side, front)",
            "Unknown: DisplayPort MST."), report.Findings);
        Assert.Contains(new Finding(FactKind.VerifiedDatabaseFact, DiagnosticEvaluator.ComputerId, "Port right",
            "USB: usb-c; DisplayPort Alt Mode: no."), report.Findings);
        Assert.Contains(new Finding(FactKind.VerifiedDatabaseFact, DiagnosticEvaluator.ComputerId, "Port right", "Charging only below 15 W."), report.Findings);
    }

    [Fact]
    public void PortCapabilitiesFromUnverifiedProfileAreUnknown()
    {
        var report = Evaluate(Desk, Profile(status: "unverified", extra: "ports:\n  left:\n    displayport_alt_mode: true"), 0);

        Assert.Contains(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Port left", "DisplayPort Alt Mode: yes."), report.Findings);
    }

    [Fact]
    public void DisplayRestrictionsAreListed()
    {
        var report = Evaluate(Desk, Profile(displays: "  restrictions:\n    - \"HDMI and the left port share one display output.\""), 0);

        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.VerifiedDatabaseFact && finding.Message == "HDMI and the left port share one display output.");
    }

    [Fact]
    public void DisplayLinkAdapterIsDetectedInTheTree()
    {
        var adapter = Device("DOCK-DL") with { VendorId = 0x17E9, ProductId = 0x6006 };
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Hub("DOCK", adapter)))];

        var report = Evaluate(tree, Profile(extra: "ports:\n  left:\n    displaylink: true"), 0);

        Assert.Equal(["DOCK-DL"], report.Displays.DisplayLinkAdapters);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.DetectedFact && finding.SubjectId == "DOCK-DL");
        Assert.Equal(ConnectionStatus.Green, report.Overall);
    }

    [Fact]
    public void DisplayLinkOnComputerWithoutSupportIsARisk()
    {
        var adapter = Device("DL") with { VendorId = 0x17E9, ProductId = 0x6006 };

        var report = Evaluate([Controller("C1", Hub("ROOT", adapter))], Profile(extra: "ports:\n  left:\n    displaylink: false"), 0);

        Assert.Equal(ConnectionStatus.Yellow, report.Overall);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.PredictedRisk && finding.Message.StartsWith("A DisplayLink adapter is connected, but"));
    }

    [Fact]
    public void DisplayLinkWithUnknownSupportIsGrey()
    {
        var adapter = Device("DL") with { VendorId = 0x17E9, ProductId = 0x6006 };

        var report = Evaluate([Controller("C1", Hub("ROOT", adapter))], Profile(), 0);

        Assert.Equal(ConnectionStatus.Grey, report.Overall);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.Unknown && finding.Message.Contains("DisplayLink is unknown"));
    }
}
