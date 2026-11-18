using System.Collections.ObjectModel;
using System.Text;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;

namespace UsbHelper.App.Tests;

/// <summary>
/// A minimal valid database with one platform profile ("os-p" unless named) and one computer profile on another chip of
/// the same platform, with USB-Helper's guide, loaded from a temporary folder that is then deleted.
/// </summary>
internal static class DatabaseFolder
{
    /// <param name="platformLimits">
    /// The platform profile's limits in USB-Helper's guide, as <c>max_tiers: 2</c>, <c>recommended_tiers: 2</c> or
    /// <c>max_hubs: 1</c> lines; every other limit of it is <c>none</c>. The computer's are hubs 5 and tiers 7.
    /// </param>
    /// <param name="name">The computer's human-readable <c>name</c>; none when null.</param>
    /// <param name="platform">The platform profile's id: the platform file's name, a dash, and the chip.</param>
    /// <param name="mst">The computer's <c>displays.mst</c>; not set when null.</param>
    /// <param name="computerFile">The computer profile's file name without extension.</param>
    /// <param name="platformFile">The platform file's name without extension; the part of the id before the dash when null.</param>
    /// <param name="status">The verification status of the computer file and of USB-Helper's guide.</param>
    /// <param name="guides">More guides, by id: their name and whether they are selectable; every value in them empty.</param>
    /// <param name="count">What USB-Helper's guide counts (#310); <c>max_chain: 1</c> in <paramref name="platformLimits"/> sets a chain.</param>
    public static HardwareDatabase Load(
        string manufacturer, string model, string status, string? platformLimits = null, string? name = null, string platform = "os-p",
        string? mst = null, string computerFile = "c", string? platformFile = null,
        IReadOnlyDictionary<string, (string Name, bool Selectable)>? guides = null, string count = "[tiers, hubs]")
    {
        var dash = platform.IndexOf('-');
        var (file, chip) = (platformFile ?? platform[..dash], platform[(dash + 1)..]);
        var folder = Directory.CreateTempSubdirectory("usb-helper-app-").FullName;
        foreach (var subfolder in new[] { "platforms", "computers", "guides" })
        {
            Directory.CreateDirectory(Path.Combine(folder, subfolder));
        }
        File.WriteAllText(Path.Combine(folder, "base.yaml"), "schema_version: 2\nusb:\n  max_tiers: 7\n  max_devices_per_controller: 127\n");
        File.WriteAllText(Path.Combine(folder, "platforms", $"{file}.yaml"),
            $"name: \"{file}\"\nchips:\n  {chip}: {{ name: {chip}, built_in_hub_levels: 0 }}\n  computer: {{ name: Computer, built_in_hub_levels: 0 }}\n");
        File.WriteAllText(Path.Combine(folder, "computers", $"{computerFile}.yaml"), $"""
            platform: {file}
            chip_family: computer
            manufacturer: {manufacturer}
            model: {model}
            {(name == null ? "" : $"name: {name}")}
            displays:
              max_external: 2
              {(mst == null ? "" : $"mst: {mst}")}
            verification:
              status: {status}
            """);
        var limits = (platformLimits ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split(':', 2))
            .ToDictionary(
                pair => pair[0] switch { "max_tiers" => "tiers.max", "recommended_tiers" => "tiers.recommended", "max_hubs" => "hubs.max", "max_chain" => "chain.max", var other => other },
                pair => pair[1].Trim());
        File.WriteAllText(Path.Combine(folder, "guides", "usb-helper.yaml"), UsbHelper(file, chip, limits, status, count));
        foreach (var (id, (guideName, selectable)) in guides ?? new Dictionary<string, (string, bool)>())
        {
            File.WriteAllText(Path.Combine(folder, "guides", $"{id}.yaml"), $$"""
                name: {{guideName}}
                selectable: {{(selectable ? "true" : "false")}}
                proav: 1
                count: [tiers, hubs]
                limits: {}
                platforms:
                  {{file}}:
                    mst:
                    limits: {}
                    chips:
                      {{chip}}: { mst: , limits: {} }
                      computer: { mst: , limits: {} }
                """);
        }
        try
        {
            return HardwareDatabase.Load(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // USB-Helper's guide file in the shape every guide has: the platform chip with the limits given (none otherwise),
    // the global limits (which the computer's chip uses) hubs 5 and tiers 7, hubs 5 on USB 2 and USB 3.
    private static string UsbHelper(string platform, string chip, IReadOnlyDictionary<string, string> limits, string status, string count)
    {
        var computer = new Dictionary<string, string> { ["hubs.max"] = "5", ["tiers.max"] = "7" };
        static string Block(string indent, IReadOnlyDictionary<string, string> values, bool buses)
        {
            var yaml = new StringBuilder();
            void Pair(string pad, string key, string path) => yaml.Append($"{indent}{pad}{key}:\n")
                .Append($"{indent}{pad}  recommended: {values.GetValueOrDefault($"{path}.recommended", "none")}\n")
                .Append($"{indent}{pad}  max: {values.GetValueOrDefault($"{path}.max", "none")}\n");
            foreach (var kind in new[] { "hubs", "tiers", "chain" })
            {
                Pair("", kind, kind);
            }
            if (buses)
            {
                yaml.Append($"{indent}buses:\n");
                foreach (var bus in new[] { "usb2", "usb3", "usb4" })
                {
                    yaml.Append($"{indent}  {bus}:\n");
                    Pair("    ", "hubs", bus == "usb4" ? "none" : "hubs");
                }
            }
            yaml.Append($"{indent}camera_audio:\n");
            Pair("  ", "tiers", "camera_audio.tiers");
            Pair("  ", "chain", "camera_audio.chain");
            return yaml.Append($"{indent}exceeded_note:\n").ToString();
        }
        return $"name: USB-Helper\nselectable: true\nproav: 1\ncount: {count}\nlink:\nverification:\n  status: {status}\nlimits:\n{Block("  ", computer, buses: true)}"
            + $"platforms:\n  {platform}:\n    mst: 0\n    limits: {{}}\n    chips:\n"
            + $"      {chip}:\n        mst: 0\n        limits:\n{Block("          ", limits, buses: false)}"
            + "      computer:\n        mst: 0\n        limits: {}\n";
    }

    /// <summary>The platform profile's option, not the one of the computer's chip.</summary>
    public static ProfileOption Platform(this ObservableCollection<ProfileOption> operatingSystems) =>
        operatingSystems.Single(option => option.Profile.Chip != "computer");
}
