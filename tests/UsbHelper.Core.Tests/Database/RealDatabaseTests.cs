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
            ["apple-mobile-a-series", "macos-apple-silicon", "macos-intel", "windows-amd", "windows-intel"],
            db.Platforms.Keys.Order());
        Assert.Equal("arm64", db.Platforms["macos-apple-silicon"].Find("architecture")!.Scalar);
        Assert.Equal(ValueKind.Null, db.Platforms["windows-intel"].Find("limits.max_hubs")!.Kind);
    }

    [Fact]
    public void ComputerTemplateIsValidWhenCopied()
    {
        var template = File.ReadAllText(Path.Combine(DatabaseFolder(), "computers", ".template.yaml"));
        using var db = new TestDatabase().Write("computers/copy.yaml", template.Replace("inherits: platform-profile", "inherits: test-platform"));

        Assert.Equal([ComputerProfileId.From("Manufacturer", "Exact model name")], db.Load().Computers.Keys);
    }
}
