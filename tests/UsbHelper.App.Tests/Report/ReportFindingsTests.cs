using UsbHelper.App.Report;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Report;

// #247: the findings in the report read as plain sentences: no database fields, no file names.
public class ReportFindingsTests
{
    // The checked profiles' files, as the view model passes them; names with parentheses included.
    private static readonly string[] Files =
    [
        "base.yaml", "computers/dell/latitude-7450.yaml", "computers/c.yaml", "platforms/windows-intel.yaml",
        "computers/Dell XPS (1).yaml", "computers/computer (1).yaml", "platforms/win (1).yaml",
    ];

    [Theory]
    [InlineData("limits.max_hubs = 5 (computers/dell/latitude-7450.yaml).", "External hubs: 5 (Hardware database).")]
    [InlineData("displays.max_external = 2 (computers/c.yaml).", "External displays: 2 (Hardware database).")]
    [InlineData("limits.max_hops = 5 comes from an unverified profile (platforms/windows-intel.yaml); it is not confirmed.",
        "Most hops: 5 (Hardware database, unverified).")]
    [InlineData("limits.max_hubs is unknown (platforms/windows-intel.yaml).", "External hubs: unknown (Hardware database).")]
    [InlineData("limits.max_tiers is unknown.", "Deepest tier: unknown.")]
    [InlineData("Hop depth 3 exceeds limits.max_hops 2 (unverified limit).", "Hop depth 3 exceeds the most hops limit of 2 (Hardware database, unverified).")]
    [InlineData("Tier depth 6 exceeds limits.max_tiers 5.", "Tier depth 6 exceeds the deepest tier limit of 5.")]
    [InlineData("Hop depth 3 exceeds the recommended limits.recommended_hops 2; limits.max_hops is 5.",
        "Hop depth 3 exceeds the recommended hops limit of 2; the most hops limit is 5.")]
    [InlineData("3 external hubs are connected; limits.max_hubs is 2.", "3 external hubs are connected; the external hubs limit is 2.")]
    [InlineData("3 external displays are active; displays.max_external is 2.", "3 external displays are active; the external displays limit is 2.")]
    [InlineData("3 external displays are active; displays.max_external is 2 (unverified limit).",
        "3 external displays are active; the external displays limit is 2 (Hardware database, unverified).")]
    [InlineData("Not on the left port (unverified, computers/c.yaml)", "Not on the left port (Hardware database, unverified)")]
    [InlineData("2 DisplayPort displays are active; whether Dell Latitude 7450 supports MST (displays.mst) is unknown (computers/c.yaml).",
        "2 DisplayPort displays are active; whether Dell Latitude 7450 supports MST is unknown (Hardware database).")]
    [InlineData("No MST on X: this display is duplicated; use a second USB-C port or DisplayLink. (displays.mst comes from an unverified profile, computers/c.yaml)",
        "No MST on X: this display is duplicated; use a second USB-C port or DisplayLink. (Hardware database, unverified)")]
    [InlineData("Disconnected and reconnected during this session.", "Disconnected and reconnected during this session.")]
    // File names with parentheses, from computer and platform profiles, verified and unverified.
    [InlineData("limits.max_hubs = 4 (computers/Dell XPS (1).yaml).", "External hubs: 4 (Hardware database).")]
    [InlineData("Not on left (unverified, computers/computer (1).yaml)", "Not on left (Hardware database, unverified)")]
    [InlineData("limits.max_hops = 5 comes from an unverified profile (platforms/win (1).yaml); it is not confirmed.",
        "Most hops: 5 (Hardware database, unverified).")]
    [InlineData("limits.max_hubs is unknown (platforms/win (1).yaml).", "External hubs: unknown (Hardware database).")]
    [InlineData("displays.max_external = 2 comes from an unverified profile (computers/Dell XPS (1).yaml); it is not confirmed.",
        "External displays: 2 (Hardware database, unverified).")]
    [InlineData("No MST on X: this display is duplicated. (displays.mst comes from an unverified profile, computers/Dell XPS (1).yaml)",
        "No MST on X: this display is duplicated. (Hardware database, unverified)")]
    public void Messages_name_the_check_and_the_hardware_database_not_fields_and_files(string message, string expected)
    {
        Assert.Equal(expected, ReportFindings.Readable(message, Files));
    }

    // The safety net: a file the view model did not pass never reaches the report either.
    [Theory]
    [InlineData("limits.max_hubs = 4 (computers/Unknown (2).yaml).")]
    [InlineData("Not on left (unverified, computers/other (1).YAML)")]
    [InlineData("Read from computers/x.yml and platforms/y.yaml.")]
    [InlineData("limits.max_hops = 5 comes from an unverified profile (computers/a b (3).yml); it is not confirmed.")]
    [InlineData("(displays.mst comes from an unverified profile, computers/z (1) (2).yaml)")]
    public void No_finding_text_ever_names_a_yaml_file(string message)
    {
        foreach (var files in new string[][] { [], Files })
        {
            var readable = ReportFindings.Readable(message, files);

            Assert.DoesNotContain(".yaml", readable, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".yml", readable, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Hardware database", readable);
        }
    }

    [Fact]
    public void Identical_findings_are_listed_once_with_their_profiles()
    {
        var findings = ReportFindings.Compact(
        [
            new(FactKind.VerifiedDatabaseFact, "Dell Latitude 7450", "Computer", "limits.max_hubs = 4 (computers/a.yaml)."),
            new(FactKind.DetectedFact, null, "Jabra Speak2 75", "Disconnected and reconnected during this session."),
            new(FactKind.VerifiedDatabaseFact, "HP EliteBook 840 G10", "Computer", "limits.max_hubs = 4 (computers/b.yaml)."),
            new(FactKind.DetectedFact, null, "Jabra Speak2 75", "Disconnected and reconnected during this session."),
        ], ["computers/a.yaml", "computers/b.yaml"]);

        Assert.Equal(
        [
            new(FactKind.VerifiedDatabaseFact, "Dell Latitude 7450, HP EliteBook 840 G10", "Computer", "External hubs: 4 (Hardware database)."),
            new(FactKind.DetectedFact, null, "Jabra Speak2 75", "Disconnected and reconnected during this session."),
        ], findings);
    }
}
