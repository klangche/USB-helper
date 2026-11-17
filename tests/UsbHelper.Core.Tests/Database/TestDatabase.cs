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
    /// Values of USB-Helper's guide folder, which <see cref="Load"/> writes in full unless the test wrote it itself. A key
    /// such as <c>hops.max</c> sets that value on USB 2 and USB 3 in all.yaml and every chip; <c>usb4.hops.max</c>,
    /// <c>camera_audio.hops.max</c>, <c>mst</c> and <c>exceeded_note</c> likewise; a full path such as
    /// <c>platforms.test.chips.platform.limits.hops.max</c> sets one chip's value. Unset values are <c>none</c>,
    /// <c>mst</c> 0 and no note.
    /// </summary>
    public Dictionary<string, string> UsbHelper { get; } = [];

    /// <summary>USB-Helper's verification status.</summary>
    public string UsbHelperStatus { get; set; } = "partial";

    /// <summary>
    /// Another guide folder <c>guides/{id}/</c>, written in full by <see cref="Load"/>. A key such as <c>hops.max</c> sets
    /// that value in all.yaml; a full path sets one chip's value; every other value is empty.
    /// </summary>
    public TestDatabase WriteGuide(Dictionary<string, string> values, string id = "guide", string name = "Guide", bool selectable = true, string status = "partial")
    {
        guides[id] = (name, selectable, values, status);
        return this;
    }

    private readonly Dictionary<string, (string Name, bool Selectable, Dictionary<string, string> Values, string Status)> guides = [];

    /// <summary>USB-Helper's guide files as <see cref="Load"/> writes them, by name (guide.yaml, all.yaml, test.yaml...).</summary>
    public Dictionary<string, string> UsbHelperFiles() => GuideFiles("USB-Helper", true, true, UsbHelper, Platforms(), UsbHelperStatus);

    /// <summary>The files of a guide folder in the shape every guide must have, for <paramref name="platforms"/>.</summary>
    public static Dictionary<string, string> GuideFiles(string name, bool selectable, bool isDefault, IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>> platforms, string status)
    {
        string Value(string? chipPath, string relative)
        {
            var parts = relative.Split('.');
            var busless = parts[0] is "usb2" or "usb3" ? string.Join('.', parts[1..]) : null;
            string? Find(string key) => values.GetValueOrDefault(key) ?? (busless == null ? null : values.GetValueOrDefault(key[..^relative.Length] + busless));
            return (chipPath == null ? null : Find(chipPath + relative))
                ?? (isDefault || chipPath == null ? Find(relative) : null)
                ?? (!isDefault || relative == "exceeded_note" ? "" : relative == "mst" ? "0" : "none");
        }
        string Block(string indent, string? chipPath)
        {
            var yaml = new System.Text.StringBuilder();
            void Leaf(string pad, string key, string relative) =>
                yaml.Append($"{indent}{pad}{key}:{(Value(chipPath, relative) is { Length: > 0 } text ? " " + text : "")}\n");
            foreach (var bus in new[] { "usb2", "usb3", "usb4" })
            {
                yaml.Append($"{indent}{bus}:\n");
                foreach (var kind in new[] { "hubs", "tiers", "hops" })
                {
                    yaml.Append($"{indent}  {kind}:\n");
                    Leaf("    ", "recommended", $"{bus}.{kind}.recommended");
                    Leaf("    ", "max", $"{bus}.{kind}.max");
                }
            }
            yaml.Append($"{indent}camera_audio:\n");
            foreach (var kind in new[] { "tiers", "hops" })
            {
                yaml.Append($"{indent}  {kind}:\n");
                Leaf("    ", "recommended", $"camera_audio.{kind}.recommended");
                Leaf("    ", "max", $"camera_audio.{kind}.max");
            }
            Leaf("", "mst", "mst");
            Leaf("", "exceeded_note", "exceeded_note");
            return yaml.ToString();
        }
        var files = new Dictionary<string, string>
        {
            ["guide.yaml"] = $"name: {name}\nselectable: {(selectable ? "true" : "false")}\nverification:\n  status: {status}\n",
            ["all.yaml"] = Block("", null),
        };
        foreach (var (platform, chips) in platforms)
        {
            files[$"{platform}.yaml"] = string.Concat(chips.Select(chip => $"{chip}:\n" + Block("  ", $"platforms.{platform}.chips.{chip}.limits.")));
        }
        return files;
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
        void WriteFolder(string id, Dictionary<string, string> files)
        {
            Directory.CreateDirectory(Path.Combine(Folder, "guides", id));
            foreach (var (file, content) in files.Where(file => !written.Contains($"guides/{id}/{file.Key}")))
            {
                File.WriteAllText(Path.Combine(Folder, "guides", id, file), content);
            }
        }
        if (WithDefaultGuide)
        {
            WriteFolder("usb-helper", UsbHelperFiles());
        }
        foreach (var (id, guide) in guides)
        {
            WriteFolder(id, GuideFiles(guide.Name, guide.Selectable, false, guide.Values, platforms, guide.Status));
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
