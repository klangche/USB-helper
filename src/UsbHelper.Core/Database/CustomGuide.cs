using System.Text;

namespace UsbHelper.Core.Database;

/// <summary>
/// The user's own guide (#300): one file, <c>custom.yaml</c>, kept outside the database in the same shape as every guide
/// in <c>guides/</c>, validated the same way, with USB-Helper's guide as the fallback for its empty values.
/// </summary>
public static class CustomGuide
{
    /// <summary>The guide's id, as the picked guide is kept in the settings.</summary>
    public const string Id = "custom";

    /// <summary>The file's name; also the file errors name.</summary>
    public const string FileName = "custom.yaml";

    /// <summary>The <c>name</c> the file must have: the menu lists the guide as Custom.</summary>
    public const string Name = "Custom";

    private const string Rule = "# =============================================================================";
    private const string ThinRule = "# -----------------------------------------------------------------------------";

    /// <summary>Reads <paramref name="yaml"/> as the custom guide against <paramref name="database"/>'s platforms and chips.</summary>
    /// <exception cref="DatabaseException">The file has one or more problems, each with its line in <c>custom.yaml</c>.</exception>
    public static Guide Load(HardwareDatabase database, string yaml) =>
        database.ReadGuide(yaml, FileName, Id, (root, errors) =>
        {
            if (root.Fields.GetValueOrDefault("name") is { Scalar: string name } field && name != Name)
            {
                errors.Add(new DatabaseError(FileName, field.Line, $"Field 'name' must be {Name}: the menu lists this guide as {Name}."));
            }
            if (root.Fields.GetValueOrDefault("selectable") is { Scalar: false } selectable)
            {
                errors.Add(new DatabaseError(FileName, selectable.Line, "Field 'selectable' must be true: Custom is always offered."));
            }
        });

    /// <summary>
    /// A blank custom guide: every platform and chip of <paramref name="database"/>, every value empty, so everything falls
    /// back to USB-Helper's guide until it is filled in, and <c>count</c> as USB-Helper's (tiers and hubs; #310), as it is
    /// required. It loads without errors.
    /// </summary>
    /// <param name="madeBy">The unlock email, written as the note "Made by &lt;email&gt;"; no note when null.</param>
    public static string Blank(HardwareDatabase database, string? madeBy = null)
    {
        var yaml = new StringBuilder();
        void Line(string text = "") => yaml.Append(text).Append('\n');

        Line(Rule);
        Line("# Custom  –  Your own guide");
        Line(Rule);
        Line("# Your own limits and recommendations. Saved as custom.yaml next to");
        Line("# settings.json and offered as Custom in \"USB philosophy\" while unlocked.");
        Line("#");
        Line("# How to edit");
        Line("#   - Leave a value empty  → fall back to the value above it, then to");
        Line("#                            USB-Helper's guide");
        Line("#   - Write a number       → that is the limit");
        Line("#   - Write \"none\"         → deliberately no limit");
        Line("#");
        Line("# Keys");
        Line("#   count           what this guide judges: any of chain, tiers, hubs");
        Line("#   link            the guide's page (http or https); optional");
        Line("#   hubs            external hubs counted per bus");
        Line("#   tiers           USB-IF tier count (host = tier 1)");
        Line("#   chain           hubs daisy-chained in front of a device, built-in hub");
        Line("#                   levels included (judged on each bus)");
        Line("#   buses           hubs per bus (usb2, usb3, usb4)");
        Line("#   camera_audio    stricter tiers and chain for isochronous devices (webcams, mics)");
        Line("#   mst             1: the platform or chip extends the desktop over MST, 0: no");
        Line(Rule);
        Line();
        Line($"name: {Name}");
        Line("selectable: true");
        Line("proav: 0                              # 1: ProAV guide (marked in the menu), 0: general purpose");
        Line("count: [tiers, hubs]                  # what this guide judges: any of chain, tiers, hubs");
        Line("link:");
        if (madeBy != null)
        {
            Line("notes:");
            Line($"  - \"Made by {madeBy.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"");
        }
        Line();
        Line();
        Line(ThinRule);
        Line("# Global defaults");
        Line(ThinRule);
        Limits(yaml, "");
        var first = true;
        foreach (var platform in database.Platforms.Values.GroupBy(profile => profile.Platform))
        {
            Line();
            Line();
            Line(Rule);
            Line($"# {platform.First().PlatformName.ToUpperInvariant()}");
            Line(Rule);
            if (first)
            {
                Line("platforms:");
                Line();
                first = false;
            }
            Line($"  {platform.Key}:");
            Line("    mst:");
            Limits(yaml, "    ");
            Line();
            Line("    chips:");
            foreach (var chip in platform)
            {
                Line($"      {chip.Chip}:                     # {chip.ChipName}");
                Line("        mst:");
                Limits(yaml, "        ");
                Line();
            }
        }
        return yaml.ToString();
    }

    // A limits block with every key and every value empty.
    private static void Limits(StringBuilder yaml, string indent)
    {
        void Line(string text) => yaml.Append(indent).Append(text).Append('\n');
        void Pair(string pad, string key)
        {
            Line($"{pad}{key}:");
            Line($"{pad}  recommended:");
            Line($"{pad}  max:");
        }

        Line("limits:");
        Pair("  ", "hubs");
        Pair("  ", "tiers");
        Pair("  ", "chain");
        Line("  buses:");
        foreach (var bus in new[] { "usb2", "usb3", "usb4" })
        {
            Line($"    {bus}:");
            Pair("      ", "hubs");
        }
        Line("  camera_audio:");
        Pair("    ", "tiers");
        Pair("    ", "chain");
        Line("  exceeded_note:");
    }
}
