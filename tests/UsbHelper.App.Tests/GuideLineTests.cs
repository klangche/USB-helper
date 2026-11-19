using UsbHelper.Core.Database;

namespace UsbHelper.App.Tests;

/// <summary>The menu line under a guide, generated from its rules (#327).</summary>
public class GuideLineTests
{
    private const string Base = """
        schema_version: 4
        usb: { max_tiers: 7 }
        path_elements:
          root_hub: { found_by: "root hub" }
          computer_hub: { found_by: "computer_hubs" }
          hub: { found_by: "reported hubs" }
          marked_hub: { found_by: "marks" }
          cable_length: { found_by: "marks" }
          usb4_router: { found_by: "not measured yet" }
          device: { found_by: "itself" }
        device_kinds:
          camera: { usb_class: 14 }
          audio: { usb_class: 1 }
          hid: { usb_class: 3 }
        """;

    private const string Measures = """
        measures:
          chain: { counts: [computer_hub, hub, marked_hub] }
          tiers: { counts: [root_hub, computer_hub, hub, marked_hub, cable_length, device] }
          room: { counts: [hub, marked_hub] }
        """;

    // A guide whose rules are the given lines, read in a small v4 database.
    private static (Guide Guide, HardwareDatabase Db) Read(string rules)
    {
        var db = HardwareDatabase.LoadV4(new Dictionary<string, string>
        {
            ["base.yaml"] = Base,
            ["platforms/windows.yaml"] = "name: Windows\nchips:\n  intel: { name: Intel, computer_hubs: 0 }\n  amd: { name: AMD, computer_hubs: null }\n",
            ["guides/usb-helper.yaml"] = $"name: USB Standard\nselectable: true\nproav: 0\n{Measures}\nrules:\n{rules}\n",
        });
        return (db.DefaultGuide, db);
    }

    private static string Line(string rules)
    {
        var (guide, db) = Read(rules);
        return GuideLine.For(guide, db.Platforms);
    }

    [Theory]
    // Ochno: one hard limit on the chain.
    [InlineData("  - { measure: chain, max: 5 }", "hub chain max 5")]
    // Crestron: tiers.
    [InlineData("  - { measure: tiers, max: 7 }", "tiers max 7")]
    // PureLink: a recommendation and a maximum.
    [InlineData("  - { measure: tiers, recommended: 4, max: 7 }", "tiers recommended 4, max 7")]
    // Ochno's room recommendation next to its chain limit.
    [InlineData("  - { measure: chain, max: 5 }\n  - { measure: room, recommended: 4 }", "hub chain max 5; hubs in the room recommended 4")]
    public void EachSampleGuideHasItsLine(string rules, string line)
    {
        Assert.Equal(line, Line(rules));
    }

    [Fact]
    public void ABusThatDiffersIsNamed()
    {
        Assert.Equal("hub chain max 5; USB 2: hub chain max 3", Line("  - { measure: chain, max: 5 }\n  - { measure: chain, bus: usb2, max: 3 }"));
    }

    [Fact]
    public void CameraAndAudioWithTheSameNumbersAreOnePart()
    {
        Assert.Equal("tiers max 7; cameras and audio: tiers recommended 3, max 4",
            Line("  - { measure: tiers, max: 7 }\n  - { measure: tiers, devices: [camera], recommended: 3, max: 4 }\n  - { measure: tiers, devices: [audio], recommended: 3, max: 4 }"));
    }

    [Fact]
    public void CameraAndAudioWithDifferentNumbersAreTwoParts()
    {
        Assert.Equal("cameras: tiers max 4; audio: tiers max 5",
            Line("  - { measure: tiers, devices: [camera], max: 4 }\n  - { measure: tiers, devices: [audio], max: 5 }"));
    }

    [Fact]
    public void NoneSaysNoLimit()
    {
        Assert.Equal("cameras: hub chain no limit", Line("  - { measure: chain, devices: [camera], recommended: none, max: none }"));
    }

    [Fact]
    public void AChipOverrideNamesThePlatformAndChip()
    {
        Assert.Equal("hub chain max 5; Windows AMD: hub chain max 4", Line("  - { measure: chain, max: 5 }\n  - { measure: chain, on: windows/amd, max: 4 }"));
    }

    [Fact]
    public void AGuideThatNeedsANewerAppSaysSo()
    {
        var db = HardwareDatabase.LoadV4(new Dictionary<string, string>
        {
            ["base.yaml"] = Base,
            ["platforms/windows.yaml"] = "name: Windows\nchips:\n  intel: { name: Intel, computer_hubs: 0 }\n",
            ["guides/usb-helper.yaml"] = $"name: USB Standard\nselectable: true\nproav: 0\n{Measures}\nrules:\n  - {{ measure: chain, max: 5 }}\n",
            ["guides/future.yaml"] = "name: Future\nselectable: true\nproav: 1\nrequires: [laser_link]\n",
        });

        Assert.Equal("needs a newer USB-Helper", GuideLine.For(db.Guides["future"]));
        Assert.False(new ViewModels.GuideOption(db.Guides["future"]).IsEnabled);
    }
}
