using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
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

    // USB-Helper's guide (the owner's file of 2026-11-17): the most hubs in front of a device is the lower of hops and
    // tiers − 2 − the chip's built-in hub levels, for the maximum and the recommendation each. iPhone and iPad (tiers
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
        Assert.Null(report.Hops.Limit);
    }

    // A picked guide's tiers count the chip's built-in hub levels: on an A-series chip (two levels) a device behind any
    // hub is above four tiers. Its empty values are USB-Helper's.
    [Fact]
    public void GuideTiersCountTheBuiltInHubLevels()
    {
        var (aSeries, guide) = Picked(2, new() { ["usb2.tiers.recommended"] = "4", ["usb2.tiers.max"] = "7", ["usb3.tiers.recommended"] = "4", ["usb3.tiers.max"] = "7" });

        Assert.Equal(ConnectionStatus.Green, Evaluate(aSeries, 0, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Orange, Evaluate(aSeries, 1, Device("KEYBOARD"), guide).Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Purple, Evaluate(aSeries, 4, Device("KEYBOARD"), guide).Connections["KEYBOARD"]); // USB-Helper's hops.max 3.
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
        Assert.Contains(report.Findings, finding => finding.Message == "limits.max_hops = 3 (USB-Helper: limits.hops.max 3 in guides/usb-helper.yaml).");
    }

    // A test database with one chip of the given built-in hub levels, USB-Helper's guide with hops 3 / 2, and a picked
    // guide with the values given.
    private static (ResolvedProfile Profile, Guide Guide) Picked(int levels, Dictionary<string, string> values)
    {
        using var db = new TestDatabase().Write(TestDatabase.PlatformFile, TestDatabase.Platform.Replace("built_in_hub_levels: 0", $"built_in_hub_levels: {levels}"));
        db.UsbHelper["hops.max"] = "3";
        db.UsbHelper["hops.recommended"] = "2";
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

        // Cameras and audio: tiers 3 / 5 and hops 2 / 4, so on Windows Intel 1 hub is fine, 2 or 3 are orange, 4 purple.
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
        db.UsbHelper["hops.max"] = "0";
        db.UsbHelper["exceeded_note"] = "\"All note.\"";
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
        db.WriteGuide(new() { ["usb2.hops.max"] = "0", ["usb3.hops.max"] = "0" });
        var loaded = db.Load();

        var report = DiagnosticEvaluator.Evaluate(Chain(1, Device("KEYBOARD")), loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.Guides["guide"]);

        Assert.Contains(report.Findings, finding => finding.Brief == "Hop depth 1 exceeds limits.max_hops 0. Use a powered hub.");
        Assert.Contains(report.Findings, finding => finding.Message.StartsWith("limits.max_hops = 0 (Guide: limits.hops.max 0 in guides/guide.yaml)", StringComparison.Ordinal));
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
