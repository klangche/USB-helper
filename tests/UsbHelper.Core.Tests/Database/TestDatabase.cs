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
    /// such as <c>tiers.max</c>, <c>camera_audio.tiers.max</c> or <c>exceeded_note</c> sets the global value;
    /// <c>usb2.hubs.max</c> sets one bus's hubs (<c>usb2.tiers.max</c> is the same as <c>tiers.max</c>: tiers are
    /// the same on every bus; a bus's hubs default to the global hubs in USB-Helper's); <c>mst</c> sets every platform's and chip's mst; a path such as
    /// <c>platforms.test.chips.platform.limits.tiers.max</c> (or <c>.limits.mst</c>) sets one chip's value. Unset values are
    /// <c>none</c>, a bus's hubs the global hubs, <c>mst</c> 0 and no note.
    /// </summary>
    public Dictionary<string, string> UsbHelper { get; } = [];

    /// <summary>USB-Helper's verification status.</summary>
    public string UsbHelperStatus { get; set; } = "partial";

    /// <summary>
    /// Another guide file <c>guides/{id}.yaml</c>, written in full by <see cref="Load"/>. Keys as for
    /// <see cref="UsbHelper"/>; every value not given is empty.
    /// </summary>
    public TestDatabase WriteGuide(Dictionary<string, string> values, string id = "guide", string name = "Guide", bool selectable = true, string status = "partial")
    {
        guides[id] = (name, selectable, values, status);
        return this;
    }

    private readonly Dictionary<string, (string Name, bool Selectable, Dictionary<string, string> Values, string Status)> guides = [];

    /// <summary>USB-Helper's guide file as <see cref="Load"/> writes it.</summary>
    public string UsbHelperFile() => GuideFile("USB-Helper", true, true, UsbHelper, Platforms(), UsbHelperStatus);

    /// <summary>A guide file in the shape every guide must have, for <paramref name="platforms"/>.</summary>
    public static string GuideFile(string name, bool selectable, bool isDefault, IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>> platforms, string status)
    {
        // A global value: the key itself, a bus's hubs as usbN.hubs.x, tiers also as usb2.x or usb3.x.
        string? Global(string relative)
        {
            if (values.GetValueOrDefault(relative) is { } value)
            {
                return value;
            }
            var parts = relative.Split('.');
            if (parts[0] == "buses")
            {
                return values.GetValueOrDefault(string.Join('.', parts[1..])) ?? (isDefault ? Global(string.Join('.', parts[2..])) : null);
            }
            return parts[0] is "tiers"
                ? values.GetValueOrDefault("usb2." + relative) ?? values.GetValueOrDefault("usb3." + relative)
                : null;
        }
        var yaml = new System.Text.StringBuilder();
        void Leaf(string indent, string key, string? value) => yaml.Append($"{indent}{key}:{(value is { Length: > 0 } ? " " + value : "")}\n");
        void Pair(string indent, string key, Func<string, string?> value)
        {
            yaml.Append($"{indent}{key}:\n");
            Leaf(indent + "  ", "recommended", value($"{key}.recommended"));
            Leaf(indent + "  ", "max", value($"{key}.max"));
        }
        string? Filled(string relative) => Global(relative) ?? (!isDefault || relative == "exceeded_note" ? null : "none");
        yaml.Append($"name: {name}\nselectable: {(selectable ? "true" : "false")}\nproav: 1\nverification:\n  status: {status}\nlimits:\n");
        foreach (var kind in new[] { "hubs", "tiers" })
        {
            Pair("  ", kind, Filled);
        }
        yaml.Append("  buses:\n");
        foreach (var bus in new[] { "usb2", "usb3", "usb4" })
        {
            yaml.Append($"    {bus}:\n");
            Pair("      ", "hubs", relative => Filled($"buses.{bus}.{relative}"));
        }
        yaml.Append("  camera_audio:\n");
        Pair("    ", "tiers", relative => Filled("camera_audio." + relative));
        Leaf("  ", "exceeded_note", Global("exceeded_note"));
        var mst = values.GetValueOrDefault("mst") ?? (isDefault ? "0" : null);
        yaml.Append("platforms:\n");
        foreach (var (platform, chips) in platforms)
        {
            yaml.Append($"  {platform}:\n");
            Leaf("    ", "mst", mst);
            yaml.Append("    limits: {}\n    chips:\n");
            foreach (var chip in chips)
            {
                var prefix = $"platforms.{platform}.chips.{chip}.limits.";
                yaml.Append($"      {chip}:\n");
                Leaf("        ", "mst", values.GetValueOrDefault(prefix + "mst") ?? mst);
                var own = values.Where(value => value.Key.StartsWith(prefix, StringComparison.Ordinal) && value.Key != prefix + "mst")
                    .Select(value => (Path: value.Key[prefix.Length..], value.Value)).ToList();
                yaml.Append(own.Count == 0 ? "        limits: {}\n" : "        limits:\n");
                // A chip value such as tiers.max, usb2.hubs.max or camera_audio.tiers.max, nested; a pair gets both keys.
                var tree = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var (path, value) in own)
                {
                    var parts = path.Split('.');
                    if (parts[0] is "usb2" or "usb3" or "usb4")
                    {
                        parts = parts[1] == "hubs" ? ["buses", .. parts] : parts[1..];
                    }
                    var node = tree;
                    foreach (var part in parts[..^1])
                    {
                        node = (SortedDictionary<string, object>)(node.TryGetValue(part, out var child) ? child : node[part] = new SortedDictionary<string, object>(StringComparer.Ordinal));
                    }
                    node[parts[^1]] = value;
                    if (parts[^1] is "recommended" or "max")
                    {
                        node.TryAdd(parts[^1] == "max" ? "recommended" : "max", "");
                    }
                }
                void Emit(SortedDictionary<string, object> node, string indent)
                {
                    foreach (var (key, child) in node)
                    {
                        if (child is SortedDictionary<string, object> mapping)
                        {
                            yaml.Append($"{indent}{key}:\n");
                            Emit(mapping, indent + "  ");
                        }
                        else
                        {
                            Leaf(indent, key, (string)child);
                        }
                    }
                }
                Emit(tree, "          ");
            }
        }
        return yaml.ToString();
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
        void WriteFile(string id, string content)
        {
            if (!written.Contains($"guides/{id}.yaml"))
            {
                File.WriteAllText(Path.Combine(Folder, "guides", $"{id}.yaml"), content);
            }
        }
        if (WithDefaultGuide)
        {
            WriteFile("usb-helper", UsbHelperFile());
        }
        foreach (var (id, guide) in guides)
        {
            WriteFile(id, GuideFile(guide.Name, guide.Selectable, false, guide.Values, platforms, guide.Status));
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
