using System.Globalization;
using System.Text;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Database;

/// <summary>
/// Writes this computer as a computer profile for the developers: what was detected, every limit unknown and the
/// status unverified. It loads as a valid profile; its id is computed from manufacturer and model.
/// </summary>
public static class ComputerProfileExport
{
    /// <param name="computer">Manufacturer and model must be known.</param>
    /// <param name="externalDisplays">Active external displays; null when unknown.</param>
    public static string ToYaml(ComputerIdentity computer, IReadOnlyList<UsbNode> tree, int? externalDisplays, string appVersion, DateOnly date)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var displays = externalDisplays?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        var notes = new List<string>
        {
            $"Exported by USB-helper {appVersion} on {day}.",
            $"Detected: {tree.Count} USB controllers, {TopologyCounts.HubCount(tree)} external hubs, active external displays: {displays}.",
        };
        notes.AddRange(tree.Select(controller =>
            $"USB controller {controller.Name}: {controller.Children.Sum(rootHub => rootHub.Children.Count)} root hub ports."));

        var yaml = new StringBuilder();
        yaml.AppendLine("# Computer profile exported by USB-helper. Send this file to the USB-helper developers.");
        if (computer.Platform == null)
        {
            yaml.AppendLine("# Choose 'inherits' from database/platforms/.");
        }
        yaml.AppendLine($"inherits: {Text(computer.Platform)}");
        yaml.AppendLine($"manufacturer: {Text(computer.Manufacturer)}");
        yaml.AppendLine($"model: {Text(computer.Model)}");
        yaml.AppendLine($"chip: {Text(computer.Chip)}");
        yaml.AppendLine("limits:");
        yaml.AppendLine("  max_hubs: null");
        yaml.AppendLine("  max_tiers: null");
        yaml.AppendLine("  max_hops: null");
        yaml.AppendLine("displays:");
        yaml.AppendLine("  max_external: null");
        yaml.AppendLine("notes:");
        foreach (var note in notes)
        {
            yaml.AppendLine($"  - {Text(note)}");
        }
        yaml.AppendLine("sources: []");
        yaml.AppendLine("verification:");
        yaml.AppendLine("  status: unverified");
        yaml.AppendLine("  last_checked: null");
        return yaml.ToString();
    }

    /// <summary><c>&lt;manufacturer&gt;-&lt;model&gt;.yaml</c>, with every character a file name cannot hold replaced by a dash.</summary>
    public static string FileName(ComputerIdentity computer)
    {
        var name = new StringBuilder();
        foreach (var c in $"{computer.Manufacturer} {computer.Model}")
        {
            name.Append(char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '-');
        }
        var collapsed = string.Join('-', name.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return $"{collapsed}.yaml";
    }

    // Double-quoted YAML text, or null.
    private static string Text(string? value) =>
        value == null ? "null" : $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
