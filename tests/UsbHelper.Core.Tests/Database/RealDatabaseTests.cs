using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>Loads the database folder in this repository.</summary>
public class RealDatabaseTests
{
    private static string DatabaseFolder()
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
                "macos-apple-silicon", "macos-intel", "windows-amd", "windows-arm", "windows-intel",
            ],
            db.Platforms.Keys.Order());
        Assert.Equal("arm64", db.Platforms["macos-apple-silicon"].Find("architecture")!.Scalar);
        Assert.Equal(ValueKind.Null, db.Platforms["windows-intel"].Find("limits.max_hubs")!.Kind);
    }

    // Owner decision 2026-11-16 (#260): USB allows seven tiers, so five hubs in a row (PureLink); Apple silicon has one
    // fewer and A-series one fewer again; recommended is within four tiers (PureLink). Android tablet keeps its 4.
    [Theory]
    [InlineData("windows-intel", 5, 2)]
    [InlineData("windows-amd", 5, 2)]
    [InlineData("windows-arm", 5, 2)]
    [InlineData("macos-intel", 5, 2)]
    [InlineData("macos-apple-silicon", 4, 1)]
    [InlineData("apple-mobile-a-series", 3, 0)]
    [InlineData("linux-x64", 5, 2)]
    [InlineData("linux-arm", 5, 2)]
    [InlineData("android-phone-qualcomm", 5, 2)]
    [InlineData("android-tablet-exynos", 4, 1)]
    public void PlatformHopLimitsArePartiallyVerified(string id, long maxHops, long recommendedHops)
    {
        var platform = HardwareDatabase.Load(DatabaseFolder()).Platforms[id];

        Assert.Equal(maxHops, platform.Find("limits.max_hops")!.Scalar);
        Assert.Equal(recommendedHops, platform.Find("limits.recommended_hops")!.Scalar);
        Assert.Equal("partial", platform.Find("verification.status")!.Scalar);
        Assert.Equal($"platforms/{id}.yaml", platform.Find("verification.status")!.File);
        Assert.NotEmpty(platform.Find("sources")!.Items);
    }

    // Owner decision 2026-11-16: MST is a property of the operating system, so every platform profile has the field:
    // false on macOS, true on Windows, null (unknown) elsewhere.
    [Theory]
    [InlineData("macos-apple-silicon", false)]
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
    public void MacBookNeoHasItsOwnHopLimit()
    {
        var neo = HardwareDatabase.Load(DatabaseFolder()).Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")];

        Assert.Equal("MacBook Neo", neo.Find("name")!.Scalar);
        Assert.Equal(3L, neo.Find("limits.max_hops")!.Scalar);
        Assert.Equal(0L, neo.Find("limits.recommended_hops")!.Scalar);
        Assert.Equal(false, neo.Find("ports.usb2.displayport_alt_mode")!.Scalar);
        // Apple: one external display. No MST is the operating system's limit, inherited from the platform profile.
        Assert.Equal(1L, neo.Find("displays.max_external")!.Scalar);
        Assert.Equal(false, neo.Find("displays.mst")!.Scalar);
        Assert.Equal("platforms/macos-apple-silicon.yaml", neo.Find("displays.mst")!.File);
        Assert.Equal("partial", neo.Find("verification.status")!.Scalar);
        Assert.NotEmpty(neo.Find("sources")!.Items);
    }

    [Fact]
    public void MacBookPro16M1MaxIsSelectedByExactModelAndKeepsUnknownsNull()
    {
        var mac = HardwareDatabase.Load(DatabaseFolder()).Computers[ComputerProfileId.From("Apple Inc.", "MacBookPro18,2")];

        Assert.Equal("Apple M1 Max", mac.Find("chip")!.Scalar);
        Assert.Equal(4L, mac.Find("displays.max_external")!.Scalar);
        Assert.Equal(4L, mac.Find("ports.thunderbolt4.thunderbolt")!.Scalar);
        Assert.Null(mac.Find("limits.max_hubs")!.Scalar);
        Assert.Null(mac.Find("limits.max_tiers")!.Scalar);
        Assert.Equal(4L, mac.Find("limits.max_hops")!.Scalar);
        Assert.NotEmpty(mac.Find("sources")!.Items);
    }

    // Exported by the owner from USB-helper 0.5.0-beta.4 (#74); limits come from windows-amd. The ports are from ASUS's
    // specification page and stay unverified until confirmed on the machine.
    [Fact]
    public void AsusRogStrixG713RWInheritsWindowsAmd()
    {
        var asus = HardwareDatabase.Load(DatabaseFolder()).Computers[ComputerProfileId.From("ASUSTeK COMPUTER INC.", "ROG Strix G713RW_G713RW")];

        Assert.Equal("ROG Strix G713RW", asus.Find("name")!.Scalar);
        Assert.Equal("unverified", asus.Find("verification.status")!.Scalar);
        Assert.Equal(5L, asus.Find("limits.max_hops")!.Scalar);
        Assert.Equal("platforms/windows-amd.yaml", asus.Find("limits.max_hops")!.File);
        Assert.Equal(true, asus.Find("ports.usbc_display.displayport_alt_mode")!.Scalar);
        Assert.Null(asus.Find("ports.usbc_data.displayport_alt_mode"));
        Assert.Equal(true, asus.Find("ports.hdmi.hdmi")!.Scalar);
        Assert.NotEmpty(asus.Find("sources")!.Items);
    }

    [Fact]
    public void ComputerTemplateIsValidWhenCopied()
    {
        var template = File.ReadAllText(Path.Combine(DatabaseFolder(), "computers", ".template.yaml"));
        using var db = new TestDatabase().Write("computers/copy.yaml", template.Replace("inherits: platform-profile", "inherits: test-platform"));

        Assert.Equal([ComputerProfileId.From("Manufacturer", "Exact model name")], db.Load().Computers.Keys);
    }
}
