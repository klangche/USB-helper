using UsbHelper.Core.Database;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.Core.Tests.Database;

/// <summary>Loads the database folder in this repository.</summary>
public class RealDatabaseTests
{
    internal static string DatabaseFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "database");
            if (File.Exists(Path.Combine(candidate, "base.yaml")))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("database/base.yaml not found above the test output folder.");
    }

    [Fact]
    public void RepositoryDatabaseLoads()
    {
        var db = HardwareDatabase.Load(DatabaseFolder());

        Assert.Equal(
            [
                "android-phone-qualcomm", "android-tablet-exynos", "apple-mobile-a-series", "linux-arm", "linux-x64",
                "macos-a-series", "macos-apple-silicon", "macos-intel", "windows-amd", "windows-arm", "windows-intel",
            ],
            db.Platforms.Keys.Order());
        Assert.Equal(["ochno", "purelink", "unisynk"], db.Guides.Keys.Order());
        Assert.Equal("arm64", db.Platforms["macos-apple-silicon"].Find("architecture")!.Scalar);
    }

    // The USB specification (and PureLink): seven tiers on a path.
    [Fact]
    public void BaseHoldsTheUsbMaximum()
    {
        var tiers = HardwareDatabase.Load(DatabaseFolder()).Platforms["windows-intel"].Find("usb.max_tiers")!;

        Assert.Equal(7L, tiers.Scalar);
        Assert.Equal("base.yaml", tiers.File);
    }

    // Owner decisions 2026-11-16 (#260) and 2026-11-17 (#284): the chips' built-in hub levels, carried over from the
    // former platform files' max_hops (5 - max_hops).
    [Theory]
    [InlineData("windows-intel", "windows", "Windows", "Intel", 0)]
    [InlineData("windows-amd", "windows", "Windows", "AMD", 0)]
    [InlineData("windows-arm", "windows", "Windows", "ARM", 0)]
    [InlineData("macos-intel", "macos", "macOS", "Intel", 0)]
    [InlineData("macos-apple-silicon", "macos", "macOS", "M-series", 1)]
    [InlineData("macos-a-series", "macos", "macOS", "A-series", 2)]
    [InlineData("apple-mobile-a-series", "apple-mobile", "iPhone and iPad", "A-series", 2)]
    [InlineData("linux-x64", "linux", "Linux", "x64", 0)]
    [InlineData("linux-arm", "linux", "Linux", "ARM", 0)]
    [InlineData("android-phone-qualcomm", "android", "Android", "Phone (Qualcomm)", 0)]
    [InlineData("android-tablet-exynos", "android", "Android", "Tablet (Exynos)", 1)]
    public void ChipsHaveTheirBuiltInHubLevels(string id, string platform, string platformName, string chipName, long levels)
    {
        var profile = HardwareDatabase.Load(DatabaseFolder()).Platforms[id];

        Assert.Equal(platform, profile.Platform);
        Assert.Equal(platformName, profile.PlatformName);
        Assert.Equal(chipName, profile.ChipName);
        Assert.Equal(levels, profile.Find("built_in_hub_levels")!.Scalar);
        Assert.Equal($"platforms/{platform}.yaml", profile.Find("built_in_hub_levels")!.File);
        Assert.Equal("partial", profile.Find("verification.status")!.Scalar);
        Assert.Equal($"platforms/{platform}.yaml", profile.Find("verification.status")!.File);
        Assert.NotEmpty(profile.Find("sources")!.Items);
    }

    // #284: with PureLink, today's merged values (Windows 5 / 2; Apple silicon 4 / 1; A-series 3 / 1 after the floor of
    // 1 hub); cameras and audio devices one tier stricter, never below 1 hub.
    [Theory]
    [InlineData("windows-intel", 5, 2, 1)]
    [InlineData("windows-amd", 5, 2, 1)]
    [InlineData("windows-arm", 5, 2, 1)]
    [InlineData("macos-intel", 5, 2, 1)]
    [InlineData("macos-apple-silicon", 4, 1, 1)]
    [InlineData("macos-a-series", 3, 1, 1)]
    [InlineData("apple-mobile-a-series", 3, 1, 1)]
    [InlineData("linux-x64", 5, 2, 1)]
    [InlineData("linux-arm", 5, 2, 1)]
    [InlineData("android-phone-qualcomm", 5, 2, 1)]
    [InlineData("android-tablet-exynos", 4, 1, 1)]
    public void PureLinkGivesTodaysHopLimits(string id, long maxHops, long recommendedHops, long cameraAudioHops)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var profile = db.Platforms[id];
        var purelink = db.Guides["purelink"];

        Assert.Equal(maxHops, DepthLimits.MaxHops(profile, purelink).Value);
        Assert.Equal(recommendedHops, DepthLimits.RecommendedHops(profile, purelink).Value);
        Assert.Equal(cameraAudioHops, DepthLimits.CameraAudioRecommendedHops(profile, purelink).Value);
        Assert.True(DepthLimits.MaxHops(profile, purelink).Verified);
        Assert.True(DepthLimits.RecommendedHops(profile, purelink).Verified);
        // Without a guide, only the USB maximum.
        Assert.Equal(maxHops, DepthLimits.MaxHops(profile, null).Value);
        Assert.Null(DepthLimits.RecommendedHops(profile, null).Value);
    }

    [Fact]
    public void PureLinkGuideHasItsLimits()
    {
        var purelink = HardwareDatabase.Load(DatabaseFolder()).Guides["purelink"];

        Assert.Equal("PureLink", purelink.Name);
        Assert.True(purelink.Selectable);
        Assert.Equal(4L, purelink.Find("limits.recommended_tiers")!.Scalar);
        Assert.Equal(3L, purelink.Find("limits.camera_audio_recommended_tiers")!.Scalar);
        Assert.Equal(1L, purelink.Find("limits.min_recommended_hubs")!.Scalar);
        Assert.Null(purelink.Find("limits.max_tiers"));
        Assert.Equal("partial", purelink.Status);
        Assert.Contains(purelink.Find("sources")!.Items, source => ((string)source.Scalar!).StartsWith("PureLink, USB-C whitepaper", StringComparison.Ordinal));
    }

    // Listed, not selectable until filled in (#284).
    [Theory]
    [InlineData("ochno", "Ochno")]
    [InlineData("unisynk", "Unisynk")]
    public void GuidesNotFilledInAreNotSelectable(string id, string name)
    {
        var guide = HardwareDatabase.Load(DatabaseFolder()).Guides[id];

        Assert.Equal(name, guide.Name);
        Assert.False(guide.Selectable);
        Assert.Null(guide.Find("limits"));
    }

    // Owner decision 2026-11-16: MST is a property of the operating system, so every platform file has the field:
    // false on macOS, true on Windows, null (unknown) elsewhere.
    [Theory]
    [InlineData("macos-apple-silicon", false)]
    [InlineData("macos-a-series", false)]
    [InlineData("macos-intel", false)]
    [InlineData("windows-intel", true)]
    [InlineData("windows-amd", true)]
    [InlineData("windows-arm", true)]
    [InlineData("linux-x64", null)]
    [InlineData("linux-arm", null)]
    [InlineData("android-phone-qualcomm", null)]
    [InlineData("android-tablet-exynos", null)]
    [InlineData("apple-mobile-a-series", null)]
    public void EveryPlatformSaysWhetherItSupportsMst(string id, bool? mst)
    {
        var platform = HardwareDatabase.Load(DatabaseFolder()).Platforms[id];

        Assert.Equal(mst, platform.Find("displays.mst")!.Scalar);
    }

    // Lightware: the MacBook Neo, an A-series Mac, uses three of the seven tiers itself, leaving three hubs (#260).
    [Fact]
    public void MacBookNeoIsAnASeriesMac()
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var neo = db.Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")];

        Assert.Equal("MacBook Neo", neo.Find("name")!.Scalar);
        Assert.Equal("macos", neo.Platform);
        Assert.Equal("a-series", neo.Chip);
        Assert.Equal(2L, neo.Find("built_in_hub_levels")!.Scalar);
        Assert.Equal(3L, DepthLimits.MaxHops(neo, null).Value);
        Assert.Equal(1L, DepthLimits.RecommendedHops(neo, db.Guides["purelink"]).Value);
        Assert.Equal(false, neo.Find("ports.usb2.displayport_alt_mode")!.Scalar);
        // Apple: one external display. No MST is the operating system's limit, inherited from the platform file.
        Assert.Equal(1L, neo.Find("displays.max_external")!.Scalar);
        Assert.Equal(false, neo.Find("displays.mst")!.Scalar);
        Assert.Equal("platforms/macos.yaml", neo.Find("displays.mst")!.File);
        Assert.Equal("partial", neo.Find("verification.status")!.Scalar);
        Assert.NotEmpty(neo.Find("sources")!.Items);
    }

    [Fact]
    public void MacBookPro16M1MaxIsSelectedByExactModel()
    {
        var mac = HardwareDatabase.Load(DatabaseFolder()).Computers[ComputerProfileId.From("Apple Inc.", "MacBookPro18,2")];

        Assert.Equal("Apple M1 Max", mac.Find("chip")!.Scalar);
        Assert.Equal("apple-silicon", mac.Chip);
        Assert.Equal(4L, mac.Find("displays.max_external")!.Scalar);
        Assert.Equal(4L, mac.Find("ports.thunderbolt4.thunderbolt")!.Scalar);
        Assert.Equal(1L, mac.Find("built_in_hub_levels")!.Scalar);
        Assert.Equal(4L, DepthLimits.MaxHops(mac, null).Value);
        Assert.NotEmpty(mac.Find("sources")!.Items);
    }

    // Exported by the owner from USB-helper 0.5.0-beta.4 (#74); its facts come from Windows on AMD. The ports are from
    // ASUS's specification page and stay unverified until confirmed on the machine.
    [Fact]
    public void AsusRogStrixG713RWIsWindowsOnAmd()
    {
        var asus = HardwareDatabase.Load(DatabaseFolder()).Computers[ComputerProfileId.From("ASUSTeK COMPUTER INC.", "ROG Strix G713RW_G713RW")];

        Assert.Equal("ROG Strix G713RW", asus.Find("name")!.Scalar);
        Assert.Equal("unverified", asus.Find("verification.status")!.Scalar);
        Assert.Equal("windows-amd", $"{asus.Platform}-{asus.Chip}");
        Assert.Equal(0L, asus.Find("built_in_hub_levels")!.Scalar);
        Assert.Equal("platforms/windows.yaml", asus.Find("built_in_hub_levels")!.File);
        Assert.Equal(5L, DepthLimits.MaxHops(asus, null).Value);
        Assert.Equal(true, asus.Find("ports.usbc_display.displayport_alt_mode")!.Scalar);
        Assert.Null(asus.Find("ports.usbc_data.displayport_alt_mode"));
        Assert.Equal(true, asus.Find("ports.hdmi.hdmi")!.Scalar);
        Assert.NotEmpty(asus.Find("sources")!.Items);
    }

    [Fact]
    public void ComputerTemplateIsValidWhenCopied()
    {
        var template = File.ReadAllText(Path.Combine(DatabaseFolder(), "computers", ".template.yaml"));
        using var db = new TestDatabase().Write("computers/copy.yaml",
            template.Replace("platform: platform-file", "platform: test").Replace("chip_family: chip", "chip_family: platform"));

        Assert.Equal([ComputerProfileId.From("Manufacturer", "Exact model name")], db.Load().Computers.Keys);
    }
}
