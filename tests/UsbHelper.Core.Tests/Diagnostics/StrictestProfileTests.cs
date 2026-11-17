using UsbHelper.Core.Database;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;

namespace UsbHelper.Core.Tests.Diagnostics;

public class StrictestProfileTests
{
    // One platform profile per entry, each with its limits in USB-Helper's guide ("none" for none), in the order given.
    private static (IReadOnlyList<ResolvedProfile> Profiles, Guide Guide) Profiles(params (string Tiers, string Recommended, string Hubs)[] limits)
    {
        using var db = new TestDatabase().Write("platforms/p.yaml", "name: P\nchips:\n"
            + string.Concat(limits.Select((_, i) => $"  c{i}: {{ name: C{i}, built_in_hub_levels: 0 }}\n")));
        for (var i = 0; i < limits.Length; i++)
        {
            var chip = $"platforms.p.chips.c{i}.limits";
            db.UsbHelper[$"{chip}.tiers.max"] = limits[i].Tiers;
            db.UsbHelper[$"{chip}.tiers.recommended"] = limits[i].Recommended;
            db.UsbHelper[$"{chip}.hubs.max"] = limits[i].Hubs;
        }
        var loaded = db.Load();
        return ([.. limits.Select((_, i) => loaded.Platforms[$"p-c{i}"])], loaded.DefaultGuide);
    }

    private static int Strictest(params (string Tiers, string Recommended, string Hubs)[] limits)
    {
        var (profiles, guide) = Profiles(limits);
        return StrictestProfile.Of(profiles, guide);
    }

    [Fact]
    public void OneProfileIsTheStrictest() => Assert.Equal(0, Strictest(("7", "5", "5")));

    [Fact]
    public void TheLowestMaxTiersIsTheStrictest() =>
        Assert.Equal(1, Strictest(("7", "1", "1"), ("6", "5", "5")));

    [Fact]
    public void ATieGoesToTheNextLimit()
    {
        Assert.Equal(1, Strictest(("7", "5", "1"), ("7", "4", "5")));
        Assert.Equal(1, Strictest(("7", "5", "5"), ("7", "5", "4")));
    }

    [Fact]
    public void AnUnknownLimitCountsAsHigherThanAnyKnownOne()
    {
        Assert.Equal(1, Strictest(("none", "1", "1"), ("7", "6", "100")));
        Assert.Equal(0, Strictest(("7", "5", "5"), ("7", "none", "1")));
    }

    [Fact]
    public void StillTiedTheFirstWins() =>
        Assert.Equal(0, Strictest(("7", "5", "none"), ("7", "5", "none")));
}
