using System.Globalization;
using System.Text.RegularExpressions;
using UsbHelper.App.Resources;

namespace UsbHelper.App.Report;

/// <summary>
/// The findings as the report lists them (#247): the check names instead of the database fields, "Hardware database"
/// instead of the profile files, and each finding once.
/// </summary>
public static partial class ReportFindings
{
    private static readonly Dictionary<string, string> Checks = new()
    {
        ["limits.max_hubs"] = Strings.LimitHubs,
        ["limits.max_tiers"] = Strings.LimitTiers,
        ["limits.max_hops"] = Strings.LimitHops,
        ["limits.recommended_hops"] = Strings.LimitRecommendedHops,
        ["displays.max_external"] = Strings.LimitDisplays,
    };

    /// <summary>
    /// The findings with readable messages; identical ones are listed once, with the profiles they apply to, in the
    /// order they first appear.
    /// </summary>
    public static IReadOnlyList<ReportFinding> Compact(IEnumerable<ReportFinding> findings) =>
    [
        .. findings
            .Select(finding => finding with { Message = Readable(finding.Message) })
            .GroupBy(finding => (finding.Kind, finding.Subject, finding.Message))
            .Select(group => group.First() with
            {
                Profile = group.Select(finding => finding.Profile).OfType<string>().Distinct().ToList() is { Count: > 0 } profiles
                    ? string.Join(", ", profiles)
                    : null,
            }),
    ];

    /// <summary>A finding's message with the check names and "Hardware database" in place of the fields and files.</summary>
    public static string Readable(string message)
    {
        string Name(Match match) => Checks[match.Groups["field"].Value];
        string Lower(Match match) => Name(match).ToLowerInvariant();
        message = UnverifiedValue().Replace(message, match =>
            $"{Name(match)}: {match.Groups["value"].Value} ({Strings.ReportSourceUnverified}).");
        message = LeadingValue().Replace(message, match => $"{Name(match)}: ");
        message = LeadingUnknown().Replace(message, match => $"{Name(match)}: unknown");
        message = LimitOf().Replace(message, match =>
            string.Format(CultureInfo.InvariantCulture, Strings.ReportLimitOf, Lower(match), match.Groups["value"].Value));
        message = LimitIs().Replace(message, match =>
            string.Format(CultureInfo.InvariantCulture, Strings.ReportLimitIs, Lower(match), match.Groups["value"].Value));
        message = MstField().Replace(message, "");
        message = Source().Replace(message, match => match.Groups["inner"].Value.Contains("unverified", StringComparison.Ordinal)
            ? $"({Strings.ReportSourceUnverified})"
            : $"({Strings.ReportSourceDatabase})");
        return File().Replace(message, Strings.ReportSourceDatabase);
    }

    private const string Field = @"(?<field>limits\.max_hubs|limits\.max_tiers|limits\.max_hops|limits\.recommended_hops|displays\.max_external)";

    // "limits.max_hops = 5 comes from an unverified profile (platforms/p.yaml); it is not confirmed."
    [GeneratedRegex("^" + Field + @" = (?<value>\d+) comes from an unverified profile(?: \([^()]*\))?; it is not confirmed\.")]
    private static partial Regex UnverifiedValue();

    [GeneratedRegex("^" + Field + " = ")]
    private static partial Regex LeadingValue();

    [GeneratedRegex("^" + Field + " is unknown")]
    private static partial Regex LeadingUnknown();

    // "exceeds limits.max_hops 2", "exceeds the recommended limits.recommended_hops 2".
    [GeneratedRegex("(?:the recommended )?" + Field + @" (?<value>\d+)")]
    private static partial Regex LimitOf();

    // "; limits.max_hops is 5".
    [GeneratedRegex(Field + @" is (?<value>\d+|unknown)")]
    private static partial Regex LimitIs();

    // "supports MST (displays.mst) is unknown".
    [GeneratedRegex(@" \(displays\.mst\)")]
    private static partial Regex MstField();

    // "(computers/c.yaml)", "(unverified, computers/c.yaml)", "(displays.mst comes from an unverified profile, computers/c.yaml)".
    [GeneratedRegex(@"\((?<inner>[^()]*?)[\w./\\-]+\.ya?ml\)")]
    private static partial Regex Source();

    [GeneratedRegex(@"[\w./\\-]+\.ya?ml")]
    private static partial Regex File();
}
