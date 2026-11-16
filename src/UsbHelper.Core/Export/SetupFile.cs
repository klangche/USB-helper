using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.Core.Export;

/// <summary>A finding as exported: its kind, what it is about and why.</summary>
public sealed record SetupFinding(FactKind Kind, string Subject, string Message);

/// <summary>One checked profile's result; <see cref="Profile"/> is null when no profile was checked.</summary>
public sealed record SetupResult(string? Profile, string Label, ConnectionStatus Overall, IReadOnlyList<SetupFinding> Findings);

/// <summary>
/// <c>setup.json</c> of a full setup export (#135): everything the app detected and found, readable, and loadable again
/// as test input. Ids are positions in the tree (see <see cref="Anonymize"/>), so no serial number is exported.
/// </summary>
/// <param name="CheckedProfiles">The ids of the checked platform and computer profiles.</param>
/// <param name="Marks">The user's active-cable marks: extra hops per port id.</param>
/// <param name="Displays">The active monitors; null when they could not be read.</param>
public sealed record SetupFile(
    string AppVersion,
    DateOnly Date,
    ComputerIdentity Computer,
    IReadOnlyList<string> CheckedProfiles,
    IReadOnlyList<UsbNode> Controllers,
    IReadOnlyDictionary<string, int> Marks,
    IReadOnlyList<DisplayMonitor>? Displays,
    IReadOnlyList<SetupResult> Results)
{
    /// <summary>Raised when the file's layout changes in a way older readers cannot follow.</summary>
    public const int CurrentFormat = 1;

    [JsonPropertyOrder(-1)]
    public int FormatVersion { get; init; } = CurrentFormat;

    // #230: optional, left out when null; older readers ignore them, so the format stays 1.

    /// <summary>The company name from the branding; null while branding is locked or no name is set.</summary>
    [JsonPropertyOrder(-1)]
    public string? CompanyName { get; init; }

    /// <summary>The room or system name the user typed; null when none.</summary>
    [JsonPropertyOrder(-1)]
    public string? RoomName { get; init; }

    /// <summary>The email the extra features are unlocked for (personal data, at the owner's request); null while locked.</summary>
    [JsonPropertyOrder(-1)]
    public string? LicensedTo { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Plain characters such as "&" and "'": the file is read by people, never embedded in a web page.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower), new HexIdConverter() },
    };

    // Vendor and product ids as four hex digits ("046D"), as Windows and usb.ids show them.
    private sealed class HexIdConverter : JsonConverter<ushort>
    {
        public override ushort Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ushort.Parse(reader.GetString() ?? "", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, ushort value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("X4", CultureInfo.InvariantCulture));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <exception cref="JsonException">The text is not a setup file.</exception>
    /// <exception cref="NotSupportedException">The file has a newer format than this app reads.</exception>
    public static SetupFile FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var format = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("format_version", out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
        if (format != CurrentFormat)
        {
            throw new NotSupportedException($"setup.json format_version {format} is not supported; this app reads {CurrentFormat}.");
        }
        return JsonSerializer.Deserialize<SetupFile>(json, Options) ?? throw new JsonException("setup.json is empty.");
    }

    /// <summary>
    /// The tree with every id replaced by its position ("1/1/2": controller 1, its first child, that child's second
    /// child), and the marks and companion port ids moved to the new port ids. Windows ids are device paths, which can hold serial numbers. A
    /// placeholder for a hub or device Windows could not identify keeps its form: its port's id, then "|device".
    /// </summary>
    public static (IReadOnlyList<UsbNode> Tree, IReadOnlyDictionary<string, int> Marks) Anonymize(
        IReadOnlyList<UsbNode> tree, IReadOnlyDictionary<string, int> extraHops)
    {
        var ids = new Dictionary<string, string>();
        var renamed = tree.Select((controller, i) => Renamed(controller, null, null, (i + 1).ToString(CultureInfo.InvariantCulture), ids)).ToList();
        // A companion port id (#193) is a hub id too: it gets the companion's new id, or is dropped when the companion
        // is not in the tree, so no original id is exported.
        UsbNode Companions(UsbNode node) => node with
        {
            CompanionPortId = node.CompanionPortId is { } companion ? ids.GetValueOrDefault(companion) : null,
            Children = [.. node.Children.Select(Companions)],
        };
        var anonymous = renamed.Select(Companions).ToList();
        var marks = extraHops.Where(mark => ids.ContainsKey(mark.Key)).ToDictionary(mark => ids[mark.Key], mark => mark.Value);
        return (anonymous, marks);
    }

    /// <summary>
    /// The tree and monitors with every container id (#143) replaced by a numbered one (…-000000000001), equal where the
    /// originals were equal, so a monitor still links to the same USB node. The computer's own well-known container id
    /// is kept.
    /// </summary>
    public static (IReadOnlyList<UsbNode> Tree, IReadOnlyList<DisplayMonitor>? Monitors) AnonymizeContainers(
        IReadOnlyList<UsbNode> tree, IReadOnlyList<DisplayMonitor>? monitors)
    {
        var numbers = new Dictionary<Guid, Guid>();
        Guid? Replaced(Guid? id) => id is not { } value || value == MonitorPlacement.ThisComputer ? id
            : numbers.TryGetValue(value, out var known) ? known
            : numbers[value] = new Guid($"00000000-0000-0000-0000-{numbers.Count + 1:D12}");
        UsbNode Renamed(UsbNode node) => node with { ContainerId = Replaced(node.ContainerId), Children = [.. node.Children.Select(Renamed)] };
        var renamed = tree.Select(Renamed).ToList();
        return (renamed, monitors?.Select(monitor => monitor with { ContainerId = Replaced(monitor.ContainerId) }).ToList());
    }

    // portId and newPortId: the port this node is on, if any.
    private static UsbNode Renamed(UsbNode node, string? portId, string? newPortId, string position, Dictionary<string, string> ids)
    {
        var id = portId != null && node.Id.StartsWith(portId + "|", StringComparison.Ordinal) ? newPortId + "|device" : position;
        ids[node.Id] = id;
        var isPort = node.Kind == UsbNodeKind.Port;
        return node with
        {
            Id = id,
            Children = [.. node.Children.Select((child, i) => Renamed(child, isPort ? node.Id : null, isPort ? id : null, $"{id}/{i + 1}", ids))],
        };
    }
}
