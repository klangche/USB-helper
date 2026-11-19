using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

/// <summary>Counts as sums of path elements, and the findings that list them with their origins (#326).</summary>
public class PathElementsTests
{
    private static readonly HardwareDatabase Db = HardwareDatabase.Load(RealDatabaseTests.DatabaseFolder());

    private static Guide Ochno => Db.Guides["ochno"];

    private static ResolvedProfile Neo => Db.Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")];

    // A device with the given number of hubs in front of it, named prefix1 (nearest the computer) to prefixN.
    private static UsbNode Behind(int hubs, UsbNode device, string prefix = "HUB", int from = 1) =>
        hubs == 0 ? device : Hub($"{prefix}{from}", Behind(hubs - 1, device, prefix, from + 1));

    private static IReadOnlyList<UsbNode> Chain(int hubs, UsbNode device) => [Controller("C1", Hub("ROOT", Behind(hubs, device)))];

    private static DiagnosticReport Evaluate(ResolvedProfile profile, IReadOnlyList<UsbNode> tree, Guide guide, IReadOnlyDictionary<string, CableMark>? marks = null) =>
        DiagnosticEvaluator.Evaluate(tree, profile, [], externalDisplays: 0, marks: marks, guide: guide);

    private static string Hubs(int count) => $"detected: {string.Join(", ", Enumerable.Range(1, count).Select(i => $"HUB{i}"))}";

    private static Finding Over(DiagnosticReport report, string id) =>
        Assert.Single(report.Findings, finding => finding.SubjectId == id && finding.Kind == FactKind.PredictedRisk);

    [Theory]
    [InlineData("macos-apple-silicon", 4)]
    [InlineData("windows-intel", 5)]
    public void OchnoCountsTheComputerHubAndTheExternalHubs(string platform, int fits)
    {
        var profile = Db.Platforms[platform];
        var levels = 5 - fits;

        var within = Evaluate(profile, Chain(fits, Device("KEYBOARD")), Ochno);
        var over = Evaluate(profile, Chain(fits + 1, Device("KEYBOARD")), Ochno);

        Assert.Equal(ConnectionStatus.Green, within.Connections["KEYBOARD"]);
        Assert.Equal(5, within.Chain.Actual);
        Assert.Equal(LimitState.Within, within.Chain.State);
        Assert.Equal(ConnectionStatus.Purple, over.Connections["KEYBOARD"]);
        var chip = platform.Split('-', 2)[1];
        Assert.Equal(
            [new CountPart(PathElements.ComputerHub, levels, $"platforms/{platform.Split('-')[0]}.yaml {chip}"), new CountPart(PathElements.Hub, fits + 1, Hubs(fits + 1))],
            Over(over, "KEYBOARD").CountParts);
    }

    [Fact]
    public void MacBookNeoUnderOchnoFitsThreeHubs()
    {
        Assert.Equal(ConnectionStatus.Green, Evaluate(Neo, Chain(3, Device("KEYBOARD")), Ochno).Connections["KEYBOARD"]);
        var over = Evaluate(Neo, Chain(4, Device("KEYBOARD")), Ochno);

        Assert.Equal(ConnectionStatus.Purple, over.Connections["KEYBOARD"]);
        Assert.Equal(
            [new CountPart(PathElements.ComputerHub, 2, "platforms/macos.yaml a-series"), new CountPart(PathElements.Hub, 4, Hubs(4))],
            Over(over, "KEYBOARD").CountParts);
    }

    // An unknown computer_hub counts as none: at the limit the result is unknown, above it exceeded, and the part is named.
    [Fact]
    public void AnUnknownComputerHubIsNamedAndLeavesTheResultAtLeast()
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform.Replace("built_in_hub_levels: 0", "built_in_hub_levels: null"));
        db.UsbHelper["count"] = "[chain]";
        db.UsbHelper["chain.max"] = "5";
        var loaded = db.Load();
        var profile = loaded.Platforms["test-platform"];

        var within = Evaluate(profile, Chain(5, Device("KEYBOARD")), loaded.DefaultGuide);
        var over = Evaluate(profile, Chain(6, Device("KEYBOARD")), loaded.DefaultGuide);

        Assert.Equal(LimitState.Unknown, within.Chain.State);
        Assert.Equal(LimitState.Exceeded, over.Chain.State);
        Assert.Equal(
            [new CountPart(PathElements.ComputerHub, null, "platforms/test.yaml platform"), new CountPart(PathElements.Hub, 6, Hubs(6))],
            Over(over, "KEYBOARD").CountParts);
        Assert.Equal("computer_hub unknown platforms/test.yaml platform", Over(over, "KEYBOARD").CountParts[0].ToString());
    }

    // Each bus has its own tree; the computer's hub is on each of them.
    [Fact]
    public void EachBusGetsTheComputerHub()
    {
        var tree = new[] { Controller("C1", RootWithBuses("ROOT", Behind(5, Device("USB2DEV"), "TWO"), Behind(6, Device("USB3DEV"), "THREE"))) };

        var report = Evaluate(Db.Platforms["macos-apple-silicon"], tree, Ochno);

        var computerHub = new CountPart(PathElements.ComputerHub, 1, "platforms/macos.yaml apple-silicon");
        Assert.Equal([computerHub, new CountPart(PathElements.Hub, 5, "detected: TWO1, TWO2, TWO3, TWO4, TWO5")], Over(report, "USB2DEV").CountParts);
        Assert.Equal(computerHub, Over(report, "USB3DEV").CountParts[0]);
        Assert.Equal(6, Over(report, "USB3DEV").CountParts[1].Count);
    }

    // Ochno has no separate limit for cameras and audio devices.
    [Fact]
    public void ACameraUnderOchnoHasNoSeparateLimit()
    {
        var camera = Device("CAMERA") with { DeviceKind = UsbDeviceKind.Camera };
        var profile = Db.Platforms["macos-apple-silicon"];

        Assert.Equal(ConnectionStatus.Green, Evaluate(profile, Chain(4, camera), Ochno).Connections["CAMERA"]);
        var over = Evaluate(profile, Chain(5, camera), Ochno);
        Assert.DoesNotContain("cameras", Over(over, "CAMERA").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkedHubIsAPartWithTheUsersMark()
    {
        var tree = Chain(3, Device("KEYBOARD"));
        var profile = Db.Platforms["macos-apple-silicon"];
        var port = "HUB3|port1";

        Assert.Equal(ConnectionStatus.Green, Evaluate(profile, tree, Ochno, new Dictionary<string, CableMark> { [port] = CableMark.OfHubs(1) }).Connections["KEYBOARD"]);
        var over = Evaluate(profile, tree, Ochno, new Dictionary<string, CableMark> { [port] = CableMark.OfHubs(2) });

        Assert.Equal(
            [new CountPart(PathElements.ComputerHub, 1, "platforms/macos.yaml apple-silicon"), new CountPart(PathElements.Hub, 3, Hubs(3)),
                new CountPart(PathElements.MarkedHub, 2, "your mark on Port 1 of HUB3")],
            Over(over, "KEYBOARD").CountParts);
    }

    // A tier count: root_hub, computer_hub, the hubs, the tiers an active cable adds, and the device itself.
    [Fact]
    public void TiersAreRootHubComputerHubHubsCableAndDevice()
    {
        var guide = Db.Guides["crestron"];
        var tree = Chain(5, Device("KEYBOARD"));

        var report = Evaluate(Db.Platforms["windows-intel"], tree, guide, new Dictionary<string, CableMark> { ["HUB5|port1"] = CableMark.OfTiers(1) });

        Assert.Equal(
            [new CountPart(PathElements.RootHub, 1, "detected"), new CountPart(PathElements.ComputerHub, 0, "platforms/windows.yaml intel"),
                new CountPart(PathElements.Hub, 5, Hubs(5)), new CountPart(PathElements.CableLength, 1, "your mark on Port 1 of HUB5"),
                new CountPart(PathElements.Device, 1, "detected")],
            Over(report, "KEYBOARD").CountParts);
    }
}
