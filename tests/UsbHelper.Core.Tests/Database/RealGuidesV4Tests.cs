using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Database;

/// <summary>Each v4 guide counts and limits exactly what its newest source says (#329), through the real database.</summary>
public class RealGuidesV4Tests
{
    private static readonly HardwareDatabase Db = HardwareDatabase.LoadV4(
        Directory.EnumerateFiles(RealDatabaseV4Tests.Folder(), "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(RealDatabaseV4Tests.Folder(), path).Replace('\\', '/'), File.ReadAllText));

    private static ResolvedProfile Neo => Db.Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")];

    private static ResolvedProfile M1Max => Db.Computers.Values.Single(profile => profile.Files[^1].Contains("macbookpro18-2", StringComparison.Ordinal));

    private static UsbNode Behind(int hubs, UsbNode device, int from = 1) => hubs == 0 ? device : Hub($"HUB{from}", Behind(hubs - 1, device, from + 1));

    private static DiagnosticReport Evaluate(ResolvedProfile profile, string guide, int hubs, UsbNode? device = null) =>
        DiagnosticEvaluator.Evaluate([Controller("C1", Hub("ROOT", Behind(hubs, device ?? Device("KEYBOARD"))))], profile, [], externalDisplays: 0, guide: Db.Guides[guide]);

    private static ConnectionStatus Status(ResolvedProfile profile, string guide, int hubs, UsbNode? device = null) =>
        Evaluate(profile, guide, hubs, device).Connections[(device ?? Device("KEYBOARD")).Id];

    [Fact]
    public void OchnoCountsTheChainAndTheRoom()
    {
        var mSeries = Db.Platforms["macos-apple-silicon"];
        Assert.Equal(ConnectionStatus.Green, Status(mSeries, "ochno", 4));
        Assert.Equal(ConnectionStatus.Purple, Status(mSeries, "ochno", 5));
        Assert.Equal(ConnectionStatus.Green, Status(Neo, "ochno", 3));
        Assert.Equal(ConnectionStatus.Purple, Status(Neo, "ochno", 4));

        // Windows Intel's computer hubs are unknown: the chain is at least 5, within its limit, and the room is 5, over 4.
        var windows = Evaluate(Db.Platforms["windows-intel"], "ochno", 5);
        Assert.Equal(ConnectionStatus.Purple, windows.Connections["KEYBOARD"]);
        Assert.Contains(windows.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message.StartsWith("Ochno: room 5 exceeds the maximum 4.", StringComparison.Ordinal));
        Assert.DoesNotContain(windows.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message.StartsWith("Ochno: chain", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtronCountsFiveCascadingHubs()
    {
        Assert.Equal(ConnectionStatus.Green, Status(Db.Platforms["macos-apple-silicon"], "extron", 4));
        Assert.Equal(ConnectionStatus.Purple, Status(Db.Platforms["macos-apple-silicon"], "extron", 5));
    }

    // An unknown computer: Crestron assumes one hub, so a device behind 4 external hubs is on tier 7.
    [Fact]
    public void CrestronAssumesOneHubWhenTheComputerIsUnknown()
    {
        Assert.Equal(ConnectionStatus.Green, Status(Db.Platforms["windows-intel"], "crestron", 4));
        var over = Evaluate(Db.Platforms["windows-intel"], "crestron", 5);
        Assert.Contains(over.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message.StartsWith("Crestron: tiers 8 exceeds the maximum 7.", StringComparison.Ordinal));
    }

    // A camera on tier 5 is over PureLink's recommendation, not its maximum; there is no camera rule. A chip value is
    // not evidence PureLink accepts, so an M1 Max MacBook Pro without a model value is "at least".
    [Fact]
    public void PureLinkRecommendsFourTiersAndAcceptsOnlyModelValues()
    {
        var camera = Device("CAMERA") with { DeviceKind = UsbDeviceKind.Camera };
        Assert.Equal(ConnectionStatus.Orange, Status(Db.Platforms["windows-intel"], "purelink", 3, camera));

        var m1Max = Evaluate(M1Max, "purelink", 3);
        Assert.Contains(m1Max.Findings, finding => finding.SubjectId == "KEYBOARD"
            && finding.Message.StartsWith("PureLink: tiers at least 5 exceeds the recommended 4.", StringComparison.Ordinal));
    }

    // An active cable with a length (#331): Crestron's first 5 m (USB 2) or 3 m (USB 3) are free, then a tier per 5 m or 3 m.
    [Theory]
    [InlineData(10, false, 1)] // Crestron's Example 2
    [InlineData(3, true, 0)]
    [InlineData(9, true, 2)]
    public void CrestronGivesTiersForAnActiveCablesLength(double metres, bool usb3, int tiers)
    {
        var mark = CableMark.ActiveCable(metres, usb3 ? UsbBus.Usb3 : UsbBus.Usb2);
        // Windows Intel's hubs are unknown, so Crestron assumes one: a device behind 4 hubs is on tier 7, the limit.
        var report = DiagnosticEvaluator.Evaluate([Controller("C1", Hub("ROOT", Behind(4, Device("KEYBOARD"))))], Db.Platforms["windows-intel"], [],
            externalDisplays: 0, marks: new Dictionary<string, CableMark> { ["HUB4|port1"] = mark }, guide: Db.Guides["crestron"]);

        var over = report.Findings.SingleOrDefault(finding => finding.SubjectId == "KEYBOARD" && finding.Kind == FactKind.PredictedRisk);
        Assert.Equal(tiers > 0, over != null);
        if (over != null)
        {
            Assert.StartsWith($"Crestron: tiers {7 + tiers} exceeds the maximum 7.", over.Message, StringComparison.Ordinal);
            Assert.Contains(over.CountParts, part => part.Element == PathElements.CableLength && part.Count == tiers);
        }
    }

    // Ochno does not count cable length: the same cable changes nothing, and only marked hubs count.
    [Fact]
    public void OchnoIgnoresACablesLength()
    {
        var mSeries = Db.Platforms["macos-apple-silicon"];
        DiagnosticReport With(CableMark mark) => DiagnosticEvaluator.Evaluate([Controller("C1", Hub("ROOT", Behind(4, Device("KEYBOARD"))))], mSeries, [],
            externalDisplays: 0, marks: new Dictionary<string, CableMark> { ["HUB4|port1"] = mark }, guide: Db.Guides["ochno"]);

        Assert.Equal(ConnectionStatus.Green, With(CableMark.ActiveCable(30, UsbBus.Usb2)).Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Purple, With(CableMark.OfHubs(1)).Connections["KEYBOARD"]);
    }

    // usb4_router is not measured yet (#332): no guide counts it, so no result depends on it.
    [Fact]
    public void NoGuideCountsUsb4RoutersYet()
    {
        Assert.All(Db.Guides.Values, guide => Assert.All(guide.MeasureDefinitions.Values, measure => Assert.DoesNotContain(PathElements.Usb4Router, measure.Counts)));
    }

    // Every rule of a manufacturer guide quotes words that are in its notes or basis.
    [Fact]
    public void EveryManufacturerQuoteIsInTheNotesOrBasis()
    {
        foreach (var guide in Db.Guides.Values.Where(guide => guide.ProAv && guide.Find("own")?.Scalar is not true))
        {
            var text = string.Join("\n", (guide.Find("notes")?.Items ?? []).Select(note => note.Scalar as string).Append(guide.Find("basis.quote")?.Scalar as string));
            Assert.All(guide.Rules, rule => Assert.Contains(rule.Quote!, text, StringComparison.Ordinal));
        }
    }
}
