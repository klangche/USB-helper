using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Topology;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Database;

/// <summary>The v4 format (#327): path elements, guides with measures and quoted rules, requires, computer_hubs.</summary>
public class V4Tests
{
    public const string Base = """
        schema_version: 4
        usb:
          max_tiers: 7
          max_devices_per_controller: 127
        path_elements:
          root_hub: { found_by: "the controller's root hub" }
          computer_hub: { found_by: "computer_hubs in platform and computer files" }
          hub: { found_by: "hubs the operating system reports" }
          marked_hub: { found_by: "the user's mark on a port" }
          cable_length: { found_by: "the user's mark on a port" }
          usb4_router: { found_by: "not measured yet" }
          device: { found_by: "the hub or device itself" }
        device_kinds:
          camera: { usb_class: 14 }
          audio: { usb_class: 1 }
          hid: { usb_class: 3 }
        verification:
          status: verified
        """;

    public const string Windows = """
        name: Windows
        chips:
          intel: { name: Intel, computer_hubs: 0 }
          amd: { name: AMD, computer_hubs: null }
        verification:
          status: verified
        """;

    public const string MacOs = """
        name: macOS
        chips:
          apple-silicon: { name: Apple silicon, computer_hubs: { usb2: 1, usb3: 1 } }
        verification:
          status: verified
        """;

    public const string UsbHelper = """
        name: USB Standard
        selectable: true
        proav: 0
        measures:
          tiers:
            counts: [root_hub, computer_hub, hub, marked_hub, cable_length, device]
        rules:
          - measure: tiers
            max: 7
        """;

    // A manufacturer guide: every rule quoted.
    public const string Room = """
        name: Room
        selectable: true
        proav: 1
        basis: { source: room, quote: "at most four hubs in the room" }
        sources:
          room: { link: "https://example.com/room", title: "Room guide", version: "1.0" }
        measures:
          room:
            counts: [hub, marked_hub]
          chain:
            counts: [computer_hub, hub, marked_hub]
            computer_hub: { from: [computer, chip], unknown: open }
        rules:
          - { measure: room, max: 4, source: room, quote: "at most four hubs in the room" }
          - { measure: chain, max: 5, source: room, quote: "five hubs in a chain" }
          - { measure: chain, bus: usb2, max: 2, source: room, quote: "two on USB 2" }
          - { measure: chain, devices: [audio], max: 1, source: room, quote: "audio on one hub" }
          - { measure: chain, devices: [camera], max: 3, source: room, quote: "cameras on three hubs" }
          - { measure: chain, on: windows/intel, max: 0, recommended: none, source: room, quote: "Intel: no hub" }
        """;

    private static Dictionary<string, string> Files(params (string File, string Yaml)[] more)
    {
        var files = new Dictionary<string, string>
        {
            ["base.yaml"] = Base,
            ["platforms/windows.yaml"] = Windows,
            ["platforms/macos.yaml"] = MacOs,
            ["guides/usb-helper.yaml"] = UsbHelper,
            ["guides/room.yaml"] = Room,
        };
        foreach (var (file, yaml) in more)
        {
            files[file] = yaml;
        }
        return files;
    }

    private static HardwareDatabase Db => HardwareDatabase.LoadV4(Files());

    private static DatabaseError Error(Dictionary<string, string> files) =>
        Assert.Single(Assert.Throws<DatabaseException>(() => HardwareDatabase.LoadV4(files)).Errors);

    private static UsbNode Behind(int hubs, UsbNode device, string prefix = "HUB", int from = 1) =>
        hubs == 0 ? device : Hub($"{prefix}{from}", Behind(hubs - 1, device, prefix, from + 1));

    private static DiagnosticReport Evaluate(string profile, IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, CableMark>? marks = null) =>
        DiagnosticEvaluator.Evaluate(tree, Db.Platforms[profile], [], externalDisplays: 0, marks: marks, guide: Db.Guides["room"]);

    private static IReadOnlyList<UsbNode> Chain(int hubs, UsbNode device) => [Controller("C1", Hub("ROOT", Behind(hubs, device)))];

    [Fact]
    public void AV4DatabaseLoadsWithRulesAndComputerHubs()
    {
        var db = Db;

        Assert.Equal(4, db.Format);
        Assert.Equal(6, db.Guides["room"].Rules.Count);
        Assert.Equal(["room", "chain"], db.Guides["room"].Count);
        Assert.Equal(new Uri("https://example.com/room"), db.Guides["room"].Link);
        Assert.Equal(1L, db.Platforms["macos-apple-silicon"].Find("computer_hubs.usb3")?.Scalar);
        Assert.Same(db, DatabaseFormats.Choose(new Dictionary<int, Func<HardwareDatabase>> { [3] = () => throw new InvalidOperationException(), [4] = () => db }, null, _ => { }).Database);
    }

    [Fact]
    public void AGuideThatRequiresSomethingUnknownIsSkippedAndTheOthersLoad()
    {
        var db = HardwareDatabase.LoadV4(Files(("guides/future.yaml", "name: Future\nselectable: true\nproav: 1\nrequires: [laser_link]\nwhatever: 1\n")));

        Assert.True(db.Guides["future"].NeedsNewerApp);
        Assert.Equal(["laser_link"], db.Guides["future"].Unsupported);
        Assert.Equal(6, db.Guides["room"].Rules.Count);
        var report = DiagnosticEvaluator.Evaluate(Chain(9, Device("KEYBOARD")), db.Platforms["windows-amd"], [], externalDisplays: 0, guide: db.Guides["future"]);
        Assert.Contains(report.Findings, finding => finding.Message == "Future needs a newer USB-Helper (laser_link); nothing is judged by it.");
        Assert.Equal(ConnectionStatus.Grey, report.Connections["KEYBOARD"]);
    }

    [Fact]
    public void ARuleWithoutAQuoteInAManufacturerGuideIsAnError()
    {
        var error = Error(Files(("guides/room.yaml", Room.Replace(", quote: \"five hubs in a chain\"", "", StringComparison.Ordinal))));

        Assert.Equal("guides/room.yaml", error.File);
        Assert.Equal(15, error.Line);
        Assert.Contains("'source' and 'quote' are required in a manufacturer guide", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRuleSelectorsAreAnError()
    {
        var error = Error(Files(("guides/room.yaml", Room + "\n  - { measure: room, max: 3, source: room, quote: \"three\" }\n")));

        Assert.Contains("the same measure, bus, devices and platforms as the rule on line 14", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("measure: room,", "measure: rooms,", "'measure' must be one of")]
    [InlineData("counts: [hub, marked_hub]", "counts: [hub, wormhole]", "unknown path element 'wormhole'")]
    [InlineData("devices: [audio]", "devices: [printer]", "unknown device kind 'printer'")]
    public void UnknownMeasuresElementsAndKindsAreRejectedWithFileAndLine(string from, string to, string message)
    {
        var error = Error(Files(("guides/room.yaml", Room.Replace(from, to, StringComparison.Ordinal))));

        Assert.Equal("guides/room.yaml", error.File);
        Assert.True(error.Line > 0);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AV4ChipMustSetComputerHubs()
    {
        var error = Error(Files(("platforms/windows.yaml", Windows.Replace(", computer_hubs: null", "", StringComparison.Ordinal))));

        Assert.Contains("must set 'computer_hubs'", error.Message, StringComparison.Ordinal);
    }

    // room counts only the hubs in the room: external and marked, not the computer's own.
    [Fact]
    public void TheRoomMeasureCountsHubsAndMarkedHubsOnly()
    {
        var tree = Chain(3, Device("SPEAKER"));

        var within = Evaluate("macos-apple-silicon", tree, new Dictionary<string, CableMark> { ["HUB3|port1"] = CableMark.OfHubs(1) });
        var over = Evaluate("macos-apple-silicon", tree, new Dictionary<string, CableMark> { ["HUB3|port1"] = CableMark.OfHubs(2) });

        Assert.DoesNotContain(within.Findings, finding => finding.Message.StartsWith("Room: room", StringComparison.Ordinal));
        var room = Assert.Single(over.Findings, finding => finding.Message.StartsWith("Room: room", StringComparison.Ordinal));
        Assert.Equal("Room: room 5 exceeds the maximum 4. \"at most four hubs in the room\" (Room guide, 1.0)", room.Message);
        Assert.Equal([new CountPart(PathElements.Hub, 3, "detected: HUB1, HUB2, HUB3"), new CountPart(PathElements.MarkedHub, 2, "your mark on Port 1 of HUB3")], room.CountParts);
    }

    // The usb2 rule applies only on USB 2: three hubs exceed it there; on USB 3 only the general chain rule (5) applies.
    [Fact]
    public void ABusRuleAppliesOnlyOnItsBus()
    {
        var tree = new[] { Controller("C1", RootWithBuses("ROOT", Behind(2, Device("USB2DEV"), "TWO"), Behind(2, Device("USB3DEV"), "THREE"))) };

        var report = Evaluate("macos-apple-silicon", tree);

        Assert.Equal(ConnectionStatus.Purple, report.Connections["USB2DEV"]);
        Assert.Equal(ConnectionStatus.Green, report.Connections["USB3DEV"]);
        Assert.Contains(report.Findings, finding => finding.SubjectId == "USB2DEV" && finding.Message.StartsWith("Room: chain 3 exceeds the maximum 2.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAudioRuleAppliesToAudioDevices()
    {
        var audio = Device("SPEAKER") with { DeviceKind = UsbDeviceKind.Audio };

        Assert.Equal(ConnectionStatus.Purple, Evaluate("macos-apple-silicon", Chain(1, audio)).Connections["SPEAKER"]);
        Assert.Equal(ConnectionStatus.Green, Evaluate("macos-apple-silicon", Chain(1, Device("KEYBOARD"))).Connections["KEYBOARD"]);
    }

    // A webcam with a microphone is a camera and an audio device: both rules apply, and the stricter (audio) wins.
    [Fact]
    public void AWebcamWithAMicrophoneIsJudgedAsBoth()
    {
        var webcam = Device("WEBCAM") with { DeviceKind = UsbDeviceKind.CameraAndAudio };

        var report = Evaluate("macos-apple-silicon", Chain(2, webcam));

        Assert.Equal(ConnectionStatus.Purple, report.Connections["WEBCAM"]);
        Assert.Contains(report.Findings, finding => finding.SubjectId == "WEBCAM" && finding.Message.Contains("\"audio on one hub\"", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Findings, finding => finding.SubjectId == "WEBCAM" && finding.Message.Contains("\"cameras on three hubs\"", StringComparison.Ordinal));
        Assert.Contains(Evaluate("macos-apple-silicon", Chain(3, webcam)).Findings,
            finding => finding.SubjectId == "WEBCAM" && finding.Message.Contains("\"cameras on three hubs\"", StringComparison.Ordinal));
    }

    // on: windows/intel applies on Windows Intel only. Windows AMD's computer_hubs is unknown, so its chain is open. The
    // bus is not reported here, so the USB 2 rule applies as well: the strictest result wins.
    [Fact]
    public void AnOnRuleAppliesOnItsPlatformAndChipOnly()
    {
        Assert.Equal(ConnectionStatus.Purple, Evaluate("windows-intel", Chain(1, Device("KEYBOARD"))).Connections["KEYBOARD"]);
        Assert.Equal(ConnectionStatus.Grey, Evaluate("windows-amd", Chain(1, Device("KEYBOARD"))).Connections["KEYBOARD"]);
        var over = Evaluate("windows-amd", Chain(6, Device("KEYBOARD")));
        var finding = Assert.Single(over.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message.Contains("the maximum 5.", StringComparison.Ordinal));
        Assert.StartsWith("Room: chain at least 6 exceeds the maximum 5.", finding.Message, StringComparison.Ordinal);
        Assert.Equal(new CountPart(PathElements.ComputerHub, null, "platforms/windows.yaml amd: unknown"), finding.CountParts[0]);
    }

    // A value from evidence the measure does not accept is unknown, and the part says why.
    [Fact]
    public void EvidenceTheMeasureDoesNotAcceptIsUnknown()
    {
        var db = HardwareDatabase.LoadV4(Files(("guides/room.yaml", Room.Replace("from: [computer, chip]", "from: [computer]", StringComparison.Ordinal))));

        var report = DiagnosticEvaluator.Evaluate(Chain(6, Device("KEYBOARD")), db.Platforms["macos-apple-silicon"], [], externalDisplays: 0, guide: db.Guides["room"]);

        var finding = Assert.Single(report.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message.Contains("the maximum 5.", StringComparison.Ordinal));
        Assert.Equal(new CountPart(PathElements.ComputerHub, null, "platforms/macos.yaml: not evidence Room accepts"), finding.CountParts[0]);
    }

    // own: true marks the owner's own numbers: a ProAV guide without quotes.
    [Fact]
    public void AnOwnGuideNeedsNoQuotes()
    {
        var db = HardwareDatabase.LoadV4(Files(("guides/mine.yaml", "name: Mine\nselectable: true\nproav: 1\nown: true\nmeasures:\n  tiers: { counts: [root_hub, hub, device] }\nrules:\n  - { measure: tiers, max: 6 }\n")));

        Assert.Single(db.Guides["mine"].Rules);
    }

    // #276: a kind selected by hand is judged like a reported one: an unknown device on tier 4 is within "tiers max 7", and
    // the same device selected as a camera is over the camera's "max 3"; back to unknown, it is within again.
    [Fact]
    public void AKindSelectedByHandIsJudgedByTheStricterRule()
    {
        var guide = "name: Cams\nselectable: true\nproav: 0\nmeasures:\n  tiers: { counts: [root_hub, hub, device] }\n"
            + "rules:\n  - { measure: tiers, max: 7 }\n  - { measure: tiers, devices: [camera], max: 3 }\n";
        var db = HardwareDatabase.LoadV4(Files(("guides/cams.yaml", guide)));
        ConnectionStatus With(UsbNode camera) => DiagnosticEvaluator.Evaluate(Chain(2, camera), db.Platforms["windows-intel"], [],
            externalDisplays: 0, guide: db.Guides["cams"]).Connections["CAM"];
        var unknown = Device("CAM") with { DeviceKind = UsbDeviceKind.Unknown };

        Assert.Equal(ConnectionStatus.Green, With(unknown));
        Assert.NotEqual(ConnectionStatus.Green, With(unknown with { DeviceKind = UsbDeviceKind.Camera, KindByUser = true }));
        Assert.Equal(ConnectionStatus.Green, With(unknown));
    }

    // usb4_router is not measured yet (#332): a measure that counts it is "at least", never 0.
    [Fact]
    public void AMeasureThatCountsUsb4RoutersIsAtLeast()
    {
        var routers = "name: Routers\nselectable: true\nproav: 0\nmeasures:\n  routers: { counts: [usb4_router, hub] }\nrules:\n  - { measure: routers, max: 5 }\n";
        var db = HardwareDatabase.LoadV4(Files(("guides/routers.yaml", routers)));
        DiagnosticReport With(int hubs) =>
            DiagnosticEvaluator.Evaluate(Chain(hubs, Device("KEYBOARD")), db.Platforms["windows-intel"], [], externalDisplays: 0, guide: db.Guides["routers"]);

        Assert.Equal(ConnectionStatus.Grey, With(5).Connections["KEYBOARD"]);
        var over = Assert.Single(With(6).Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Kind == FactKind.PredictedRisk);
        Assert.StartsWith("Routers: routers at least 6 exceeds the maximum 5.", over.Message, StringComparison.Ordinal);
        Assert.Contains(new CountPart(PathElements.Usb4Router, null, "not measured yet (#267)"), over.CountParts);
    }

    // Routers read on the USB4 link (#332): judged only by a measure that counts them; the hubs still count only as hubs.
    [Fact]
    public void DetectedUsb4RoutersAreJudgedOnlyWhereAMeasureCountsThem()
    {
        var routers = "name: Routers\nselectable: true\nproav: 0\nmeasures:\n  routers: { counts: [usb4_router] }\n  chain: { counts: [hub] }\n"
            + "rules:\n  - { measure: routers, max: 5 }\n  - { measure: chain, max: 2 }\n";
        var db = HardwareDatabase.LoadV4(Files(("guides/routers.yaml", routers)));
        DiagnosticReport With(int depth) => DiagnosticEvaluator.Evaluate(
            [Chain(2, Device("KEYBOARD"))[0] with { Usb4Routers = depth, Usb4Link = "thunderboltusb4_bus_0: PS9010" }],
            db.Platforms["macos-apple-silicon"], [], externalDisplays: 0, guide: db.Guides["routers"]);

        // A dock (one router) with its two hubs: within both limits, the hubs not counted as routers or the other way.
        Assert.Equal(ConnectionStatus.Green, With(1).Connections["KEYBOARD"]);
        var over = Assert.Single(With(6).Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Kind == FactKind.PredictedRisk);
        Assert.StartsWith("Routers: routers 6 exceeds the maximum 5.", over.Message, StringComparison.Ordinal);
        Assert.Contains(new CountPart(PathElements.Usb4Router, 6, "detected: thunderboltusb4_bus_0: PS9010"), over.CountParts);
        Assert.DoesNotContain(over.CountParts, part => part.Element == PathElements.Hub);
    }

    // A guide that accepts hubs the system reports inside the computer (#330).
    private const string Detected = "name: Detected\nselectable: true\nproav: 0\nmeasures:\n  chain:\n    counts: [computer_hub, hub, marked_hub]\n"
        + "    computer_hub: { from: [detected, computer, chip], unknown: open }\nrules:\n  - { measure: chain, max: 5 }\n";

    private static CountPart ComputerHubOf(string profile, IReadOnlyList<UsbNode> tree, string id)
    {
        var db = HardwareDatabase.LoadV4(Files(("guides/detected.yaml", Detected)));
        var guide = db.Guides["detected"];
        var path = TopologyCounts.Paths(tree)[id];
        return RuleJudge.Parts(guide.MeasureDefinitions["chain"], guide, db.Platforms[profile], path, null)[0];
    }

    // Windows: an internal hub on a root port that is not user-connectable, with an external port behind it.
    private static IReadOnlyList<UsbNode> InternalHubTree => [Controller("C1",
        Hub("ROOT", Hub("INTERNAL", Device("KEYBOARD"), Device("WEBCAM")).Connectable(true, false), Device("MOUSE")).Connectable(false, true))];

    [Fact]
    public void AnInternalHubInFrontOfAnExternalPortIsADetectedComputerHub()
    {
        Assert.Equal(new CountPart(PathElements.ComputerHub, 1, "detected: INTERNAL"), ComputerHubOf("windows-intel", InternalHubTree, "KEYBOARD"));
        // The detected hub takes the place of the database value: an M-series Mac's 1 is not added to it.
        Assert.Equal(new CountPart(PathElements.ComputerHub, 1, "detected: INTERNAL"), ComputerHubOf("macos-apple-silicon", InternalHubTree, "KEYBOARD"));
        // It is a computer hub, not an external hub.
        Assert.Empty(TopologyCounts.Paths(InternalHubTree)["KEYBOARD"].Hubs);
    }

    // macOS (#330): an M1 Max controller has tier limit 6 and shows no internal hub; the hidden hub is counted once, as
    // detected, in place of the chip's value of 1, not as 2.
    private static IReadOnlyList<UsbNode> LimitedControllerTree => [Controller("C1", Hub("ROOT", Device("KEYBOARD"))) with { Name = "AppleT6000USBXHCI", TierLimit = 6 }];

    [Fact]
    public void AControllerTierLimitBelowSevenIsADetectedComputerHubCountedOnce()
    {
        Assert.Equal(new CountPart(PathElements.ComputerHub, 1, "detected: AppleT6000USBXHCI tier limit 6"),
            ComputerHubOf("macos-apple-silicon", LimitedControllerTree, "KEYBOARD"));
        Assert.Empty(TopologyCounts.Paths(LimitedControllerTree)["KEYBOARD"].Hubs);
        // Limit 7, or none: nothing hidden, so the database value applies.
        Assert.Empty(TopologyCounts.Paths([LimitedControllerTree[0] with { TierLimit = 7 }])["KEYBOARD"].ComputerHubs);
        Assert.Empty(TopologyCounts.Paths([LimitedControllerTree[0] with { TierLimit = null }])["KEYBOARD"].ComputerHubs);
    }

    // An internal hub that serves only a built-in webcam, next to an external port on the root hub.
    private static IReadOnlyList<UsbNode> WebcamOnlyTree => [Controller("C1",
        Hub("ROOT", Hub("INTERNAL", Device("WEBCAM")).Connectable(false), Device("KEYBOARD")).Connectable(false, true))];

    [Fact]
    public void AnInternalHubThatServesOnlyAWebcamIsNotADetectedComputerHub()
    {
        Assert.Empty(TopologyCounts.Paths(WebcamOnlyTree)["WEBCAM"].ComputerHubs);
        Assert.Empty(TopologyCounts.Paths(WebcamOnlyTree)["KEYBOARD"].ComputerHubs);
        Assert.Equal(new CountPart(PathElements.ComputerHub, 0, "platforms/windows.yaml intel"), ComputerHubOf("windows-intel", WebcamOnlyTree, "WEBCAM"));
        Assert.Equal(new CountPart(PathElements.ComputerHub, 0, "platforms/windows.yaml intel"), ComputerHubOf("windows-intel", WebcamOnlyTree, "KEYBOARD"));
    }

    // A built-in device behind it (a webcam on an internal port), and a device on the root's own port, get no detected hub.
    [Fact]
    public void AnInternalHubWithoutAnExternalPortBehindItIsNotCounted()
    {
        Assert.Equal(new CountPart(PathElements.ComputerHub, 0, "platforms/windows.yaml intel"), ComputerHubOf("windows-intel", InternalHubTree, "WEBCAM"));
        Assert.Equal(new CountPart(PathElements.ComputerHub, 0, "platforms/windows.yaml intel"), ComputerHubOf("windows-intel", InternalHubTree, "MOUSE"));
    }

    // A guide's number for an unknown computer_hub is used, with the guide as its origin.
    [Fact]
    public void AnAssumedComputerHubComesFromTheGuide()
    {
        var db = HardwareDatabase.LoadV4(Files(("guides/room.yaml", Room.Replace("unknown: open", "unknown: 1", StringComparison.Ordinal))));

        var report = DiagnosticEvaluator.Evaluate(Chain(5, Device("KEYBOARD")), db.Platforms["windows-amd"], [], externalDisplays: 0, guide: db.Guides["room"]);

        var finding = Assert.Single(report.Findings, finding => finding.SubjectId == "KEYBOARD" && finding.Message.Contains("the maximum 5.", StringComparison.Ordinal));
        Assert.StartsWith("Room: chain 6 exceeds the maximum 5.", finding.Message, StringComparison.Ordinal);
        Assert.Equal(new CountPart(PathElements.ComputerHub, 1, "guides/room.yaml:12 when unknown"), finding.CountParts[0]);
    }
}
