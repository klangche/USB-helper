using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

/// <summary>The guide rules of #285 on the repository's database: USB-Helper's guide by default, PureLink when picked.</summary>
public class GuideRulesTests
{
    private static readonly HardwareDatabase Db = HardwareDatabase.Load(RealDatabaseTests.DatabaseFolder());

    private static Guide UsbHelper => Db.DefaultGuide;

    private static Guide PureLink => Db.Guides["purelink"];

    // A device with the given number of hubs in front of it, on the computer's own port.
    private static IReadOnlyList<UsbNode> Chain(int hubs, UsbNode device) => [Controller("C1", Hub("ROOT", Behind(hubs, device)))];

    private static UsbNode Behind(int hubs, UsbNode device) => hubs == 0 ? device : Hub($"HUB{hubs}", Behind(hubs - 1, device));

    private static DiagnosticReport Evaluate(ResolvedProfile profile, int hubs, UsbNode device, Guide? guide) =>
        DiagnosticEvaluator.Evaluate(Chain(hubs, device), profile, [], externalDisplays: 0, guide: guide);

    private static ConnectionStatus StatusOf(string profile, int hubs, UsbNode device, Guide? guide) =>
        Evaluate(Db.Platforms[profile], hubs, device, guide).Connections[device.Id];

    private static UsbNode Camera => Device("CAMERA") with { DeviceKind = UsbDeviceKind.Camera };

    // Today's merged values before #284: purple above the maximum, orange above the recommendation.
    private static ConnectionStatus Today(int hubs, int max, int recommended) =>
        hubs > max ? ConnectionStatus.Purple : hubs > recommended ? ConnectionStatus.Orange : ConnectionStatus.Green;

    // USB-Helper's guide (the default) keeps today's colours: Windows 5 / 2, M-series 4 / 1, A-series 3 / 1 (the
    // recommendation never below 1 hub).
    [Theory]
    [InlineData("windows-intel", 5, 2)]
    [InlineData("windows-amd", 5, 2)]
    [InlineData("windows-arm", 5, 2)]
    [InlineData("macos-intel", 5, 2)]
    [InlineData("macos-apple-silicon", 4, 1)]
    [InlineData("macos-a-series", 3, 1)]
    [InlineData("apple-mobile-a-series", 3, 1)]
    [InlineData("linux-x64", 5, 2)]
    [InlineData("linux-arm", 5, 2)]
    [InlineData("android-phone-qualcomm", 5, 2)]
    [InlineData("android-tablet-exynos", 4, 1)]
    public void UsbHelperGivesTodaysColours(string profile, int max, int recommended)
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
        Assert.Null(report.Hops.Limit);
    }

    // PureLink, strict: four tiers recommended, built-in hub levels counted, so on an A-series chip a device behind any
    // hub is potentially unstable (owner decision 2026-11-17). Its empty values are USB-Helper's.
    [Fact]
    public void PureLinkCountsTiersWithTheBuiltInHubLevels()
    {
        Assert.Equal(ConnectionStatus.Green, StatusOf("macos-a-series", 0, Device("KEYBOARD"), PureLink));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("macos-a-series", 1, Device("KEYBOARD"), PureLink));
        Assert.Equal(ConnectionStatus.Purple, StatusOf("macos-a-series", 4, Device("KEYBOARD"), PureLink)); // USB-Helper's hops.max 3.
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 2, Device("KEYBOARD"), PureLink));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 3, Device("KEYBOARD"), PureLink));
    }

    // Findings and the log name the guide and the file each number comes from.
    [Fact]
    public void FindingsNameTheGuideAndTheFile()
    {
        var report = Evaluate(Db.Platforms["macos-a-series"], 1, Device("KEYBOARD"), PureLink);

        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.PredictedRisk && finding.SubjectId == "KEYBOARD"
            && finding.Message == "Tier depth 5 exceeds the recommended limits.recommended_tiers 4; limits.max_tiers is 7.");
        Assert.Contains(report.Findings, finding => finding.Message == "limits.recommended_tiers = 4 (PureLink: tiers.recommended 4 in guides/purelink.yaml).");
        Assert.Contains(report.Findings, finding => finding.Message == "limits.max_hops = 3 (USB-Helper: hops.max 3 in guides/usb-helper.yaml).");
    }

    // #275 and #285: cameras and audio devices get their own limits on top of the general ones.
    [Theory]
    [InlineData(UsbDeviceKind.Camera)]
    [InlineData(UsbDeviceKind.Audio)]
    [InlineData(UsbDeviceKind.CameraAndAudio)]
    public void CamerasAndAudioDevicesGetUsbHelpersStricterRecommendation(UsbDeviceKind kind)
    {
        var device = Device("CAMERA") with { DeviceKind = kind };

        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 1, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 2, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 5, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Purple, StatusOf("windows-intel", 6, device, UsbHelper));
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 2, Device("KEYBOARD") with { DeviceKind = UsbDeviceKind.Neither }, UsbHelper));
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 2, Device("THING") with { DeviceKind = UsbDeviceKind.Unknown }, UsbHelper));
        Assert.Equal(ConnectionStatus.Green, StatusOf("macos-a-series", 1, device, UsbHelper));
    }

    // PureLink: webcams and audio interfaces 3 tiers recommended, 4 at most.
    [Fact]
    public void PureLinkLimitsCamerasToThreeOrFourTiers()
    {
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 1, Camera, PureLink));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 2, Camera, PureLink));
        Assert.Equal(ConnectionStatus.Purple, StatusOf("windows-intel", 3, Camera, PureLink));
        Assert.Contains(Evaluate(Db.Platforms["windows-intel"], 3, Camera, PureLink).Findings, finding => finding.SubjectId == "CAMERA"
            && finding.Message == "Tier depth 5 exceeds limits.camera_audio_max_tiers 4 for cameras and audio devices.");
    }

    // An exceeded limit shows the most specific exceeded_note of the guide that set it: the chip's over the platform's.
    [Fact]
    public void ExceededLimitShowsTheChipsNote()
    {
        using var db = new TestDatabase();
        db.UsbHelper["hops.max"] = "0";
        db.UsbHelper["platforms.test.limits.exceeded_note"] = "\"Platform note.\"";
        db.UsbHelper["platforms.test.chips.platform.limits.exceeded_note"] = "\"Chip note.\"";
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(Chain(1, Device("KEYBOARD")), loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.DefaultGuide);

        Assert.Contains(report.Findings, finding => finding.Brief == "Hop depth 1 exceeds limits.max_hops 0. Chip note.");
    }

    // An empty note falls back to USB-Helper's, like the numbers.
    [Fact]
    public void EmptyNoteFallsBackToUsbHelpers()
    {
        using var db = new TestDatabase();
        db.UsbHelper["exceeded_note"] = "\"Use a powered hub.\"";
        db.WriteGuide(new() { ["hops.max"] = "0" });
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(Chain(1, Device("KEYBOARD")), loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.Guides["guide"]);

        Assert.Contains(report.Findings, finding => finding.Brief == "Hop depth 1 exceeds limits.max_hops 0. Use a powered hub.");
        Assert.Contains(report.Findings, finding => finding.Message.StartsWith("limits.max_hops = 0 (Guide: hops.max 0 in guides/guide.yaml)", StringComparison.Ordinal));
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
        IReadOnlyList<ResolvedProfile> profiles = [Db.Platforms["windows-intel"], Db.Platforms["macos-a-series"], Db.Platforms["macos-apple-silicon"]];

        Assert.Equal(1, StrictestProfile.Of(profiles, UsbHelper));
        Assert.Equal(0, StrictestProfile.Of(profiles));
    }
}
