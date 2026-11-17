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
                "android-phone-qualcomm", "android-tablet-exynos", "apple-mobile-a-series", "apple-mobile-m-series",
                "debian-amd", "debian-arm", "debian-intel", "macos-a-series", "macos-apple-silicon", "macos-intel",
                "ubuntu-amd", "ubuntu-arm", "ubuntu-intel", "windows-amd", "windows-arm", "windows-intel",
            ],
            db.Platforms.Keys.Order());
        Assert.Equal(["crestron", "extron", "ochno", "purelink", "unisynk", "usb-helper"], db.Guides.Keys.Order());
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

    // Owner decisions 2026-11-16 (#260) and 2026-11-17 (#284): the chips' built-in hub levels. Ubuntu and Debian are
    // their own systems with Intel, AMD and ARM, as the generic Linux profile before them (owner decision 2026-11-17).
    [Theory]
    [InlineData("windows-intel", "windows", "Windows", "Intel", 0)]
    [InlineData("windows-amd", "windows", "Windows", "AMD", 0)]
    [InlineData("windows-arm", "windows", "Windows", "ARM", 0)]
    [InlineData("macos-intel", "macos", "macOS", "Intel", 0)]
    [InlineData("macos-apple-silicon", "macos", "macOS", "M-series", 1)]
    [InlineData("macos-a-series", "macos", "macOS", "A-series", 2)]
    [InlineData("apple-mobile-a-series", "apple-mobile", "iPhone and iPad", "A-series", 2)]
    [InlineData("ubuntu-intel", "ubuntu", "Ubuntu", "Intel", 0)]
    [InlineData("ubuntu-amd", "ubuntu", "Ubuntu", "AMD", 0)]
    [InlineData("ubuntu-arm", "ubuntu", "Ubuntu", "ARM", 0)]
    [InlineData("debian-intel", "debian", "Debian", "Intel", 0)]
    [InlineData("debian-amd", "debian", "Debian", "AMD", 0)]
    [InlineData("debian-arm", "debian", "Debian", "ARM", 0)]
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

    // Not measured yet on an M-series iPad: unknown, never a guessed default.
    [Fact]
    public void MSeriesIPadHasUnknownBuiltInHubLevels()
    {
        var profile = HardwareDatabase.Load(DatabaseFolder()).Platforms["apple-mobile-m-series"];

        Assert.Equal("M-series", profile.ChipName);
        Assert.Equal(ValueKind.Null, profile.Find("built_in_hub_levels")!.Kind);
    }

    // USB-Helper's guide (the owner's file of 2026-11-17, hops removed in #297): tiers per chip, else per platform, else
    // global; hubs the same way; cameras and audio devices tiers 3 / 5 everywhere.
    [Theory]
    [InlineData("windows-intel", 7, 5, 6)]
    [InlineData("windows-amd", 7, 5, 6)]
    [InlineData("windows-arm", 7, 5, 6)]
    [InlineData("macos-intel", 7, 5, 6)]
    [InlineData("macos-apple-silicon", 7, 5, 6)]
    [InlineData("macos-a-series", 7, 5, 6)]
    [InlineData("apple-mobile-a-series", 3, 2, 2)]
    [InlineData("ubuntu-intel", 7, 5, 6)]
    [InlineData("ubuntu-amd", 7, 5, 6)]
    [InlineData("ubuntu-arm", 7, 5, 6)]
    [InlineData("debian-intel", 7, 5, 6)]
    [InlineData("debian-arm", 7, 5, 6)]
    [InlineData("android-phone-qualcomm", 3, 2, 2)]
    [InlineData("android-tablet-exynos", 4, 3, 2)]
    public void UsbHelperGivesItsLimits(string id, long maxTiers, long recommendedTiers, long maxHubs)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var profile = db.Platforms[id];

        Assert.Equal(maxTiers, GuideLimits.Of(profile, db.DefaultGuide, "tiers.max").Value);
        Assert.Equal(recommendedTiers, GuideLimits.Of(profile, db.DefaultGuide, "tiers.recommended").Value);
        Assert.Equal(maxTiers, GuideLimits.Of(profile, db.DefaultGuide, "usb2.tiers.max").Value); // The same on every bus.
        Assert.Null(GuideLimits.Of(profile, db.DefaultGuide, "hops.max").Value); // No hops any more (#297).
        Assert.Equal(3L, GuideLimits.Of(profile, db.DefaultGuide, "camera_audio.tiers.recommended").Value);
        Assert.Equal(5L, GuideLimits.Of(profile, db.DefaultGuide, "camera_audio.tiers.max").Value);
        foreach (var bus in new[] { "usb2", "usb3", "usb4" })
        {
            Assert.Equal(maxHubs, GuideLimits.Of(profile, db.DefaultGuide, $"{bus}.hubs.max").Value);
        }
    }

    // The per-bus hubs are USB-Helper's only (owner decision 2026-11-17): the other guides leave them empty.
    [Fact]
    public void UsbHelperHoldsHubsPerBus()
    {
        var guide = HardwareDatabase.Load(DatabaseFolder()).DefaultGuide;

        foreach (var bus in new[] { "usb2", "usb3", "usb4" })
        {
            Assert.Equal(4L, guide.Find($"limits.buses.{bus}.hubs.recommended")!.Scalar);
            Assert.Equal(6L, guide.Find($"limits.buses.{bus}.hubs.max")!.Scalar);
        }
    }

    // The owner's files of 2026-11-17: every guide selectable and filled in, USB-Helper's values where one is empty.
    [Theory]
    [InlineData("purelink", "PureLink")]
    [InlineData("ochno", "Ochno")]
    [InlineData("unisynk", "Unisynk")]
    public void GuidesAreSelectableWithUsbHelperAsFallback(string id, string name)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var guide = db.Guides[id];

        Assert.Equal(name, guide.Name);
        Assert.True(guide.Selectable);
        Assert.Same(db.DefaultGuide, guide.Fallback);
        foreach (var bus in new[] { "usb2", "usb3", "usb4" })
        {
            Assert.Equal(ValueKind.Null, guide.Find($"limits.buses.{bus}.hubs.max")!.Kind);
        }
        foreach (var profile in db.Platforms.Values)
        {
            Assert.NotNull(guide.Limit(profile.Platform, profile.Chip, "tiers.max"));
            Assert.NotNull(guide.Mst(profile.Platform, profile.Chip));
        }
    }

    // macOS does not extend the desktop over MST, whatever the guide (owner decision 2026-11-17).
    [Fact]
    public void NoGuideGivesMacOsMst()
    {
        var db = HardwareDatabase.Load(DatabaseFolder());

        foreach (var guide in db.Guides.Values)
        {
            foreach (var profile in db.Platforms.Values.Where(profile => profile.Platform == "macos"))
            {
                Assert.Equal(0L, guide.Mst(profile.Platform, profile.Chip)!.Number);
            }
        }
    }

    // USB-Helper has no sources (owner decision 2026-11-17); Ochno's guide links its page.
    [Fact]
    public void GuideSources()
    {
        var db = HardwareDatabase.Load(DatabaseFolder());

        Assert.Null(db.DefaultGuide.Find("sources"));
        Assert.Contains(db.Guides["ochno"].Find("sources")!.Items,
            item => item.Scalar is "https://ochno.atlassian.net/wiki/spaces/~ocs/pages/1114996744/Guide+USB+hub+chains+in+video+conference+room");
    }

    // A few values read from the owner's files, through chip, platform and global.
    [Theory]
    [InlineData("purelink", "windows-intel", "tiers.max", 7)]
    [InlineData("purelink", "windows-amd", "tiers.recommended", 3)]
    [InlineData("purelink", "windows-amd", "usb3.hubs.max", 5)] // The guide's own hubs before USB-Helper's per bus.
    [InlineData("ochno", "windows-intel", "tiers.max", 6)]
    [InlineData("ochno", "ubuntu-arm", "tiers.max", 4)]
    [InlineData("unisynk", "debian-amd", "tiers.recommended", 4)]
    [InlineData("unisynk", "android-phone-qualcomm", "tiers.recommended", 2)]
    [InlineData("unisynk", "apple-mobile-m-series", "tiers.max", 3)]
    public void GuidesReadTheirOwnValues(string id, string profile, string path, long value)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var platform = db.Platforms[profile];

        var found = db.Guides[id].Limit(platform.Platform, platform.Chip, path)!;

        Assert.Equal(value, found.Number);
        Assert.Same(db.Guides[id], found.Guide);
    }

    // Owner's files: MST per platform and chip; USB-Helper says 1 on Windows except ARM, 0 elsewhere (macOS has no MST,
    // owner decision 2026-11-17).
    [Theory]
    [InlineData("macos-apple-silicon", 0)]
    [InlineData("macos-a-series", 0)]
    [InlineData("macos-intel", 0)]
    [InlineData("windows-intel", 1)]
    [InlineData("windows-amd", 1)]
    [InlineData("windows-arm", 0)]
    [InlineData("ubuntu-intel", 0)]
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
        Assert.Equal(7L, GuideLimits.Of(neo, db.DefaultGuide, "tiers.max").Value);
        Assert.Equal(5L, GuideLimits.Of(neo, db.DefaultGuide, "tiers.recommended").Value);
        Assert.Equal(false, neo.Find("ports.usb2.displayport_alt_mode")!.Scalar);
        // Apple: one external display. No MST comes from the guide (A-series: 0).
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
        Assert.Equal(7L, GuideLimits.Of(mac, HardwareDatabase.Load(DatabaseFolder()).DefaultGuide, "tiers.max").Value);
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
        Assert.Equal(7L, GuideLimits.Of(asus, HardwareDatabase.Load(DatabaseFolder()).DefaultGuide, "tiers.max").Value);
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

    // The owner's files of 2026-11-17: Extron and Crestron, with their sources; the first web link is the guide's page.
    [Theory]
    [InlineData("extron", "Extron", "https://www.chariotgroup.com/wp-content/uploads/2021/01/usb_white_paper_Extron.pdf")]
    [InlineData("crestron", "Crestron", "https://community.crestron.com/s/article/3082")]
    [InlineData("ochno", "Ochno", "https://ochno.atlassian.net/wiki/spaces/~ocs/pages/1114996744/Guide+USB+hub+chains+in+video+conference+room")]
    [InlineData("usb-helper", "USB-Helper", null)]
    public void GuidesLinkTheirFirstWebSource(string id, string name, string? link)
    {
        var guide = HardwareDatabase.Load(DatabaseFolder()).Guides[id];

        Assert.Equal(name, guide.Name);
        Assert.Equal(link, guide.Link?.ToString());
    }

    [Theory]
    [InlineData("extron", "hubs.max", 5)]
    [InlineData("crestron", "hubs.max", 4)]
    [InlineData("crestron", "tiers.max", 7)]
    public void ExtronAndCrestronHoldTheirLimits(string id, string path, long value)
    {
        var db = HardwareDatabase.Load(DatabaseFolder());
        var windows = db.Platforms["windows-intel"];

        Assert.Equal(value, db.Guides[id].Limit(windows.Platform, windows.Chip, path)!.Number);
    }
}
