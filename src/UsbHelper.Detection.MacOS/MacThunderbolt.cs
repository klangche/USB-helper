using System.Text.Json;

namespace UsbHelper.Detection.MacOS;

/// <summary>One Thunderbolt/USB4 bus of the Mac with its receptacle, as System Information shows it (#158).</summary>
/// <param name="Name">The bus, for example <c>thunderboltusb4_bus_1</c>.</param>
/// <param name="ReceptacleId">The receptacle number the system gives, or null when unknown.</param>
/// <param name="Status">The raw receptacle status, for example <c>receptacle_no_devices_connected</c>.</param>
/// <param name="SpeedLabel">The port's capability text, for example "Up to 40 Gb/s". Not a measured speed.</param>
/// <param name="Connected">False when the system reports no device, true when devices are listed, else null.</param>
/// <param name="Devices">The names of the attached Thunderbolt/USB4 devices. They are not USB hubs.</param>
/// <param name="RouterDepth">
/// The deepest USB4/Thunderbolt device router on the bus (#332): 1 for a dock on the Mac's port, 2 for one behind it, 0
/// for none. Each listed device is a router, one level of <c>_items</c> deeper than the one it hangs on.
/// </param>
public sealed record ThunderboltBus(string Name, string? ReceptacleId, string? Status, string? SpeedLabel, bool? Connected, IReadOnlyList<string> Devices,
    int RouterDepth = 0)
{
    /// <summary>N in <c>thunderboltusb4_bus_N</c>, or null when the name has no number.</summary>
    public int? Index => Name.LastIndexOf('_') is var at and >= 0 && int.TryParse(Name.AsSpan(at + 1), out var index) ? index : null;
}

/// <summary>Parses <c>system_profiler -json SPThunderboltDataType</c>. Missing keys mean unknown; serials and UIDs are never read.</summary>
internal static class MacThunderbolt
{
    private const string NoDevices = "receptacle_no_devices_connected";

    public static IReadOnlyList<ThunderboltBus> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var buses = new List<ThunderboltBus>();
        foreach (var bus in MacDisplays.Items(document.RootElement, "SPThunderboltDataType"))
        {
            var receptacle = bus.EnumerateObject()
                .Where(property => property.Name.StartsWith("receptacle_", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Object)
                .Select(property => property.Value)
                .FirstOrDefault();
            var status = MacDisplays.String(receptacle, "receptacle_status_key");
            var devices = Devices(bus).ToList();
            buses.Add(new ThunderboltBus(
                MacDisplays.String(bus, "_name") ?? "unnamed",
                MacDisplays.String(receptacle, "receptacle_id_key"),
                status,
                MacDisplays.String(receptacle, "current_speed_key"),
                devices.Count > 0 ? true : status == NoDevices ? false : null,
                devices,
                Depth(bus)));
        }
        return buses;
    }

    // The deepest level of _items below the bus: each level is one router further from the Mac.
    private static int Depth(JsonElement parent) =>
        MacDisplays.Items(parent, "_items").Select(item => 1 + Depth(item)).DefaultIfEmpty(0).Max();

    private static IEnumerable<string> Devices(JsonElement parent) =>
        MacDisplays.Items(parent, "_items").SelectMany(item =>
            ((MacDisplays.String(item, "_name") ?? MacDisplays.String(item, "device_name_key")) is { } name ? [name] : Enumerable.Empty<string>())
                .Concat(Devices(item)));
}
