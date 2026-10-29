using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>A database folder in a temporary directory, with a valid base.yaml and one platform profile.</summary>
internal sealed class TestDatabase : IDisposable
{
    public const string Base = """
        schema_version: 1
        defaults:
          limits:
            max_hubs: null
            max_tiers: null
            max_hops: null
          verification:
            status: unverified
        """;

    public const string Platform = """
        id: test-platform
        inherits: base
        platform: windows
        architecture: x64
        notes:
          - "Platform note."
        """;

    public TestDatabase()
    {
        Folder = Directory.CreateTempSubdirectory("usb-helper-db-").FullName;
        Write("base.yaml", Base);
        Write("platforms/test-platform.yaml", Platform);
    }

    public string Folder { get; }

    public TestDatabase Write(string file, string content)
    {
        var path = Path.Combine(Folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return this;
    }

    /// <summary>A computer profile inheriting from test-platform, with the required fields and <paramref name="extra"/> appended.</summary>
    public TestDatabase WriteComputer(string file, string id, string extra = "") => Write(file, $"""
        id: {id}
        inherits: test-platform
        manufacturer: Test
        model: Test model
        limits:
          max_hubs: null
          max_tiers: null
          max_hops: null
        {extra}
        """);

    public HardwareDatabase Load() => HardwareDatabase.Load(Folder);

    public IReadOnlyList<DatabaseError> LoadErrors() => Assert.Throws<DatabaseException>(Load).Errors;

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
