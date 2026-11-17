using UsbHelper.Core.Database;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

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
          max_devices_per_controller: 127
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
        written.Add(file);
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

    /// <summary>
    /// Values of USB-Helper's guide file, which <see cref="Load"/> writes in full unless the test wrote it itself. A key
    /// such as <c>hops.max</c> sets that value at every level (all platforms, each platform and chip); a full path such as
    /// <c>platforms.test.chips.platform.limits.hops.max</c> or <c>platforms.test.mst</c> sets one value. Unset values are
    /// <c>none</c>, <c>mst</c> 0 and no note.
    /// </summary>
    public Dictionary<string, string> UsbHelper { get; } = [];

    /// <summary>USB-Helper's verification status.</summary>
    public string UsbHelperStatus { get; set; } = "partial";

    /// <summary>
    /// Another guide file <c>guides/{id}.yaml</c>, written in full by <see cref="Load"/>. A key such as <c>hops.max</c>
    /// sets that value under <c>limits</c> (all platforms); a full path sets one value; every other value is empty.
    /// </summary>
    public TestDatabase WriteGuide(Dictionary<string, string> values, string id = "guide", string name = "Guide", bool selectable = true, string status = "partial")
    {
        guides[id] = (name, selectable, values, status);
        return this;
    }

    private readonly Dictionary<string, (string Name, bool Selectable, Dictionary<string, string> Values, string Status)> guides = [];

    /// <summary>
    /// A guide file in the shape every guide file must have, for <paramref name="platforms"/> (each platform's chips).
    /// </summary>
    public static string GuideYaml(string name, bool selectable, bool isDefault, IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>> platforms, string status)
    {
        var yaml = new System.Text.StringBuilder($"name: {name}\nselectable: {(selectable ? "true" : "false")}\n");
        string Value(string path, string relative, bool everyLevel) =>
            values.GetValueOrDefault(path) ?? (isDefault || !everyLevel ? values.GetValueOrDefault(relative) : null)
            ?? (!isDefault || relative == "exceeded_note" ? "" : relative == "mst" ? "0" : "none");
        void Leaf(string indent, string key, string path, string relative, bool everyLevel) =>
            yaml.Append($"{indent}{key}:{(Value(path, relative, everyLevel) is { Length: > 0 } text ? " " + text : "")}\n");
        void Block(string indent, string path, bool everyLevel)
        {
            yaml.Append($"{indent}limits:\n");
            foreach (var kind in new[] { "hubs", "tiers", "hops" })
            {
                yaml.Append($"{indent}  {kind}:\n");
                foreach (var which in new[] { "recommended", "max" })
                {
                    Leaf(indent + "    ", which, $"{path}.{kind}.{which}", $"{kind}.{which}", everyLevel);
                }
            }
            yaml.Append($"{indent}  camera_audio:\n");
            foreach (var kind in new[] { "tiers", "hops" })
            {
                yaml.Append($"{indent}    {kind}:\n");
                foreach (var which in new[] { "recommended", "max" })
                {
                    Leaf(indent + "      ", which, $"{path}.camera_audio.{kind}.{which}", $"camera_audio.{kind}.{which}", everyLevel);
                }
            }
            Leaf(indent + "  ", "exceeded_note", $"{path}.exceeded_note", "exceeded_note", everyLevel);
        }
        Block("", "limits", everyLevel: false);
        yaml.Append("platforms:\n");
        foreach (var (platform, chips) in platforms)
        {
            yaml.Append($"  {platform}:\n");
            Leaf("    ", "mst", $"platforms.{platform}.mst", "mst", everyLevel: true);
            Block("    ", $"platforms.{platform}.limits", everyLevel: true);
            yaml.Append("    chips:\n");
            foreach (var chip in chips)
            {
                yaml.Append($"      {chip}:\n");
                Leaf("        ", "mst", $"platforms.{platform}.chips.{chip}.mst", "mst", everyLevel: true);
                Block("        ", $"platforms.{platform}.chips.{chip}.limits", everyLevel: true);
            }
        }
        return yaml.Append($"verification:\n  status: {status}\n").ToString();
    }

    // Each platform file's chip keys, read from the folder; a file that cannot be read is left out (the loader reports it).
    private Dictionary<string, IReadOnlyList<string>> Platforms()
    {
        var platforms = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var path in Directory.GetFiles(Path.Combine(Folder, "platforms"), "*.yaml").Order(StringComparer.Ordinal))
        {
            try
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(File.ReadAllText(path)));
                if (stream.Documents.Count == 1 && stream.Documents[0].RootNode is YamlMappingNode root
                    && root.Children.TryGetValue(new YamlScalarNode("chips"), out var chips) && chips is YamlMappingNode chipMap)
                {
                    platforms[Path.GetFileNameWithoutExtension(path)] = [.. chipMap.Children.Keys.OfType<YamlScalarNode>().Select(key => key.Value ?? "")];
                }
            }
            catch (YamlException)
            {
            }
        }
        return platforms;
    }

    private void WriteGuides()
    {
        var platforms = Platforms();
        if (!written.Contains("guides/usb-helper.yaml") && WithDefaultGuide)
        {
            File.WriteAllText(Path.Combine(Folder, "guides", "usb-helper.yaml"), GuideYaml("USB-Helper", true, true, UsbHelper, platforms, UsbHelperStatus));
        }
        foreach (var (id, guide) in guides)
        {
            File.WriteAllText(Path.Combine(Folder, "guides", $"{id}.yaml"), GuideYaml(guide.Name, guide.Selectable, false, guide.Values, platforms, guide.Status));
        }
    }

    /// <summary>False to load without USB-Helper's guide file.</summary>
    public bool WithDefaultGuide { get; set; } = true;

    private readonly HashSet<string> written = [];

    /// <summary>The computed id of a computer written with <see cref="WriteComputer"/>.</summary>
    public static string IdOf(string model) => ComputerProfileId.From("Test", model);

    public HardwareDatabase Load()
    {
        Directory.CreateDirectory(Path.Combine(Folder, "guides"));
        WriteGuides();
        return HardwareDatabase.Load(Folder);
    }

    public IReadOnlyList<DatabaseError> LoadErrors() => Assert.Throws<DatabaseException>(Load).Errors;

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
