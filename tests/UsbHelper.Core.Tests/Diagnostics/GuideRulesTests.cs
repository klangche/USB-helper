using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

/// <summary>The guide rules of #285 on the repository's database: USB-Helper's guide by default, another guide when picked.</summary>
public class GuideRulesTests
{
    private static readonly HardwareDatabase Db = HardwareDatabase.Load(RealDatabaseTests.DatabaseFolder());

    private static Guide UsbHelper => Db.DefaultGuide;

    // A device with the given number of hubs in front of it, on the computer's own port.
    private static IReadOnlyList<UsbNode> Chain(int hubs, UsbNode device) => [Controller("C1", Hub("ROOT", Behind(hubs, device)))];

    private static UsbNode Behind(int hubs, UsbNode device) => hubs == 0 ? device : Hub($"HUB{hubs}", Behind(hubs - 1, device));

    private static DiagnosticReport Evaluate(ResolvedProfile profile, int hubs, UsbNode device, Guide? guide) =>
        DiagnosticEvaluator.Evaluate(Chain(hubs, device), profile, [], externalDisplays: 0, guide: guide);

    private static ConnectionStatus StatusOf(string profile, int hubs, UsbNode device, Guide? guide) =>
        Evaluate(Db.Platforms[profile], hubs, device, guide).Connections[device.Id];

    private static UsbNode Camera => Device("CAMERA") with { DeviceKind = UsbDeviceKind.Camera };

    // Purple above the maximum, orange above the recommendation, both counted in hubs in front of the device.
    private static ConnectionStatus Today(int hubs, int max, int recommended) =>
        hubs > max ? ConnectionStatus.Purple : hubs > recommended ? ConnectionStatus.Orange : ConnectionStatus.Green;

    // USB-Helper's guide (the owner's file of 2026-11-17; only tiers since #297): a device behind N hubs is on tier
    // N + 2 + the chip's built-in hub levels, so the most hubs in front of it is tiers − 2 − those levels, for the
    // maximum and the recommendation each. iPhone and iPad (tiers
    // at most 3, two built-in levels) exceed even in the own port; Android supports direct products only, so any hub
    // is potentially unstable (owner decisions 2026-11-17).
    [Theory]
    [InlineData("windows-intel", 5, 3)]
    [InlineData("windows-amd", 5, 3)]
    [InlineData("windows-arm", 5, 3)]
    [InlineData("macos-intel", 5, 3)]
    [InlineData("macos-apple-silicon", 4, 2)]
    [InlineData("macos-a-series", 3, 1)]
    [InlineData("apple-mobile-a-series", -1, -2)]
    [InlineData("ubuntu-intel", 5, 3)]
    [InlineData("ubuntu-amd", 5, 3)]
    [InlineData("ubuntu-arm", 5, 3)]
    [InlineData("debian-intel", 5, 3)]
    [InlineData("debian-arm", 5, 3)]
    [InlineData("android-phone-qualcomm", 1, 0)]
    [InlineData("android-tablet-exynos", 1, 0)]
    public void UsbHelperGivesItsColours(string profile, int max, int recommended)
    {
        for (var hubs = 0; hubs <= 7; hubs++)
        {
            Assert.Equal(Today(hubs, max, recommended), StatusOf(profile, hubs, Device("KEYBOARD"), UsbHelper));
        }
    }

    [Fact]
    public void MacBookNeoGetsTheASeriesColours()
    {
        var neo = Db.Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")];

        for (var hubs = 0; hubs <= 5; hubs++)
        {
            Assert.Equal(Today(hubs, 3, 1), Evaluate(neo, hubs, Device("KEYBOARD"), UsbHelper).Connections["KEYBOARD"]);
        }
    }

    // Every limit is in the guides: without one, nothing is known.
    [Fact]
    public void WithoutAGuideNothingIsJudged()
    {
        var report = Evaluate(Db.Platforms["windows-intel"], 6, Device("KEYBOARD"), null);

        Assert.Equal(ConnectionStatus.Grey, report.Connections["KEYBOARD"]);
        Assert.Null(report.Tiers.Limit);
        Assert.Null(report.Hubs.Limit);
    }

    // A picked guide's tiers count the chip's built-in hub levels: on an A-series chip (two levels) a device behind any
    // hub is above four tiers. Its empty values are USB-Helper's.
    [Fact]
    public void GuideTiersCountTheBuiltInHubLevels()
    {
        var (aSeries, guide) = Picked(2, new() { ["usb2.tiers.recommended"] = "4", ["usb2.tiers.max"] = "7", ["usb3.tiers.recommended"] = "4", ["usb3.tiers.max"] = "7" });

        Assert.Equal(ConnectionStatus.Green, Evaluate(aSeries, 0, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Orange, Evaluate(aSeries, 1, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Purple, Evaluate(aSeries, 4, Device("KEYBOARD"), guide).Connections["KEYBOARD"]); // Tier 8.
    }

    // Findings and the log name the guide and the file each number comes from.
    [Fact]
    public void FindingsNameTheGuideAndTheFile()
    {
        var (aSeries, guide) = Picked(2, new() { ["usb2.tiers.recommended"] = "4", ["usb2.tiers.max"] = "7", ["usb3.tiers.recommended"] = "4", ["usb3.tiers.max"] = "7" });

        var report = Evaluate(aSeries, 1, Device("KEYBOARD"), guide);

        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.PredictedRisk && finding.SubjectId == "KEYBOARD"
            && finding.Message == "Tier depth 5 exceeds the recommended limits.recommended_tiers 4; limits.max_tiers is 7.");
        Assert.Contains(report.Findings, finding => finding.Message == "limits.recommended_tiers = 4 (Guide: limits.tiers.recommended 4 in guides/guide.yaml).");
        Assert.Contains(report.Findings, finding => finding.Message == "limits.max_hubs = 3 (USB-Helper: limits.buses.usb2.hubs.max 3 in guides/usb-helper.yaml).");
    }

    // A test database with one chip of the given built-in hub levels, USB-Helper's guide with hubs 3 / 2, and a picked
    // guide with the values given.
    private static (ResolvedProfile Profile, Guide Guide) Picked(object levels, Dictionary<string, string> values)
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform.Replace("built_in_hub_levels: 0", $"built_in_hub_levels: {levels}"));
        db.UsbHelper["hubs.max"] = "3";
        db.UsbHelper["hubs.recommended"] = "2";
        db.WriteGuide(values);
        var loaded = db.Load();
        return (loaded.Platforms["test-platform"], loaded.Guides["guide"]);
    }

    // #275 and #285: cameras and audio devices get their own limits on top of the general ones.
    [Theory]
    [InlineData(UsbDeviceKind.Camera)]
    [InlineData(UsbDeviceKind.Audio)]
    [InlineData(UsbDeviceKind.CameraAndAudio)]
    public void CamerasAndAudioDevicesGetUsbHelpersStricterRecommendation(UsbDeviceKind kind)
    {
        var device = Device("CAMERA") with { DeviceKind = kind };

        // Cameras and audio: tiers 3 / 5, so on Windows Intel 1 hub (tier 3) is fine, 2 or 3 are orange, 4 purple.
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 1, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 2, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 3, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Purple, StatusOf("windows-intel", 4, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 2, Device("KEYBOARD") with { DeviceKind = UsbDeviceKind.Neither }, UsbHelper));
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 2, Device("THING") with { DeviceKind = UsbDeviceKind.Unknown }, UsbHelper));
        // Two built-in hub levels: one hub puts the camera on tier 5, above the recommendation.
        Assert.Equal(ConnectionStatus.Orange, StatusOf("macos-a-series", 1, device, UsbHelper));
    }

    // A guide's camera and audio limits: here 3 tiers recommended, 4 at most.
    [Fact]
    public void GuideLimitsCamerasByTiers()
    {
        var (windows, guide) = Picked(0, new() { ["camera_audio.tiers.recommended"] = "3", ["camera_audio.tiers.max"] = "4" });

        Assert.Equal(ConnectionStatus.Green, Evaluate(windows, 1, Camera, guide).Connections["CAMERA"]);
        Assert.Equal(ConnectionStatus.Orange, Evaluate(windows, 2, Camera, guide).Connections["CAMERA"]);
        Assert.Equal(ConnectionStatus.Purple, Evaluate(windows, 3, Camera, guide).Connections["CAMERA"]);
        Assert.Equal(ConnectionStatus.Green, Evaluate(windows, 2, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        Assert.Contains(Evaluate(windows, 3, Camera, guide).Findings, finding => finding.SubjectId == "CAMERA"
            && finding.Message == "Tier depth 5 exceeds limits.camera_audio_max_tiers 4 for cameras and audio devices.");
    }

    // An exceeded limit shows the most specific exceeded_note of the guide that set it: the chip's over the platform's or global note.
    [Fact]
    public void ExceededLimitShowsTheChipsNote()
    {
        using var db = new TestDatabase();
        db.UsbHelper["tiers.max"] = "2";
        db.UsbHelper["exceeded_note"] = "\"All note.\"";
        db.UsbHelper["platforms.test.chips.platform.limits.exceeded_note"] = "\"Chip note.\"";
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(Chain(1, Device("KEYBOARD")), loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.DefaultGuide);

        Assert.Contains(report.Findings, finding => finding.Brief == "Tier depth 3 exceeds limits.max_tiers 2. Chip note.");
    }

    // An empty note falls back to USB-Helper's, like the numbers.
    [Fact]
    public void EmptyNoteFallsBackToUsbHelpers()
    {
        using var db = new TestDatabase();
        db.UsbHelper["exceeded_note"] = "\"Use a powered hub.\"";
        db.WriteGuide(new() { ["usb2.tiers.max"] = "2", ["usb3.tiers.max"] = "2" });
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(Chain(1, Device("KEYBOARD")), loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.Guides["guide"]);

        Assert.Contains(report.Findings, finding => finding.Brief == "Tier depth 3 exceeds limits.max_tiers 2. Use a powered hub.");
        Assert.Contains(report.Findings, finding => finding.Message.StartsWith("limits.max_tiers = 2 (Guide: limits.tiers.max 2 in guides/guide.yaml)", StringComparison.Ordinal));
    }

    // USB 2.0: a 7-bit device address, so at most 127 devices on one controller (base.yaml).
    [Fact]
    public void MoreDevicesThanAddressesOnOneControllerIsUnstable()
    {
        using var db = new TestDatabase();
        var loaded = db.Load();
        IReadOnlyList<UsbNode> full = [Controller("C1", Hub("ROOT", [.. Enumerable.Range(0, 128).Select(i => Device($"D{i}"))]))];

        var report = DiagnosticEvaluator.Evaluate(full, loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.DefaultGuide);

        Assert.Equal(ConnectionStatus.Purple, report.Overall);
        Assert.Contains(report.Findings, finding => finding.SubjectId == "C1"
            && finding.Message == "128 devices are connected to this controller; usb.max_devices_per_controller is 127 (base.yaml).");
    }

    [Fact]
    public void StrictestProfileUsesTheGuide()
    {
        // USB-Helper's tiers.max: 7, 4 and 3.
        IReadOnlyList<ResolvedProfile> profiles = [Db.Platforms["windows-intel"], Db.Platforms["android-tablet-exynos"], Db.Platforms["apple-mobile-a-series"]];

        Assert.Equal(2, StrictestProfile.Of(profiles, UsbHelper));
        Assert.Equal(0, StrictestProfile.Of(profiles));
    }

    // #310: only what a guide counts is judged. Counting the chain, its tier and hub numbers are ignored.
    [Fact]
    public void AGuideThatCountsTheChainIgnoresTiersAndHubs()
    {
        var (windows, guide) = Picked(0, new() { ["count"] = "[chain]", ["tiers.max"] = "2", ["hubs.max"] = "0", ["chain.max"] = "5" });

        var report = Evaluate(windows, 3, Device("KEYBOARD"), guide);

        Assert.Equal(ConnectionStatus.Green, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Overall);
        Assert.Null(report.Tiers.Limit);
        Assert.Null(report.Hubs.Limit);
        Assert.Equal(5L, report.Chain.Limit);
        Assert.Equal([Guide.Chain], report.Counted);
        Assert.DoesNotContain(report.Findings, finding => finding.Message.Contains("max_tiers", StringComparison.Ordinal)
            || finding.Message.Contains("max_hubs", StringComparison.Ordinal));
        Assert.Contains(report.Findings, finding => finding.Message == "limits.max_chain = 5 (Guide: limits.chain.max 5 in guides/guide.yaml).");
    }

    // Counting tiers and hubs, its chain numbers are ignored, and USB-Helper's chain is never a fallback for them.
    [Fact]
    public void AGuideThatCountsTiersAndHubsIgnoresTheChain()
    {
        var (windows, guide) = Picked(0, new() { ["count"] = "[tiers, hubs]", ["chain.max"] = "0", ["chain.recommended"] = "0" });

        var report = Evaluate(windows, 1, Device("KEYBOARD"), guide);

        Assert.Equal(ConnectionStatus.Green, report.Connections["KEYBOARD"]);
        Assert.Null(report.Chain.Limit);
        Assert.DoesNotContain(report.Findings, finding => finding.Message.Contains("chain", StringComparison.Ordinal));
    }

    // The chain is the external hubs in front of a device plus the chip's built-in hub levels; purple above the
    // maximum, orange above the recommendation.
    [Fact]
    public void TheChainCountsTheBuiltInHubLevels()
    {
        var (profile, guide) = Picked(1, new() { ["count"] = "[chain]", ["chain.recommended"] = "2", ["chain.max"] = "3" });

        Assert.Equal(ConnectionStatus.Green, Evaluate(profile, 1, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Orange, Evaluate(profile, 2, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        var over = Evaluate(profile, 3, Device("KEYBOARD"), guide);
        Assert.Equal(ConnectionStatus.Purple, over.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Orange, over.Connections["HUB1"]); // Two hubs and the built-in level in front of it.
        Assert.Contains(over.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message == "Hub chain 4 exceeds limits.max_chain 3.");
        Assert.Contains(Evaluate(profile, 2, Device("KEYBOARD"), guide).Findings, finding => finding.SubjectId == "KEYBOARD"
            && finding.Message == "Hub chain 3 exceeds the recommended limits.recommended_chain 2; limits.max_chain is 3.");
        Assert.Equal(4, over.Chain.Actual);
        Assert.Equal(LimitState.Exceeded, over.Chain.State);
    }

    // Unknown built-in hub levels: the chain is at least what is counted, so within the limit is not known for sure.
    [Fact]
    public void UnknownBuiltInLevelsMakeTheChainAMinimum()
    {
        var (profile, guide) = Picked("null", new() { ["count"] = "[chain]", ["chain.max"] = "3" });

        Assert.Equal(LimitState.Unknown, Evaluate(profile, 2, Device("KEYBOARD"), guide).Chain.State);
        Assert.Equal(LimitState.Exceeded, Evaluate(profile, 4, Device("KEYBOARD"), guide).Chain.State);
    }

    // The chain is judged on each bus: a USB 2 tree can be longer than its USB 3 tree.
    [Fact]
    public void TheChainIsJudgedOnEachBus()
    {
        var (profile, guide) = Picked(0, new() { ["count"] = "[chain]", ["chain.max"] = "2" });
        IReadOnlyList<UsbNode> tree = [Controller("C1", RootWithBuses("ROOT", Hub("A1", Hub("A2", Hub("A3", Device("USB2DEVICE")))), Hub("B1", Device("USB3DEVICE"))))];

        var report = DiagnosticEvaluator.Evaluate(tree, profile, [], externalDisplays: 0, guide: guide);

        Assert.Equal(ConnectionStatus.Purple, report.Connections["USB2DEVICE"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["USB3DEVICE"]);
        Assert.Equal(3, report.Chain.Actual);
        Assert.Equal(LimitState.Exceeded, report.Chain.State);
    }

    // Hubs marked on a port (an active cable or extender) are in the chain of everything behind it.
    [Fact]
    public void MarkedHubsAreInTheChain()
    {
        var (profile, guide) = Picked(0, new() { ["count"] = "[chain]", ["chain.max"] = "2" });
        var marks = new Dictionary<string, CableMark> { ["ROOT|port1"] = CableMark.OfHubs(2) };

        var report = DiagnosticEvaluator.Evaluate(Chain(1, Device("KEYBOARD")), profile, [], externalDisplays: 0, marks: marks, guide: guide);

        Assert.Equal(ConnectionStatus.Purple, report.Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["HUB1"]);
    }

    // Cameras and audio devices: a stricter chain on top of the general one, when the guide counts the chain.
    [Fact]
    public void GuideLimitsCamerasByChain()
    {
        var (windows, guide) = Picked(0, new() { ["count"] = "[chain]", ["chain.max"] = "5", ["camera_audio.chain.recommended"] = "1", ["camera_audio.chain.max"] = "2" });

        Assert.Equal(ConnectionStatus.Green, Evaluate(windows, 1, Camera, guide).Connections["CAMERA"]);
        Assert.Equal(ConnectionStatus.Orange, Evaluate(windows, 2, Camera, guide).Connections["CAMERA"]);
        Assert.Equal(ConnectionStatus.Purple, Evaluate(windows, 3, Camera, guide).Connections["CAMERA"]);
        Assert.Equal(ConnectionStatus.Green, Evaluate(windows, 3, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        Assert.Contains(Evaluate(windows, 3, Camera, guide).Findings, finding => finding.SubjectId == "CAMERA"
            && finding.Message == "Hub chain 3 exceeds limits.camera_audio_max_chain 2 for cameras and audio devices.");
    }

    // Ochno's guide: at most 5 hubs daisy-chained, a laptop's non-root internal hub included, on USB 2 and USB 3 alike.
    // Windows on Intel has no built-in hub level: 5 external hubs fit. An M-series Mac has one: 4 fit.
    [Theory]
    [InlineData("windows-intel", 5)]
    [InlineData("macos-apple-silicon", 4)]
    [InlineData("macos-intel", 5)]
    public void OchnoAllowsFiveHubsInAChain(string profile, int hubs)
    {
        var ochno = Db.Guides["ochno"];

        Assert.Equal([Guide.Chain], ochno.Count);
        Assert.Equal(ConnectionStatus.Green, StatusOf(profile, hubs, Device("KEYBOARD"), ochno));
        Assert.Equal(ConnectionStatus.Purple, StatusOf(profile, hubs + 1, Device("KEYBOARD"), ochno));
        // Ochno gives no separate limit for cameras and audio devices, and no tier or hub limit.
        Assert.Equal(ConnectionStatus.Green, StatusOf(profile, hubs, Camera, ochno));
        var report = Evaluate(Db.Platforms[profile], hubs, Device("KEYBOARD"), ochno);
        Assert.Null(report.Tiers.Limit);
        Assert.Null(report.Hubs.Limit);
        Assert.Equal(5L, report.Chain.Limit);
    }

    // Extron's design guide: at most five cascading hubs, no recommendation (owner 2026-11-18). On Windows Intel
    // (no built-in hub level) 5 hubs in a row are fine, 6 are unstable.
    [Theory]
    [InlineData(5, ConnectionStatus.Green)]
    [InlineData(6, ConnectionStatus.Purple)]
    public void ExtronCountsFiveCascadingHubs(int hubs, ConnectionStatus status) =>
        Assert.Equal(status, StatusOf("windows-intel", hubs, Device("KEYBOARD"), Db.Guides["extron"]));

    // PureLink's whitepaper: tiers only, ideally within four, seven at most; cameras and audio 3–4. A device behind
    // N hubs on Windows Intel is on tier N + 2.
    [Theory]
    [InlineData(2, ConnectionStatus.Green)]
    [InlineData(3, ConnectionStatus.Orange)]
    [InlineData(5, ConnectionStatus.Orange)]
    [InlineData(6, ConnectionStatus.Purple)]
    public void PureLinkCountsTiers(int hubs, ConnectionStatus status) =>
        Assert.Equal(status, StatusOf("windows-intel", hubs, Device("KEYBOARD"), Db.Guides["purelink"]));

    [Theory]
    [InlineData(1, ConnectionStatus.Green)]
    [InlineData(2, ConnectionStatus.Orange)]
    [InlineData(3, ConnectionStatus.Purple)]
    public void PureLinkLimitsCamerasToThreeOrFourTiers(int hubs, ConnectionStatus status) =>
        Assert.Equal(status, StatusOf("windows-intel", hubs, Camera, Db.Guides["purelink"]));

    // Crestron's article 3082: tiers only, never more than 7, no recommendation. Windows Intel: N hubs = tier N + 2.
    [Theory]
    [InlineData(5, ConnectionStatus.Green)]
    [InlineData(6, ConnectionStatus.Purple)]
    public void CrestronCountsSevenTiers(int hubs, ConnectionStatus status) =>
        Assert.Equal(status, StatusOf("windows-intel", hubs, Device("KEYBOARD"), Db.Guides["crestron"]));

    // Desktop is the freest setup, exactly the USB specification: nothing turns orange, only above 7 tiers is
    // unstable, and cameras get no stricter rule (owner 2026-11-18). Windows Intel: N hubs = tier N + 2.
    [Theory]
    [InlineData(5, ConnectionStatus.Green)]
    [InlineData(6, ConnectionStatus.Purple)]
    public void DesktopFollowsTheUsbStandardOnly(int hubs, ConnectionStatus status)
    {
        Assert.Equal(status, StatusOf("windows-intel", hubs, Device("KEYBOARD"), Db.Guides["desktop"]));
        Assert.Equal(status, StatusOf("windows-intel", hubs, Camera, Db.Guides["desktop"]));
    }
}
