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
        File.WriteAllText(Path.Combine(folder, "guides", "usb-helper.yaml"), UsbHelper(file, chip, limits, status));
        try
        {
            return HardwareDatabase.Load(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // USB-Helper's guide in the shape every guide file has: the platform chip with the limits given (none otherwise),
    // the computer's chip and the other levels with hubs 5, tiers 7 and hops 5.
    private static string UsbHelper(string platform, string chip, IReadOnlyDictionary<string, string> limits, string status)
    {
        var computer = new Dictionary<string, string> { ["hubs.max"] = "5", ["tiers.max"] = "7", ["hops.max"] = "5" };
        var yaml = new StringBuilder("name: USB-Helper\nselectable: true\n");
        void Block(string indent, IReadOnlyDictionary<string, string> values)
        {
            yaml.Append($"{indent}limits:\n");
            foreach (var kind in new[] { "hubs", "tiers", "hops", "camera_audio.tiers", "camera_audio.hops" })
            {
                var depth = kind.StartsWith("camera_audio", StringComparison.Ordinal) ? "    " : "  ";
                if (kind == "camera_audio.tiers")
                {
                    yaml.Append($"{indent}  camera_audio:\n");
                }
                yaml.Append($"{indent}{depth}{kind.Split('.')[^1]}:\n");
                foreach (var which in new[] { "recommended", "max" })
                {
                    yaml.Append($"{indent}{depth}  {which}: {values.GetValueOrDefault($"{kind}.{which}", "none")}\n");
                }
            }
            yaml.Append($"{indent}  exceeded_note:\n");
        }
        Block("", computer);
        yaml.Append($"platforms:\n  \"{platform}\":\n    mst: 0\n");
        Block("    ", computer);
        yaml.Append($"    chips:\n      {chip}:\n        mst: 0\n");
        Block("        ", limits);
        yaml.Append("      computer:\n        mst: 0\n");
        Block("        ", computer);
        return yaml.Append($"verification:\n  status: {status}\n").ToString();
    }

    /// <summary>The platform profile's option, not the one of the computer's chip.</summary>
    public static ProfileOption Platform(this ObservableCollection<ProfileOption> operatingSystems) =>
        operatingSystems.Single(option => option.Profile.Chip != "computer");
}
