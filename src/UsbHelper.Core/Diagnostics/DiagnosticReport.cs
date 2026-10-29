namespace UsbHelper.Core.Diagnostics;

/// <summary>The result of evaluating the detected tree against a computer profile.</summary>
/// <param name="Overall">The highest-precedence status of all connections and limits. Green only when everything is known and fine.</param>
/// <param name="Connections">
/// Status of each connection line, by the id of the hub or device on the port. Missing devices are included (red).
/// </param>
/// <param name="Missing">
/// Devices seen this session that are no longer connected. Devices that were behind a hub that now cannot be read
/// are not listed here; they are grey with an unknown finding.
/// </param>
/// <param name="Findings">Every warning, error and unknown, each naming its subject and reason.</param>
public sealed record DiagnosticReport(
    ConnectionStatus Overall,
    IReadOnlyDictionary<string, ConnectionStatus> Connections,
    LimitCheck Hubs,
    LimitCheck Tiers,
    LimitCheck Hops,
    IReadOnlyList<MissingDevice> Missing,
    IReadOnlyList<Finding> Findings);
