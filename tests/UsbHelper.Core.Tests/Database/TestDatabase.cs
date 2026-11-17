using UsbHelper.Core.Database;

namespace UsbHelper.Core.Tests.Database;

/// <summary>
/// A database folder in a temporary directory, with a valid base.yaml (usb.max_tiers 7) and one platform file,
/// <c>platforms/test.yaml</c>, with one chip, <c>platform</c>: so its one platform profile is <c>test-platform</c>.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    public const string Base = """
        schema_version: 2
        usb:
          max_tiers: 7
        verification:
          status: unverified
        """;

    public const string PlatformFile = "platforms/test.yaml";

    public const string Platform = """
        name: Test OS
        chips:
          platform:
            name: Test chip
            architecture: x64
            built_in_hub_levels: 0
        notes:
          - "Platform note."
        """;

    public TestDatabase()
    {
        Folder = Directory.CreateTempSubdirectory("usb-helper-db-").FullName;
        Write("base.yaml", Base);
        Write(PlatformFile, Platform);
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
    /// A computer profile of manufacturer "Test" on test-platform, with the required fields and <paramref name="extra"/>
    /// appended from line 5. Its id is <see cref="IdOf"/>(<paramref name="model"/>).
    /// </summary>
    public TestDatabase WriteComputer(string file, string model, string extra = "") => Write(file, $"""
        platform: test
        chip_family: platform
        manufacturer: Test
        model: {model}
        {extra}
        """);

    /// <summary>A guide file <c>guides/{id}.yaml</c> named "Guide", selectable, with <paramref name="limits"/> under <c>limits:</c>.</summary>
    /// <param name="limits">YAML lines, each indented by two spaces, for example <c>"  recommended_tiers: 4"</c>.</param>
    public TestDatabase WriteGuide(string limits, string id = "guide", string status = "partial") => Write($"guides/{id}.yaml",
        $"name: Guide
selectable: true
limits:
{limits}
verification:
  status: {status}
");

    /// <summary>The computed id of a computer written with <see cref="WriteComputer"/>.</summary>
    public static string IdOf(string model) => ComputerProfileId.From("Test", model);

    public HardwareDatabase Load() => HardwareDatabase.Load(Folder);

    public IReadOnlyList<DatabaseError> LoadErrors() => Assert.Throws<DatabaseException>(Load).Errors;

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
