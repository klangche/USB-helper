using UsbHelper.Core.Detection;
using UsbHelper.Detection.MacOS;

namespace UsbHelper.Detection.MacOS.Tests;

public class MacDisplayReaderTests
{
    private sealed class Fake(Dictionary<string, string> output) : IProfilerSource
    {
        public string Read(string dataType) => output[dataType];
    }

    private static MacDisplayReader Reader(string displays = "{}", string thunderbolt = "{}") =>
        new(new Fake(new() { ["SPDisplaysDataType"] = displays, ["SPThunderboltDataType"] = thunderbolt }));

    private const string InternalOnly = """
        {"SPDisplaysDataType":[{"_name":"Apple M1 Max","spdisplays_ndrvs":[
          {"_name":"Color LCD","_spdisplays_displayID":"1","spdisplays_connection_type":"spdisplays_internal","spdisplays_online":"spdisplays_yes"}]}]}
        """;

    [Fact]
    public void Internal_panel_is_internal_and_not_external()
    {
        var monitors = Reader(InternalOnly).ReadMonitors();

        var monitor = Assert.Single(monitors);
        Assert.Equal(new DisplayMonitor("1", "Color LCD", MonitorOutput.Internal), monitor);
        Assert.Equal(0, Reader(InternalOnly).CountExternalDisplays());
    }

    [Fact]
    public void External_display_is_other_and_counts_as_external()
    {
        var reader = Reader("""
            {"SPDisplaysDataType":[{"spdisplays_ndrvs":[
              {"_name":"Color LCD","_spdisplays_displayID":"1","spdisplays_connection_type":"spdisplays_internal"},
              {"_name":"DELL U2723QE","_spdisplays_displayID":"2","spdisplays_connection_type":"spdisplays_something_new","spdisplays_online":"spdisplays_yes"}]}]}
            """);

        Assert.Equal([MonitorOutput.Internal, MonitorOutput.Other], reader.ReadMonitors().Select(m => m.Output));
        Assert.Equal(1, reader.CountExternalDisplays());
    }

    [Fact]
    public void Display_with_no_connection_type_or_name_is_unknown_but_kept()
    {
        var reader = Reader("""
            {"SPDisplaysDataType":[{"spdisplays_ndrvs":[{"_spdisplays_display-vendor-id":"610","_spdisplays_display-product-id":"a051"}]}]}
            """);

        var monitor = Assert.Single(reader.ReadMonitors());
        Assert.Null(monitor.Name);
        Assert.Equal(MonitorOutput.Other, monitor.Output);
        Assert.Equal("610:a051:0", monitor.Id);
        Assert.Equal(1, reader.CountExternalDisplays());
    }

    [Fact]
    public void Offline_display_is_skipped()
    {
        var reader = Reader("""
            {"SPDisplaysDataType":[{"spdisplays_ndrvs":[{"_name":"Gone","_spdisplays_displayID":"3","spdisplays_online":"spdisplays_no"}]}]}
            """);

        Assert.Empty(reader.ReadMonitors());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"SPDisplaysDataType":[{"sppci_model":"GPU without displays"}]}""")]
    [InlineData("""{"SPDisplaysDataType":"unexpected"}""")]
    public void Missing_or_odd_keys_give_no_monitors(string json) => Assert.Empty(Reader(json).ReadMonitors());

    [Fact]
    public void Invalid_json_is_an_invalid_operation()
    {
        Assert.Throws<InvalidOperationException>(() => Reader("not json").ReadMonitors());
        Assert.Throws<InvalidOperationException>(() => Reader(thunderbolt: "").ReadThunderbolt());
    }

    [Fact]
    public void Thunderbolt_port_without_devices_is_not_connected()
    {
        var buses = Reader(thunderbolt: """
            {"SPThunderboltDataType":[{"_name":"thunderboltusb4_bus_1","switch_uid_key":"0x05AC","receptacle_1_tag":{
              "current_speed_key":"Up to 40 Gb/s","receptacle_id_key":"2","receptacle_status_key":"receptacle_no_devices_connected"}}]}
            """).ReadThunderbolt();

        var bus = Assert.Single(buses);
        Assert.Equal(("thunderboltusb4_bus_1", "2", "receptacle_no_devices_connected", "Up to 40 Gb/s", false), (bus.Name, bus.ReceptacleId, bus.Status, bus.SpeedLabel, bus.Connected));
        Assert.Empty(bus.Devices);
    }

    [Fact]
    public void Thunderbolt_attached_device_is_listed_and_connected()
    {
        var bus = Assert.Single(Reader(thunderbolt: """
            {"SPThunderboltDataType":[{"_name":"thunderboltusb4_bus_0","receptacle_1_tag":{"receptacle_id_key":"1","receptacle_status_key":"receptacle_connected"},
              "_items":[{"_name":"Studio Display","_items":[{"_name":"Dock"}]}]}]}
            """).ReadThunderbolt());

        Assert.True(bus.Connected);
        Assert.Equal(["Studio Display", "Dock"], bus.Devices);
    }

    [Fact]
    public void Thunderbolt_with_unknown_status_and_no_devices_is_unknown()
    {
        var bus = Assert.Single(Reader(thunderbolt: """{"SPThunderboltDataType":[{"_name":"bus"}]}""").ReadThunderbolt());

        Assert.Null(bus.Connected);
        Assert.Null(bus.ReceptacleId);
    }
}
