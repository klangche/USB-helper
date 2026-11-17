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
        Assert.Equal(["ochno", "purelink", "unisynk", "usb-helper"], db.Guides.Keys.Order());
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

    // USB-Helper's guide, the default (to be confirmed by the owner): today's merged values as hops, the recommendation
    // never below 1 hub; cameras and audio devices 1 hub recommended, the general maximum; tiers at most 7.
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
    public void UsbHelperGivesTodaysHopLimits(string id, long maxHops, long recommendedHops)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var profile = db.Platforms[id];

        Assert.Equal(maxHops, GuideLimits.Of(profile, db.DefaultGuide, "hops.max").Value);
        Assert.Equal(recommendedHops, GuideLimits.Of(profile, db.DefaultGuide, "hops.recommended").Value);
        Assert.Equal(1L, GuideLimits.Of(profile, db.DefaultGuide, "camera_audio.hops.recommended").Value);
        Assert.Equal(maxHops, GuideLimits.Of(profile, db.DefaultGuide, "camera_audio.hops.max").Value);
        Assert.Equal(7L, GuideLimits.Of(profile, db.DefaultGuide, "tiers.max").Value);
        Assert.Null(GuideLimits.Of(profile, db.DefaultGuide, "hubs.max").Value);
        Assert.True(GuideLimits.Of(profile, db.DefaultGuide, "hops.max").Verified);
    }

    // Owner decision 2026-11-17: PureLink, Ochno and Unisynk are blank forms to send out; until they come back filled
    // in, every value resolves to USB-Helper's.
    [Theory]
    [InlineData("purelink", "PureLink", true)]
    [InlineData("ochno", "Ochno", false)]
    [InlineData("unisynk", "Unisynk", false)]
    public void BlankGuidesFallBackToUsbHelper(string id, string name, bool selectable)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var guide = db.Guides[id];
        string[] paths = ["hubs", "tiers", "hops", "camera_audio.tiers", "camera_audio.hops"];

        Assert.Equal(name, guide.Name);
        Assert.Equal(selectable, guide.Selectable);
        Assert.Same(db.DefaultGuide, guide.Fallback);
        foreach (var profile in db.Platforms.Values)
        {
            foreach (var path in paths.SelectMany(kind => new[] { $"{kind}.recommended", $"{kind}.max" }))
            {
                Assert.Same(db.DefaultGuide, guide.Limit(profile.Platform, profile.Chip, path)!.Guide);
            }
            Assert.Same(db.DefaultGuide, guide.Mst(profile.Platform, profile.Chip)!.Guide);
            Assert.Null(guide.ExceededNote(profile.Platform, profile.Chip));
        }
    }

    // Listed, not selectable until filled in (#284): every value empty.
    [Theory]
    [InlineData("ochno", "Ochno")]
    [InlineData("unisynk", "Unisynk")]
    public void GuidesNotFilledInAreNotSelectable(string id, string name)
    {
        var guide = HardwareDatabase.Load(DatabaseFolder()).Guides[id];

        Assert.Equal(name, guide.Name);
        Assert.False(guide.Selectable);
        Assert.Equal(ValueKind.Null, guide.Find("limits.tiers.max")!.Kind);
        Assert.Equal(ValueKind.Null, guide.Find("platforms.macos.chips.a-series.limits.hops.max")!.Kind);
    }

    // Every guide file has the same shape: the same lines, only the values differ.
    [Fact]
    public void EveryGuideFileHasTheSameShape()
    {
        static IEnumerable<string> Shape(string file) => File.ReadAllLines(Path.Combine(DatabaseFolder(), "guides", file))
            .TakeWhile(line => !line.StartsWith("notes:", StringComparison.Ordinal))
            .Where(line => line.StartsWith(' ') || line.StartsWith("limits:", StringComparison.Ordinal) || line.StartsWith("platforms:", StringComparison.Ordinal))
            .Select(line => line.Split(':')[0]);

        foreach (var file in new[] { "purelink.yaml", "ochno.yaml", "unisynk.yaml" })
        {
            Assert.Equal(Shape("usb-helper.yaml"), Shape(file));
        }
    }

    // Owner decision 2026-11-17: MST is in the guides, 1 on Windows and 0 (no) elsewhere in USB-Helper's.
    [Theory]
    [InlineData("macos-apple-silicon", 0)]
    [InlineData("macos-a-series", 0)]
    [InlineData("macos-intel", 0)]
    [InlineData("windows-intel", 1)]
    [InlineData("windows-amd", 1)]
    [InlineData("windows-arm", 1)]
    [InlineData("linux-x64", 0)]
    [InlineData("android-tablet-exynos", 0)]
    [InlineData("apple-mobile-a-series", 0)]
    public void UsbHelperSaysWhetherEachPlatformSupportsMst(string id, long mst)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var profile = db.Platforms[id];

        Assert.Equal(mst, db.DefaultGuide.Mst(profile.Platform, profile.Chip)!.Number);
        Assert.Null(profile.Find("displays.mst"));
    }

    [Fact]
    public void BaseHoldsTheUsbFacts()
    {
        var windows = HardwareDatabase.Load(DatabaseFolder()).Platforms["windows-intel"];

        Assert.Equal(127L, windows.Find("usb.max_devices_per_controller")!.Scalar);
        Assert.Equal(5L, windows.Find("usb4.max_router_depth")!.Scalar);
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
        Assert.Equal(3L, GuideLimits.Of(neo, db.DefaultGuide, "hops.max").Value);
        Assert.Equal(1L, GuideLimits.Of(neo, db.DefaultGuide, "hops.recommended").Value);
        Assert.Equal(false, neo.Find("ports.usb2.displayport_alt_mode")!.Scalar);
        // Apple: one external display. No MST comes from the guide (macOS: 0).
        Assert.Equal(1L, neo.Find("displays.max_external")!.Scalar);
        Assert.Equal(0L, db.DefaultGuide.Mst(neo.Platform, neo.Chip)!.Number);
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
        Assert.Equal(4L, GuideLimits.Of(mac, HardwareDatabase.Load(DatabaseFolder()).DefaultGuide, "hops.max").Value);
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
        Assert.Equal(5L, GuideLimits.Of(asus, HardwareDatabase.Load(DatabaseFolder()).DefaultGuide, "hops.max").Value);
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
