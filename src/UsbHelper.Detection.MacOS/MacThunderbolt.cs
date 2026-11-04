using System.Text.Json;

namespace UsbHelper.Detection.MacOS;

/// <summary>One Thunderbolt/USB4 bus of the Mac with its receptacle, as System Information shows it (#158).</summary>
/// <param name="Name">The bus, for example <c>thunderboltusb4_bus_1</c>.</param>
/// <param name="ReceptacleId">The receptacle number the system gives, or null when unknown.</param>
/// <param name="Status">The raw receptacle status, for example <c>receptacle_no_devices_connected</c>.</param>
/// <param name="SpeedLabel">The port's capability text, for example "Up to 40 Gb/s". Not a measured speed.</param>
/// <param name="Connected">False when the system reports no device, true when devices are listed, else null.</param>
/// <param name="Devices">The names of the attached Thunderbolt/USB4 devices. They are not USB hubs.</param>
public sealed record ThunderboltBus(string Name, string? ReceptacleId, string? Status, string? SpeedLabel, bool? Connected, IReadOnlyList<string> Devices);

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
                devices));
        }
        return buses;
    }

    private static IEnumerable<string> Devices(JsonElement parent) =>
        MacDisplays.Items(parent, "_items").SelectMany(item =>
            ((MacDisplays.String(item, "_name") ?? MacDisplays.String(item, "device_name_key")) is { } name ? [name] : Enumerable.Empty<string>())
                .Concat(Devices(item)));
}
