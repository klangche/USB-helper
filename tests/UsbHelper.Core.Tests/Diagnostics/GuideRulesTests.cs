using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

/// <summary>The rules of #284 on the repository's database: platform facts, the USB maximum and the PureLink guide.</summary>
public class GuideRulesTests
{
    private static readonly HardwareDatabase Db = HardwareDatabase.Load(RealDatabaseTests.DatabaseFolder());

    private static Guide PureLink => Db.Guides["purelink"];

    // A device with the given number of hubs in front of it, on the computer's own port.
    private static IReadOnlyList<UsbNode> Chain(int hubs, UsbNode device) => [Controller("C1", Hub("ROOT", Behind(hubs, device)))];

    private static UsbNode Behind(int hubs, UsbNode device) => hubs == 0 ? device : Hub($"HUB{hubs}", Behind(hubs - 1, device));

    private static ConnectionStatus StatusOf(string profile, int hubs, UsbNode device, Guide? guide) =>
        DiagnosticEvaluator.Evaluate(Chain(hubs, device), Db.Platforms[profile], [], externalDisplays: 0, guide: guide).Connections[device.Id];

    // Today's merged values before #284: purple above the maximum, orange above the recommendation.
    private static ConnectionStatus Today(int hubs, int max, int recommended) =>
        hubs > max ? ConnectionStatus.Purple : hubs > recommended ? ConnectionStatus.Orange : ConnectionStatus.Green;

    // #285: with PureLink, every device gets the same colour as with today's merged values (Windows 5 / 2, Apple silicon
    // 4 / 1, A-series 3 / 1 after the floor of 1 hub).
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
    public void PureLinkGivesTodaysColours(string profile, int max, int recommended)
    {
        for (var hubs = 0; hubs <= 7; hubs++)
        {
            Assert.Equal(Today(hubs, max, recommended), StatusOf(profile, hubs, Device("KEYBOARD"), PureLink));
        }
    }

    // The MacBook Neo is an A-series Mac: the same colours as macos-a-series.
    [Fact]
    public void MacBookNeoGetsTheASeriesColours()
    {
        var neo = Db.Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")];

        for (var hubs = 0; hubs <= 5; hubs++)
        {
            var report = DiagnosticEvaluator.Evaluate(Chain(hubs, Device("KEYBOARD")), neo, [], externalDisplays: 0, guide: PureLink);
            Assert.Equal(Today(hubs, 3, 1), report.Connections["KEYBOARD"]);
        }
    }

    // #284: with no guide, only the USB maximum applies, so nothing is potentially unstable by depth.
    [Theory]
    [InlineData("windows-intel", 5)]
    [InlineData("macos-apple-silicon", 4)]
    [InlineData("macos-a-series", 3)]
    public void WithoutAGuideOnlyTheMaximumApplies(string profile, int max)
    {
        for (var hubs = 0; hubs <= 7; hubs++)
        {
            Assert.Equal(hubs > max ? ConnectionStatus.Purple : ConnectionStatus.Green, StatusOf(profile, hubs, Device("KEYBOARD"), null));
        }
    }

    [Fact]
    public void WithoutAGuideNoRecommendationIsNamed()
    {
        var report = DiagnosticEvaluator.Evaluate(Chain(3, Device("KEYBOARD")), Db.Platforms["windows-intel"], [], externalDisplays: 0);

        Assert.Null(report.RecommendedHops!.Limit);
        Assert.DoesNotContain(report.Findings, finding => finding.Message.Contains("recommended", StringComparison.Ordinal));
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.VerifiedDatabaseFact
            && finding.Message == "limits.max_hops = 5 (usb.max_tiers 7 in base.yaml; built_in_hub_levels 0 in platforms/windows.yaml).");
    }

    // The findings name the guide and every file a number comes from.
    [Fact]
    public void FindingsNameTheGuideAndTheFiles()
    {
        var report = DiagnosticEvaluator.Evaluate(Chain(2, Device("KEYBOARD")), Db.Platforms["macos-a-series"], [], externalDisplays: 0, guide: PureLink);

        Assert.Contains(report.Findings, finding => finding.Message == "limits.recommended_hops = 1 (PureLink: recommended_tiers 4 in guides/purelink.yaml; "
            + "built_in_hub_levels 2 in platforms/macos.yaml; min_recommended_hubs 1 in guides/purelink.yaml).");
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.PredictedRisk && finding.SubjectId == "KEYBOARD"
            && finding.Message == "Hop depth 2 exceeds the recommended limits.recommended_hops 1; limits.max_hops is 3.");
    }

    // #275 and #284: a camera or audio device gets PureLink's stricter recommendation (3 tiers, never below 1 hub); the
    // maximum stays the USB maximum. Other devices at the same depth keep the general recommendation.
    [Theory]
    [InlineData(UsbDeviceKind.Camera)]
    [InlineData(UsbDeviceKind.Audio)]
    [InlineData(UsbDeviceKind.CameraAndAudio)]
    public void CamerasAndAudioDevicesGetTheStricterRecommendation(UsbDeviceKind kind)
    {
        var camera = Device("CAMERA") with { DeviceKind = kind };

        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 1, camera, PureLink));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 2, camera, PureLink));
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 2, Device("KEYBOARD") with { DeviceKind = UsbDeviceKind.Neither }, PureLink));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("windows-intel", 5, camera, PureLink));
        Assert.Equal(ConnectionStatus.Purple, StatusOf("windows-intel", 6, camera, PureLink));
        // Apple silicon and A-series: 3 tiers would leave no hub, so the floor of 1 hub applies.
        Assert.Equal(ConnectionStatus.Green, StatusOf("macos-apple-silicon", 1, camera, PureLink));
        Assert.Equal(ConnectionStatus.Orange, StatusOf("macos-apple-silicon", 2, camera, PureLink));
        Assert.Equal(ConnectionStatus.Green, StatusOf("macos-a-series", 1, camera, PureLink));
    }

    [Fact]
    public void CameraFindingSaysWhyItIsJudgedMoreStrictly()
    {
        var camera = Device("CAMERA") with { DeviceKind = UsbDeviceKind.Camera };

        var report = DiagnosticEvaluator.Evaluate(Chain(2, camera), Db.Platforms["windows-intel"], [], externalDisplays: 0, guide: PureLink);

        Assert.Contains(new Finding(FactKind.PredictedRisk, "CAMERA", "CAMERA",
            "Hop depth 2 exceeds the recommended limits.camera_audio_recommended_hops 1 for cameras and audio devices; limits.max_hops is 5."), report.Findings);
        Assert.Contains(report.Findings, finding => finding.Message == "limits.camera_audio_recommended_hops = 1 (PureLink: camera_audio_recommended_tiers 3 "
            + "in guides/purelink.yaml; built_in_hub_levels 0 in platforms/windows.yaml; min_recommended_hubs 1 in guides/purelink.yaml).");
    }

    // A device whose kind is unknown is never judged as a camera.
    [Fact]
    public void UnknownKindIsJudgedByTheGeneralRecommendation()
    {
        Assert.Equal(ConnectionStatus.Green, StatusOf("windows-intel", 2, Device("THING") with { DeviceKind = UsbDeviceKind.Unknown }, PureLink));
    }

    // A guide without a camera and audio recommendation: cameras get the general one, and the log says so.
    [Fact]
    public void GuideWithoutACameraRecommendationFallsBackToTheGeneralOne()
    {
        using var db = new TestDatabase().WriteGuide("  recommended_tiers: 3");
        var loaded = db.Load();
        var camera = Device("CAMERA") with { DeviceKind = UsbDeviceKind.Camera };

        var report = DiagnosticEvaluator.Evaluate(Chain(2, camera), loaded.Platforms["test-platform"], [], externalDisplays: 0, guide: loaded.Guides["guide"]);

        Assert.Equal(ConnectionStatus.Orange, report.Connections["CAMERA"]);
        Assert.Contains(report.Findings, finding => finding.Kind == FactKind.PredictedRisk
            && finding.Message.StartsWith("Hop depth 2 exceeds the recommended limits.recommended_hops 1", StringComparison.Ordinal));
        Assert.Contains(report.Findings, finding => finding.Message
            == "Guide sets no camera_audio_recommended_tiers (guides/guide.yaml), so cameras and audio devices are judged by limits.recommended_hops.");
    }

    // A guide may set a stricter maximum than the USB specification.
    [Fact]
    public void GuideMaximumIsStricterThanTheUsbMaximum()
    {
        using var db = new TestDatabase().WriteGuide("  max_tiers: 5");
        var loaded = db.Load();

        var limit = DepthLimits.MaxHops(loaded.Platforms["test-platform"], loaded.Guides["guide"]);

        Assert.Equal(3L, limit.Value);
        Assert.Equal("Guide: max_tiers 5 in guides/guide.yaml; built_in_hub_levels 0 in platforms/test.yaml", limit.Source);
    }

    // With several profiles checked, the strictest is worked out with the selected guide.
    [Fact]
    public void StrictestProfileUsesTheGuide()
    {
        IReadOnlyList<ResolvedProfile> profiles = [Db.Platforms["windows-intel"], Db.Platforms["macos-a-series"], Db.Platforms["macos-apple-silicon"]];

        Assert.Equal(1, StrictestProfile.Of(profiles, PureLink));
        Assert.Equal(1, StrictestProfile.Of(profiles));
    }
}
