namespace UsbHelper.Core.Diagnostics;

/// <summary>What a group of systems has in common (#369), in the order the panel shows the groups.</summary>
public enum OutcomeKind
{
    /// <summary>A maximum is exceeded or a connection has an error.</summary>
    Unstable,

    /// <summary>A recommendation is exceeded.</summary>
    PotentiallyUnstable,

    /// <summary>A count is only "at least", or a limit is unknown.</summary>
    Unknown,

    /// <summary>No finding.</summary>
    Good,
}

/// <summary>One system's result, named as the panel names it: "MacBook Pro (16-inch, 2021), measured" or "Windows Intel".</summary>
public sealed record SystemOutcome(string Name, IReadOnlyDictionary<string, ConnectionStatus> Connections, IReadOnlyList<Finding> Findings)
{
    public static SystemOutcome From(string name, DiagnosticReport report) => new(name, report.Connections, report.Findings);
}

/// <summary>Systems with the same outcome (#369): their names, and the findings they share, said once.</summary>
public sealed record OutcomeGroup(OutcomeKind Kind, IReadOnlyList<string> Systems, IReadOnlyList<Finding> Findings);

/// <summary>
/// The results grouped by outcome (#369, owner decision D3 on #366): every system is in exactly one group, by its whole
/// set of findings. Only the findings that judge the setup decide the group (limits exceeded and what is unknown); notes
/// and detected facts, the same for every system, do not.
/// </summary>
public static class OutcomeGroups
{
    public static IReadOnlyList<OutcomeGroup> Group(IReadOnlyList<SystemOutcome> systems) =>
        [.. systems
            .Select(system => (system.Name, Kind: KindOf(system), Findings: Judging(system.Findings)))
            .GroupBy(each => (each.Kind, Key: string.Join("\n", each.Findings.Select(Key))))
            .Select(group => new OutcomeGroup(group.Key.Kind, [.. group.Select(each => each.Name)], group.First().Findings))
            .OrderBy(group => group.Kind)];

    /// <summary>
    /// The groups as English lines, for the exported log and the report (#369): "Same issue on: A, B", then each shared
    /// finding indented once; "Unknown on: C", the same; "Good on: D, E".
    /// </summary>
    /// <param name="message">How a finding's message is written; as it is when not given.</param>
    public static IReadOnlyList<string> Lines(IReadOnlyList<OutcomeGroup> groups, Func<string, string>? message = null) =>
        [.. groups.SelectMany(group => group.Findings.Select(finding => $"  {(message ?? (text => text))(finding.Message)}")
            .Prepend($"{Title(group.Kind)} {string.Join(", ", group.Systems)}"))];

    /// <summary>"Same issue on:", "Unknown on:" or "Good on:".</summary>
    public static string Title(OutcomeKind kind) => kind switch
    {
        OutcomeKind.Good => "Good on:",
        OutcomeKind.Unknown => "Unknown on:",
        _ => "Same issue on:",
    };

    /// <summary>
    /// The group a result belongs to, from its worst connection. A disconnected device (red) is the same on every system,
    /// so it does not decide the group.
    /// </summary>
    public static OutcomeKind KindOf(SystemOutcome system) => system.Connections.Values
        .Where(status => status != ConnectionStatus.Red).DefaultIfEmpty(ConnectionStatus.Green).Max() switch
    {
        ConnectionStatus.Purple => OutcomeKind.Unstable,
        ConnectionStatus.Orange => OutcomeKind.PotentiallyUnstable,
        ConnectionStatus.Grey => OutcomeKind.Unknown,
        _ => Judging(system.Findings).Count > 0 ? OutcomeKind.Unknown : OutcomeKind.Good,
    };

    // The findings that judge the setup, in a fixed order so that two systems with the same findings get the same key.
    private static List<Finding> Judging(IReadOnlyList<Finding> findings) =>
        [.. findings.Where(finding => finding.Kind is FactKind.PredictedRisk or FactKind.Unknown)
            .DistinctBy(Key).OrderBy(Key, StringComparer.Ordinal)];

    private static string Key(Finding finding) => $"{finding.Kind}|{finding.SubjectId}|{finding.Message}";
}
