namespace UsbHelper.Core.Diagnostics;

/// <summary>The result of evaluating the detected tree against a computer profile.</summary>
/// <param name="Overall">The highest-precedence status of all connections and limits. Green only when everything is known and fine.</param>
/// <param name="Connections">
/// Status of each connection line, by the id of the hub or device on the port. Missing devices are included (red).
/// </param>
/// <param name="Displays">External displays and DisplayLink adapters, separate from the topology limits.</param>
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
    DisplayCheck Displays,
    IReadOnlyList<MissingDevice> Missing,
    IReadOnlyList<Finding> Findings)
{
    /// <summary>Tier of every counted hub and device, by id (see <see cref="Topology.TopologyCounts.Depths"/>).</summary>
    public IReadOnlyDictionary<string, Topology.ConnectionDepth> Depths { get; init; } = new Dictionary<string, Topology.ConnectionDepth>();

    /// <summary>
    /// More than one DisplayPort display is active and the profile does not support MST (#296): a warning, not a status;
    /// the displays stay green.
    /// </summary>
    public bool NoMst { get; init; }

    /// <summary>
    /// The bus whose hub check <see cref="Hubs"/> is, as hubs are judged per bus (#195); null when no bus is known and
    /// all hubs are judged together.
    /// </summary>
    public Detection.UsbBus? HubsBus { get; init; }

    /// <summary>
    /// The longest hub chain against <c>limits.max_chain</c> (#310), the bus over it first, else the one with the lowest
    /// limit; its limit is null when the guide does not count the chain.
    /// </summary>
    public LimitCheck Chain { get; init; } = new("limits.max_chain", 0, null, false, null, LimitState.Unknown);

    /// <summary>What the guide counts (<see cref="Database.Guide.Count"/>): only these are judged. Hubs and tiers without a guide.</summary>
    public IReadOnlyList<string> Counted { get; init; } = [Database.Guide.Tiers, Database.Guide.Hubs];
}
