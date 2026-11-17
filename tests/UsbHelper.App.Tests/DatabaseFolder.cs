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
    /// The platform profile's limits in USB-Helper's guide, as <c>max_hops: 0</c>, <c>recommended_hops: 0</c> or
    /// <c>max_hubs: 1</c> lines; every other limit of it is <c>none</c>. The computer's are hubs 5, tiers 7 and hops 5.
    /// </param>
    /// <param name="name">The computer's human-readable <c>name</c>; none when null.</param>
    /// <param name="platform">The platform profile's id: the platform file's name, a dash, and the chip.</param>
    /// <param name="mst">The computer's <c>displays.mst</c>; not set when null.</param>
    /// <param name="computerFile">The computer profile's file name without extension.</param>
    /// <param name="platformFile">The platform file's name without extension; the part of the id before the dash when null.</param>
    /// <param name="status">The verification status of the computer file and of USB-Helper's guide.</param>
    public static HardwareDatabase Load(
        string manufacturer, string model, string status, string? platformLimits = null, string? name = null, string platform = "os-p",
        string? mst = null, string computerFile = "c", string? platformFile = null)
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
                pair => pair[0] switch { "max_hops" => "hops.max", "recommended_hops" => "hops.recommended", "max_hubs" => "hubs.max", var other => other },
                pair => pair[1].Trim());
        Directory.CreateDirectory(Path.Combine(folder, "guides", "usb-helper"));
        foreach (var (name, content) in UsbHelper(file, chip, limits, status))
        {
            File.WriteAllText(Path.Combine(folder, "guides", "usb-helper", name), content);
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

    // USB-Helper's guide folder in the shape every guide has: the platform chip with the limits given (none otherwise),
    // the computer's chip and all.yaml with hubs 5, tiers 7 and hops 5, the same on USB 2 and USB 3.
    private static Dictionary<string, string> UsbHelper(string platform, string chip, IReadOnlyDictionary<string, string> limits, string status)
    {
        var computer = new Dictionary<string, string> { ["hubs.max"] = "5", ["tiers.max"] = "7", ["hops.max"] = "5" };
        static string Block(string indent, IReadOnlyDictionary<string, string> values)
        {
            var yaml = new StringBuilder();
            foreach (var bus in new[] { "usb2", "usb3", "usb4" })
            {
                yaml.Append($"{indent}{bus}:\n");
                foreach (var kind in new[] { "hubs", "tiers", "hops" })
                {
                    yaml.Append($"{indent}  {kind}:\n");
                    foreach (var which in new[] { "recommended", "max" })
                    {
                        yaml.Append($"{indent}    {which}: {(bus == "usb4" ? "none" : values.GetValueOrDefault($"{kind}.{which}", "none"))}\n");
                    }
                }
            }
            yaml.Append($"{indent}camera_audio:\n");
            foreach (var kind in new[] { "tiers", "hops" })
            {
                yaml.Append($"{indent}  {kind}:\n{indent}    recommended: none\n{indent}    max: none\n");
            }
            return yaml.Append($"{indent}mst: 0\n{indent}exceeded_note:\n").ToString();
        }
        return new()
        {
            ["guide.yaml"] = $"name: USB-Helper\nselectable: true\nverification:\n  status: {status}\n",
            ["all.yaml"] = Block("", computer),
            [$"{platform}.yaml"] = $"{chip}:\n{Block("  ", limits)}computer:\n{Block("  ", computer)}",
        };
    }

    /// <summary>The platform profile's option, not the one of the computer's chip.</summary>
    public static ProfileOption Platform(this ObservableCollection<ProfileOption> operatingSystems) =>
        operatingSystems.Single(option => option.Profile.Chip != "computer");
}
