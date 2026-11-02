namespace UsbHelper.Detection.Windows.Tests;

public class DeviceNameTests
{
    [Fact]
    public void PrefersTheNameTheDeviceReports()
    {
        Assert.Equal("Dell Universal Dock D6000",
            DeviceName.Choose("Dell Universal Dock D6000", "Generic USB Hub", "USB Composite Device", @"USB\VID_17E9&PID_6006\1"));
    }

    [Theory]
    [InlineData(null, "Logitech USB Receiver (friendly)", "USB Input Device", "Logitech USB Receiver (friendly)")]
    [InlineData("", "Logitech USB Receiver (friendly)", "USB Input Device", "Logitech USB Receiver (friendly)")]
    [InlineData("  ", null, "USB Input Device", "USB Input Device")]
    [InlineData(null, null, null, @"USB\VID_046D&PID_C52B\1")]
    public void FallsBackToTheWindowsName(string? busReported, string? friendlyName, string? description, string expected)
    {
        Assert.Equal(expected, DeviceName.Choose(busReported, friendlyName, description, @"USB\VID_046D&PID_C52B\1"));
    }
}
