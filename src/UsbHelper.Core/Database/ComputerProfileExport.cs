using System.Globalization;
using System.Text;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Topology;

namespace UsbHelper.Core.Database;

/// <summary>
/// Writes this computer as a computer profile for the developers: what was detected, its platform and chip family
/// (it inherits their facts; limits are in the guide files) and the status unverified. It loads as a valid profile; its id is computed from manufacturer and model.
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
            $"Detected: USB controllers: {tree.Count}, external hubs: {TopologyCounts.PerBus(tree).HubsText}, active external displays: {displays}.",
        };
        // Numbered, not named: Windows names controllers in the user's language, the notes are in English.
        notes.AddRange(tree.Select((controller, index) =>
            $"USB controller {index + 1}: root hub ports: {controller.Children.Sum(rootHub => rootHub.Children.Count)}."));

        var yaml = new StringBuilder();
        yaml.AppendLine("# Computer profile exported by USB-helper. Send this file to the USB-helper developers.");
        // The detected platform profile id is the platform file and the chip, for example "macos-apple-silicon"; the
        // detected ids (windows-*, macos-*) have no dash in the platform part.
        var dash = computer.Platform?.IndexOf('-') ?? -1;
        if (dash <= 0)
        {
            yaml.AppendLine("# Choose 'platform' (a file in v3/platforms/ on the database branch) and 'chip_family' (one of its chips).");
        }
        yaml.AppendLine($"platform: {Text(dash > 0 ? computer.Platform![..dash] : null)}");
        yaml.AppendLine($"chip_family: {Text(dash > 0 ? computer.Platform![(dash + 1)..] : null)}");
        yaml.AppendLine($"manufacturer: {Text(computer.Manufacturer)}");
        yaml.AppendLine($"model: {Text(computer.Model)}");
        yaml.AppendLine($"chip: {Text(computer.Chip)}");
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

    /// <summary>
    /// <c>&lt;manufacturer&gt;-&lt;model&gt;.yaml</c>, with every character a file name cannot hold replaced by a dash;
    /// <c>computer.yaml</c> when nothing is left. The save dialog lets the user change it.
    /// </summary>
    public static string FileName(ComputerIdentity computer)
    {
        var name = new StringBuilder();
        foreach (var c in $"{computer.Manufacturer} {computer.Model}")
        {
            name.Append(char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '-');
        }
        var collapsed = string.Join('-', name.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Trim('.').Length == 0 ? "computer.yaml" : $"{collapsed}.yaml";
    }

    // Double-quoted YAML text, or null. Control characters, line separators and the byte order mark are written as
    // \uXXXX escapes: written as is, they would break the line structure or be dropped by the parser.
    private static string Text(string? value)
    {
        if (value == null)
        {
            return "null";
        }
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            text.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                _ when char.IsControl(c) || c is '\u2028' or '\u2029' or '\uFEFF' => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }
        return text.Append('"').ToString();
    }
}
