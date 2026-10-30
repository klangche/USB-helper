namespace UsbHelper.Detection.Windows.Tests;

public class PortConnectorTests
{
    // USB_PORT_CONNECTOR_PROPERTIES: ConnectionIndex, ActualLength, UsbPortProperties, CompanionIndex,
    // CompanionPortNumber, CompanionHubSymbolicLinkName.
    private static byte[] Properties(uint portProperties)
    {
        var buffer = new byte[64];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), 2u);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), 18u);
        BitConverter.TryWriteBytes(buffer.AsSpan(8), portProperties);
        return buffer;
    }

    [Theory]
    [InlineData(0x1u, true)] // PortIsUserConnectable
    [InlineData(0x9u, true)] // also PortConnectorIsTypeC
    [InlineData(0x0u, false)]
    [InlineData(0x2u, false)] // only PortIsDebugCapable
    public void ReadsUserConnectableBit(uint portProperties, bool expected)
    {
        Assert.Equal(expected, PortConnector.IsUserConnectable(Properties(portProperties), bytesReturned: 18));
    }

    [Fact]
    public void TooShortAnswerSaysNothing()
    {
        Assert.Null(PortConnector.IsUserConnectable(Properties(0x1), bytesReturned: 8));
    }
}
