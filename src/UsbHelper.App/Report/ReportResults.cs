using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Report;

/// <summary>One problem and the checked profiles it applies to: the same check failing with the same limit and result.</summary>
public sealed record ReportLimitation(string Check, long? Limit, int? Measured, ConnectionStatus Status, IReadOnlyList<string> Profiles);

/// <summary>A check whose limit is not in the database, and the checked profiles that lack it.</summary>
public sealed record ReportUnknownLimit(string Check, IReadOnlyList<string> Profiles);

/// <summary>
/// The report's Results section (#231): stable for everything except the problems, which are grouped over the checked
/// profiles. A limit that is not in the database is neither stable nor a problem.
/// </summary>
public static class ReportResults
{
    /// <summary>Every check that is not stable and has a known limit or none at all, grouped, worst first.</summary>
    public static IReadOnlyList<ReportLimitation> Limitations(IEnumerable<ReportProfile> profiles) =>
    [
        .. Checks(profiles)
            .Where(item => item.Check.Status != ConnectionStatus.Green && !IsUnknownLimit(item.Check))
            .GroupBy(item => item.Check)
            .OrderByDescending(group => group.Key.Status)
            .Select(group => new ReportLimitation(group.Key.Name, group.Key.Limit, group.Key.Measured, group.Key.Status,
                [.. group.Select(item => item.Profile)])),
    ];

    /// <summary>The checks whose limit is not in the database, grouped by check.</summary>
    public static IReadOnlyList<ReportUnknownLimit> Unknowns(IEnumerable<ReportProfile> profiles) =>
    [
        .. Checks(profiles)
            .Where(item => IsUnknownLimit(item.Check))
            .GroupBy(item => item.Check.Name)
            .Select(group => new ReportUnknownLimit(group.Key, [.. group.Select(item => item.Profile)])),
    ];

    private static IEnumerable<(string Profile, ReportCheck Check)> Checks(IEnumerable<ReportProfile> profiles) =>
        profiles.SelectMany(profile => profile.Checks.Select(check => (profile.Label, check)));

    private static bool IsUnknownLimit(ReportCheck check) => check.Status == ConnectionStatus.Grey && check.Limit == null;
}
