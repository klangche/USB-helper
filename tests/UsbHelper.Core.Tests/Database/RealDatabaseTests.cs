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

    // Values from usb-tree-config.json (platformStability) in klangche/usb-script: max -> max_hops, rec -> recommended_hops.
    [Theory]
    [InlineData("windows-intel", 7, 5)]
    [InlineData("windows-amd", 7, 5)]
    [InlineData("windows-arm", 5, 3)]
    [InlineData("macos-intel", 7, 5)]
    [InlineData("macos-apple-silicon", 5, 3)]
    [InlineData("apple-mobile-a-series", 4, 2)]
    [InlineData("linux-x64", 6, 4)]
    [InlineData("linux-arm", 5, 3)]
    [InlineData("android-phone-qualcomm", 5, 3)]
    [InlineData("android-tablet-exynos", 4, 2)]
    public void PlatformHopLimitsArePartiallyVerified(string id, long maxHops, long recommendedHops)
    {
        var platform = HardwareDatabase.Load(DatabaseFolder()).Platforms[id];

        Assert.Equal(maxHops, platform.Find("limits.max_hops")!.Scalar);
        Assert.Equal(recommendedHops, platform.Find("limits.recommended_hops")!.Scalar);
        Assert.Equal("partial", platform.Find("verification.status")!.Scalar);
        Assert.Equal($"platforms/{id}.yaml", platform.Find("verification.status")!.File);
        Assert.NotEmpty(platform.Find("sources")!.Items);
    }

    [Fact]
    public void ComputerTemplateIsValidWhenCopied()
    {
        var template = File.ReadAllText(Path.Combine(DatabaseFolder(), "computers", ".template.yaml"));
        using var db = new TestDatabase().Write("computers/copy.yaml", template.Replace("inherits: platform-profile", "inherits: test-platform"));

        Assert.Equal(["manufacturer.model-variant"], db.Load().Computers.Keys);
    }
}
