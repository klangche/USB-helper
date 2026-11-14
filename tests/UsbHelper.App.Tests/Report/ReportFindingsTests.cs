using UsbHelper.App.Report;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests.Report;

// #247: the findings in the report read as plain sentences: no database fields, no file names.
public class ReportFindingsTests
{
    [Theory]
    [InlineData("limits.max_hubs = 5 (computers/dell/latitude-7450.yaml).", "External hubs: 5 (Hardware database).")]
    [InlineData("displays.max_external = 2 (computers/c.yaml).", "External displays: 2 (Hardware database).")]
    [InlineData("limits.max_hops = 5 comes from an unverified profile (platforms/windows-intel.yaml); it is not confirmed.",
        "Most hops: 5 (Hardware database, unverified).")]
    [InlineData("limits.max_hubs is unknown (platforms/windows-intel.yaml).", "External hubs: unknown (Hardware database).")]
    [InlineData("limits.max_tiers is unknown.", "Deepest tier: unknown.")]
    [InlineData("Hop depth 3 exceeds limits.max_hops 2 (unverified limit).", "Hop depth 3 exceeds the most hops limit of 2 (unverified limit).")]
    [InlineData("Tier depth 6 exceeds limits.max_tiers 5.", "Tier depth 6 exceeds the deepest tier limit of 5.")]
    [InlineData("Hop depth 3 exceeds the recommended limits.recommended_hops 2; limits.max_hops is 5.",
        "Hop depth 3 exceeds the recommended hops limit of 2; the most hops limit is 5.")]
    [InlineData("3 external hubs are connected; limits.max_hubs is 2.", "3 external hubs are connected; the external hubs limit is 2.")]
    [InlineData("3 external displays are active; displays.max_external is 2.", "3 external displays are active; the external displays limit is 2.")]
    [InlineData("Not on the left port (unverified, computers/c.yaml)", "Not on the left port (Hardware database, unverified)")]
    [InlineData("2 DisplayPort displays are active; whether Dell Latitude 7450 supports MST (displays.mst) is unknown (computers/c.yaml).",
        "2 DisplayPort displays are active; whether Dell Latitude 7450 supports MST is unknown (Hardware database).")]
    [InlineData("No MST on X: this display is duplicated; use a second USB-C port or DisplayLink. (displays.mst comes from an unverified profile, computers/c.yaml)",
        "No MST on X: this display is duplicated; use a second USB-C port or DisplayLink. (Hardware database, unverified)")]
    [InlineData("Disconnected and reconnected during this session.", "Disconnected and reconnected during this session.")]
    public void Messages_name_the_check_and_the_hardware_database_not_fields_and_files(string message, string expected)
    {
        Assert.Equal(expected, ReportFindings.Readable(message));
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
        ]);

        Assert.Equal(
        [
            new(FactKind.VerifiedDatabaseFact, "Dell Latitude 7450, HP EliteBook 840 G10", "Computer", "External hubs: 4 (Hardware database)."),
            new(FactKind.DetectedFact, null, "Jabra Speak2 75", "Disconnected and reconnected during this session."),
        ], findings);
    }
}
