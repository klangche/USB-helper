using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Report;

/// <summary>One problem and the checked profiles it applies to: the same check failing with the same limit and result.</summary>
public sealed record ReportLimitation(string Check, long? Limit, int? Measured, ConnectionStatus Status, IReadOnlyList<string> Profiles);

/// <summary>A check that cannot be judged, and the checked profiles it applies to.</summary>
/// <param name="InDatabase">The limit is known but the value could not be counted (a hub could not be read, or the display
/// count is unknown); false when the limit is not in the database.</param>
public sealed record ReportUnknownLimit(string Check, bool InDatabase, IReadOnlyList<string> Profiles);

/// <summary>
/// The report's Results section (#231): stable for everything except the problems, which are grouped over the checked
/// profiles. A check that cannot be judged (its limit is not in the database, or it could not be counted) is neither
/// stable nor a problem.
/// </summary>
public static class ReportResults
{
    /// <summary>Every check that is neither stable nor unknown, grouped, worst first.</summary>
    public static IReadOnlyList<ReportLimitation> Limitations(IEnumerable<ReportProfile> profiles) =>
    [
        .. Checks(profiles)
            .Where(item => item.Check.Status is not (ConnectionStatus.Green or ConnectionStatus.Grey))
            .GroupBy(item => item.Check)
            .OrderByDescending(group => group.Key.Status)
            .Select(group => new ReportLimitation(group.Key.Name, group.Key.Limit, group.Key.Measured, group.Key.Status,
                [.. group.Select(item => item.Profile)])),
    ];

    /// <summary>The checks that cannot be judged, grouped by check and by whether the limit is in the database.</summary>
    public static IReadOnlyList<ReportUnknownLimit> Unknowns(IEnumerable<ReportProfile> profiles) =>
    [
        .. Checks(profiles)
            .Where(item => item.Check.Status == ConnectionStatus.Grey)
            .GroupBy(item => (item.Check.Name, InDatabase: item.Check.Limit != null))
            .Select(group => new ReportUnknownLimit(group.Key.Name, group.Key.InDatabase, [.. group.Select(item => item.Profile)])),
    ];

    /// <summary>
    /// Whether a profile is not stable and nothing in the Results says why (a device or port error, a reconnection): then
    /// the findings do. A limitation explains any result; an unknown check explains only an unknown one.
    /// </summary>
    public static bool SeeFindings(IReadOnlyList<ReportProfile> profiles)
    {
        var limited = Limitations(profiles).SelectMany(row => row.Profiles).ToHashSet();
        var unknown = Unknowns(profiles).SelectMany(item => item.Profiles).ToHashSet();
        return profiles.Any(profile => profile.Overall != ConnectionStatus.Green && !limited.Contains(profile.Label)
            && !(profile.Overall == ConnectionStatus.Grey && unknown.Contains(profile.Label)));
    }

    private static IEnumerable<(string Profile, ReportCheck Check)> Checks(IEnumerable<ReportProfile> profiles) =>
        profiles.SelectMany(profile => profile.Checks.Select(check => (profile.Label, check)));
}
