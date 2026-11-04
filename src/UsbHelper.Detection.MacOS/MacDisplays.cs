using System.Text.Json;
using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>
/// Parses <c>system_profiler -json SPDisplaysDataType</c> into monitors (#158). Keys are read by name and a missing key
/// means unknown. Only the built-in panel is named by the output type; every other connection stays
/// <see cref="MonitorOutput.Other"/>, because no external-display capture backs a finer mapping yet, and the connector
/// shape is never used to guess one.
/// </summary>
internal static class MacDisplays
{
    private const string Internal = "spdisplays_internal";

    public static IReadOnlyList<DisplayMonitor> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var monitors = new List<DisplayMonitor>();
        foreach (var gpu in Items(document.RootElement, "SPDisplaysDataType"))
        {
            foreach (var display in Items(gpu, "spdisplays_ndrvs"))
            {
                if (String(display, "spdisplays_online") == "spdisplays_no")
                {
                    continue; // Known but not active.
                }
                monitors.Add(new DisplayMonitor(Id(display, monitors.Count), String(display, "_name"), Output(display)));
            }
        }
        return monitors;
    }

    private static MonitorOutput Output(JsonElement display) =>
        String(display, "spdisplays_connection_type") == Internal ? MonitorOutput.Internal : MonitorOutput.Other;

    // The display id is stable while connected. Vendor and product alone would merge two identical monitors.
    private static string Id(JsonElement display, int index) =>
        String(display, "_spdisplays_displayID") is { } id
            ? id
            : $"{String(display, "_spdisplays_display-vendor-id") ?? "?"}:{String(display, "_spdisplays_display-product-id") ?? "?"}:{index}";

    internal static IEnumerable<JsonElement> Items(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    internal static string? String(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
