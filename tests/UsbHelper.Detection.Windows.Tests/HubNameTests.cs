namespace UsbHelper.Detection.Windows.Tests;

public class HubNameTests
{
    [Theory]
    [InlineData(@"USB#ROOT_HUB30#4&1a2b&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}", @"USB\ROOT_HUB30\4&1a2b&0&0")]
    [InlineData(@"USB#VID_05E3&PID_0610#6&1&0&2#{f18a0e88-c30c-11d0-8815-00a0c906bed8}", @"USB\VID_05E3&PID_0610\6&1&0&2")]
    [InlineData(@"USB#VID_05E3&PID_0610#6&1&0&2", @"USB\VID_05E3&PID_0610\6&1&0&2")]
    public void SymbolicNameMapsToInstanceId(string symbolicName, string instanceId)
    {
        Assert.Equal(instanceId, HubName.ToInstanceId(symbolicName));
    }

    [Fact]
    public void SymbolicNameMapsToDevicePath()
    {
        Assert.Equal(@"\\.\USB#ROOT_HUB30#4&1a2b&0&0#{guid}", HubName.ToDevicePath("USB#ROOT_HUB30#4&1a2b&0&0#{guid}"));
    }
}
