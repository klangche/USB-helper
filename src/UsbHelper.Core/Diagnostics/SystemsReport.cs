using UsbHelper.Core.Database;

namespace UsbHelper.Core.Diagnostics;

/// <summary>One checked profile and its result.</summary>
public sealed record ProfileResult(ResolvedProfile Profile, DiagnosticReport Report);

/// <summary>The detected tree evaluated against every checked profile.</summary>
/// <param name="Overall">The worst overall result of all checked profiles; grey when none is checked.</param>
/// <param name="Connections">Each line's worst status over all checked profiles.</param>
/// <param name="Profiles">Every checked profile with its own result, in the order given.</param>
/// <param name="Counts">
/// A report for the counts, disconnected devices and detected facts, which are the same for every profile: the first
/// profile's report, or the report without a profile when none is checked.
/// </param>
public sealed record SystemsReport(
    ConnectionStatus Overall,
    IReadOnlyDictionary<string, ConnectionStatus> Connections,
    IReadOnlyList<ProfileResult> Profiles,
    DiagnosticReport Counts);
