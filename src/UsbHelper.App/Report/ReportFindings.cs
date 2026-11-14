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
    // Stands in for a known database file while the message is rewritten, whatever characters the file name has.
    private const string KnownFile = "\u0001";

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
    /// order they first appear. <paramref name="files"/> are the database files the messages may name.
    /// </summary>
    public static IReadOnlyList<ReportFinding> Compact(IEnumerable<ReportFinding> findings, IReadOnlyCollection<string> files) =>
    [
        .. findings
            .Select(finding => finding with { Message = Readable(finding.Message, files) })
            .GroupBy(finding => (finding.Kind, finding.Subject, finding.Message))
            .Select(group => group.First() with
            {
                Profile = group.Select(finding => finding.Profile).OfType<string>().Distinct().ToList() is { Count: > 0 } profiles
                    ? string.Join(", ", profiles)
                    : null,
            }),
    ];

    /// <summary>
    /// A finding's message with the check names and "Hardware database" in place of the fields and of
    /// <paramref name="files"/>, the database files it may name. Any other name ending in .yaml or .yml is replaced too,
    /// so no file name reaches the report.
    /// </summary>
    public static string Readable(string message, IReadOnlyCollection<string> files)
    {
        foreach (var file in files.Where(file => file.Length > 0).OrderByDescending(file => file.Length))
        {
            message = message.Replace(file, KnownFile, StringComparison.Ordinal);
        }
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
        message = Source().Replace(message, match => Cited(match.Groups["inner"].Value));
        message = UnverifiedLimit().Replace(message, $"({Strings.ReportSourceUnverified})");
        message = message.Replace(KnownFile, Strings.ReportSourceDatabase, StringComparison.Ordinal);
        // The safety net, for a file the caller did not name.
        message = UnknownSource().Replace(message, match => Cited(match.Value));
        return UnknownFile().Replace(message, Strings.ReportSourceDatabase);
    }

    private static string Cited(string text) => text.Contains("unverified", StringComparison.Ordinal)
        ? $"({Strings.ReportSourceUnverified})"
        : $"({Strings.ReportSourceDatabase})";

    private const string Field = @"(?<field>limits\.max_hubs|limits\.max_tiers|limits\.max_hops|limits\.recommended_hops|displays\.max_external)";

    // "limits.max_hops = 5 comes from an unverified profile (platforms/p.yaml); it is not confirmed."
    [GeneratedRegex("^" + Field + @" = (?<value>\d+) comes from an unverified profile(?: \(\x01\))?; it is not confirmed\.")]
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

    // "(file)", "(unverified, file)", "(displays.mst comes from an unverified profile, file)".
    [GeneratedRegex(@"\((?<inner>[^()]*?)\x01\)")]
    private static partial Regex Source();

    // "exceeds limits.max_hops 2 (unverified limit)".
    [GeneratedRegex(@"\(unverified limit\)")]
    private static partial Regex UnverifiedLimit();

    // A parenthesis that ends in a file name, which may have parentheses of its own.
    [GeneratedRegex(@"\([^()]*(?:\([^()]*\)[^()]*)*\.ya?ml\)", RegexOptions.IgnoreCase)]
    private static partial Regex UnknownSource();

    [GeneratedRegex(@"\S*\.ya?ml\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnknownFile();
}
