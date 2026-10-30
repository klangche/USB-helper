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

    /// <summary>
    /// A computer profile of manufacturer "Test" inheriting from test-platform, with the required fields and
    /// <paramref name="extra"/> appended. Its id is <see cref="IdOf"/>(<paramref name="model"/>).
    /// </summary>
    public TestDatabase WriteComputer(string file, string model, string extra = "") => Write(file, $"""
        inherits: test-platform
        manufacturer: Test
        model: {model}
        limits:
          max_hubs: null
          max_tiers: null
          max_hops: null
        {extra}
        """);

    /// <summary>The computed id of a computer written with <see cref="WriteComputer"/>.</summary>
    public static string IdOf(string model) => ComputerProfileId.From("Test", model);

    public HardwareDatabase Load() => HardwareDatabase.Load(Folder);

    public IReadOnlyList<DatabaseError> LoadErrors() => Assert.Throws<DatabaseException>(Load).Errors;

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
