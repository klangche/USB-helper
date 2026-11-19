using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>Which database format is used when several are present (#334).</summary>
public class DatabaseFormatsTests
{
    private static readonly HardwareDatabase V3 = HardwareDatabase.Load(RealDatabaseTests.DatabaseFolder());
    private static readonly HardwareDatabase V4 = HardwareDatabase.Load(RealDatabaseTests.DatabaseFolder());
    private static readonly int[] Supported = [3, 4];

    private readonly List<string> log = [];

    private static Func<HardwareDatabase> Broken(string message) =>
        () => throw new DatabaseException([new DatabaseError("base.yaml", 1, message)]);

    private (HardwareDatabase Database, int Format) Choose(Dictionary<int, Func<HardwareDatabase>> present, int? preferred = null) =>
        DatabaseFormats.Choose(present, preferred, log.Add, Supported);

    [Fact]
    public void TheNewestPresentFormatIsUsed()
    {
        var (database, format) = Choose(new() { [3] = () => V3, [4] = () => V4 });

        Assert.Same(V4, database);
        Assert.Equal(4, format);
        Assert.Equal(["Database format v4 in use"], log);
    }

    [Fact]
    public void OnlyAnOlderFormatIsUsedWhenItIsTheOnlyOne()
    {
        Assert.Equal(3, Choose(new() { [3] = () => V3 }).Format);
    }

    [Fact]
    public void ABrokenNewestFormatFallsBackAndTheLogNamesItsError()
    {
        var (database, format) = Choose(new() { [3] = () => V3, [4] = Broken("v4 is broken.") });

        Assert.Same(V3, database);
        Assert.Equal(3, format);
        Assert.Equal(["Database format v4 not used: 1 problem(s), first: base.yaml:1: v4 is broken.", "Database format v3 in use, because v4 does not load"], log);
    }

    [Fact]
    public void AFormatNewerThanTheAppIsIgnored()
    {
        var (_, format) = Choose(new() { [3] = () => V3, [4] = () => V4, [5] = Broken("never read") });

        Assert.Equal(4, format);
        Assert.DoesNotContain(log, line => line.Contains("v5", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePreferredFormatIsUsedWhenItLoads()
    {
        Assert.Equal(3, Choose(new() { [3] = () => V3, [4] = () => V4 }, preferred: 3).Format);
        Assert.Equal("Database format v3 in use, chosen in the settings", log[^1]);
        Assert.Equal(4, Choose(new() { [3] = Broken("broken"), [4] = () => V4 }, preferred: 3).Format);
        Assert.Equal(4, Choose(new() { [4] = () => V4 }, preferred: 3).Format);
    }

    [Fact]
    public void NoFormatThatLoadsIsAnError()
    {
        var e = Assert.Throws<DatabaseException>(() => Choose(new() { [3] = Broken("three"), [4] = Broken("four") }));

        Assert.Equal("four", e.Errors[0].Message);
        Assert.Throws<DatabaseException>(() => Choose(new() { [5] = () => V4 }));
    }

    // The v3 reader is the loader every existing test uses.
    [Fact]
    public void ThisAppReadsV3WithTheV3Reader()
    {
        Assert.Contains(3, DatabaseFormats.Supported);
        Assert.Equal(V3.Platforms.Keys, DatabaseFormats.Read(3, Directory.EnumerateFiles(RealDatabaseTests.DatabaseFolder(), "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(RealDatabaseTests.DatabaseFolder(), path).Replace('\\', '/'), File.ReadAllText)).Platforms.Keys);
    }
}
