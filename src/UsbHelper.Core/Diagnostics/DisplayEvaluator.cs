using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Diagnostics;

/// <summary>
/// Display rules, separate from the topology limits. Never infers a display count from ports, never assumes a
/// USB-C port carries video, and never calculates bandwidth. Which physical port a monitor uses is not detected.
/// </summary>
internal static class DisplayEvaluator
{
    // DisplayLink's USB vendor id. Its adapters are ordinary USB devices, so they appear in the tree.
    private const ushort DisplayLinkVendorId = 0x17E9;

    private static readonly (string Field, string Label)[] Capabilities =
    [
        ("displayport_alt_mode", "DisplayPort Alt Mode"),
        ("displayport_mst", "DisplayPort MST"),
        ("usb4", "USB4"),
        ("native_displayport", "DisplayPort output"),
        ("hdmi", "HDMI output"),
        ("displaylink", "DisplayLink"),
    ];

    /// <summary>Adds the display findings and returns the check and its share of the overall status.</summary>
    public static (DisplayCheck Check, ConnectionStatus Contribution) Evaluate(
        IReadOnlyList<UsbNode> tree, ResolvedProfile? profile, int? externalDisplays, string computer, List<Finding> findings)
    {
        var contribution = ConnectionStatus.Green;
        var maxValue = profile?.Find("displays.max_external");
        var max = maxValue?.Scalar as long?;
        var verified = max != null && DiagnosticEvaluator.IsVerified(profile, maxValue!);
        var state = externalDisplays == null ? LimitState.Unknown
            : externalDisplays == 0 ? LimitState.Within
            : max == null ? LimitState.Unknown
            : externalDisplays > max ? LimitState.Exceeded
            : LimitState.Within;

        if (externalDisplays == null)
        {
            findings.Add(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, computer,
                "The number of external displays is not known."));
            contribution = ConnectionStatus.Grey;
        }
        else
        {
            findings.Add(new Finding(FactKind.DetectedFact, DiagnosticEvaluator.ComputerId, computer,
                $"{externalDisplays} external displays are active (DisplayLink and wireless displays not counted)."));
        }

        if (externalDisplays > 0)
        {
            var source = maxValue == null ? "" : $" ({maxValue.File})";
            if (max == null)
            {
                findings.Add(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, computer, $"displays.max_external is unknown{source}."));
            }
            else
            {
                findings.Add(new Finding(verified ? FactKind.VerifiedDatabaseFact : FactKind.Unknown, DiagnosticEvaluator.ComputerId, computer,
                    verified ? $"displays.max_external = {max}{source}." : $"displays.max_external = {max} comes from an unverified profile{source}; it is not confirmed."));
            }
            if (state == LimitState.Exceeded)
            {
                findings.Add(new Finding(FactKind.PredictedRisk, DiagnosticEvaluator.ComputerId, computer,
                    $"{externalDisplays} external displays are active; displays.max_external is {max}{(verified ? "" : " (unverified limit)")}."));
                contribution = ConnectionStatus.Yellow;
            }
            else if (state == LimitState.Unknown || !verified)
            {
                contribution = Max(contribution, ConnectionStatus.Grey);
            }
        }

        var ports = profile?.Find("ports")?.Fields ?? new Dictionary<string, ProfileValue>();
        foreach (var (name, port) in ports)
        {
            var kind = DiagnosticEvaluator.IsVerified(profile, port) ? FactKind.VerifiedDatabaseFact : FactKind.Unknown;
            var subject = port.Fields.TryGetValue("location", out var location) && location.Scalar is string place
                ? $"Port {name} ({place})"
                : $"Port {name}";
            findings.Add(new Finding(kind, DiagnosticEvaluator.ComputerId, subject, Describe(port)));
            foreach (var limitation in port.Find("limitations")?.Items ?? [])
            {
                findings.Add(new Finding(kind, DiagnosticEvaluator.ComputerId, subject, (string)limitation.Scalar!));
            }
        }
        foreach (var restriction in profile?.Find("displays.restrictions")?.Items ?? [])
        {
            findings.Add(new Finding(DiagnosticEvaluator.IsVerified(profile, restriction) ? FactKind.VerifiedDatabaseFact : FactKind.Unknown,
                DiagnosticEvaluator.ComputerId, computer, (string)restriction.Scalar!));
        }

        var adapters = AllNodes(tree).Where(node => node.Kind == UsbNodeKind.Device && node.VendorId == DisplayLinkVendorId).ToList();
        foreach (var adapter in adapters)
        {
            findings.Add(new Finding(FactKind.DetectedFact, adapter.Id, adapter.Name,
                "DisplayLink adapter: its displays use the DisplayLink driver, not the computer's own video outputs."));
        }
        var support = ports.Values.Select(port => port.Find("displaylink")).Where(value => value?.Kind == ValueKind.Boolean).ToList();
        if (adapters.Count > 0 && !support.Any(value => (bool)value!.Scalar!))
        {
            if (support.Count > 0 && support.Count == ports.Count)
            {
                findings.Add(new Finding(FactKind.PredictedRisk, DiagnosticEvaluator.ComputerId, computer,
                    "A DisplayLink adapter is connected, but the computer profile marks no port as supporting DisplayLink."));
                contribution = ConnectionStatus.Yellow;
            }
            else
            {
                findings.Add(new Finding(FactKind.Unknown, DiagnosticEvaluator.ComputerId, computer,
                    "A DisplayLink adapter is connected; whether this computer supports DisplayLink is unknown."));
                contribution = Max(contribution, ConnectionStatus.Grey);
            }
        }

        var check = new DisplayCheck(externalDisplays, max, verified, maxValue?.File, state, adapters.Select(adapter => adapter.Id).ToList());
        return (check, contribution);
    }

    // Only the fields the profile sets are described; an omitted field is not claimed either way.
    private static string Describe(ProfileValue port)
    {
        var parts = new List<string>();
        if (port.Find("usb") is { Kind: ValueKind.String } usb)
        {
            parts.Add($"USB: {usb.Scalar}");
        }
        if (port.Find("thunderbolt") is { } thunderbolt)
        {
            parts.Add(thunderbolt.Kind == ValueKind.Integer ? $"Thunderbolt {thunderbolt.Scalar}" : "Thunderbolt: unknown");
        }
        foreach (var (field, label) in Capabilities)
        {
            if (port.Find(field) is { } value)
            {
                parts.Add($"{label}: {(value.Kind == ValueKind.Boolean ? ((bool)value.Scalar! ? "yes" : "no") : "unknown")}");
            }
        }
        return parts.Count == 0 ? "No capabilities recorded." : string.Join("; ", parts) + ".";
    }

    private static ConnectionStatus Max(ConnectionStatus a, ConnectionStatus b) => a > b ? a : b;

    private static IEnumerable<UsbNode> AllNodes(IReadOnlyList<UsbNode> nodes) =>
        nodes.SelectMany(node => AllNodes(node.Children).Prepend(node));
}
