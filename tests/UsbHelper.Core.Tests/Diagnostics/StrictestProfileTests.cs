using UsbHelper.Core.Database;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Tests.Database;

namespace UsbHelper.Core.Tests.Diagnostics;

public class StrictestProfileTests
{
    // One computer profile per entry, each with its limits (YAML values, "null" for unknown), in the order given.
    private static IReadOnlyList<ResolvedProfile> Profiles(params (string Hops, string Recommended, string Tiers, string Hubs)[] limits)
    {
        using var db = new TestDatabase();
        for (var i = 0; i < limits.Length; i++)
        {
            db.Write($"computers/c{i}.yaml", $"""
                inherits: test-platform
                manufacturer: Test
                model: Laptop {i}
                limits:
                  max_hops: {limits[i].Hops}
                  recommended_hops: {limits[i].Recommended}
                  max_tiers: {limits[i].Tiers}
                  max_hubs: {limits[i].Hubs}
                """);
        }
        var computers = db.Load().Computers;
        return [.. Enumerable.Range(0, limits.Length).Select(i => computers[TestDatabase.IdOf($"Laptop {i}")])];
    }

    [Fact]
    public void OneProfileIsTheStrictest() => Assert.Equal(0, StrictestProfile.Of(Profiles(("5", "3", "7", "5"))));

    [Fact]
    public void TheLowestMaxHopsIsTheStrictest() =>
        Assert.Equal(1, StrictestProfile.Of(Profiles(("7", "1", "1", "1"), ("5", "5", "7", "5"))));

    [Fact]
    public void ATieGoesToTheNextLimit()
    {
        Assert.Equal(1, StrictestProfile.Of(Profiles(("5", "4", "1", "1"), ("5", "3", "7", "5"))));
        Assert.Equal(1, StrictestProfile.Of(Profiles(("5", "3", "7", "1"), ("5", "3", "6", "5"))));
        Assert.Equal(1, StrictestProfile.Of(Profiles(("5", "3", "7", "5"), ("5", "3", "7", "4"))));
    }

    [Fact]
    public void AnUnknownLimitCountsAsHigherThanAnyKnownOne()
    {
        Assert.Equal(1, StrictestProfile.Of(Profiles(("null", "1", "1", "1"), ("100", "100", "100", "100"))));
        Assert.Equal(0, StrictestProfile.Of(Profiles(("5", "3", "7", "5"), ("5", "null", "1", "1"))));
    }

    [Fact]
    public void StillTiedTheFirstWins() =>
        Assert.Equal(0, StrictestProfile.Of(Profiles(("5", "3", "null", "null"), ("5", "3", "null", "null"))));
}
