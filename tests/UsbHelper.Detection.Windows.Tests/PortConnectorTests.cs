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

    [Theory]
    [InlineData(0x8u, true)] // PortConnectorIsTypeC
    [InlineData(0x9u, true)] // also PortIsUserConnectable
    [InlineData(0x0u, false)]
    [InlineData(0x7u, false)] // every other bit
    public void ReadsTypeCBit(uint portProperties, bool expected)
    {
        Assert.Equal(expected, PortConnector.IsTypeC(Properties(portProperties), bytesReturned: 18));
    }

    [Fact]
    public void TooShortAnswerSaysNothingAboutTypeC()
    {
        Assert.Null(PortConnector.IsTypeC(Properties(0x8), bytesReturned: 8));
    }

    // #193: CompanionPortNumber at offset 14, CompanionHubSymbolicLinkName (UTF-16, null-terminated) from offset 16.
    private static (byte[] Buffer, int Returned) WithCompanion(ushort port, string hubName)
    {
        var buffer = Properties(0x1);
        BitConverter.TryWriteBytes(buffer.AsSpan(12), (ushort)1);
        BitConverter.TryWriteBytes(buffer.AsSpan(14), port);
        var name = System.Text.Encoding.Unicode.GetBytes(hubName + "\0");
        var full = new byte[16 + name.Length];
        buffer.AsSpan(0, 16).CopyTo(full);
        name.CopyTo(full, 16);
        BitConverter.TryWriteBytes(full.AsSpan(4), (uint)full.Length);
        return (full, full.Length);
    }

    [Fact]
    public void ReadsTheCompanionPort()
    {
        var (buffer, returned) = WithCompanion(13, "USB#ROOT_HUB30#4&1a2b&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}");

        Assert.Equal((13, "USB#ROOT_HUB30#4&1a2b&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}"), PortConnector.Companion(buffer, returned));
    }

    [Fact]
    public void NoCompanionWhenThePortNumberIsZeroOrTheAnswerIsShort()
    {
        var (buffer, returned) = WithCompanion(0, "USB#ROOT_HUB30#4&1a2b&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}");

        Assert.Null(PortConnector.Companion(buffer, returned));
        Assert.Null(PortConnector.Companion(Properties(0x1), bytesReturned: 12));
    }

    [Fact]
    public void NoCompanionWithoutAHubName()
    {
        var (buffer, returned) = WithCompanion(13, "");

        Assert.Null(PortConnector.Companion(buffer, returned));
    }
}
