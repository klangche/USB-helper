using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows.Tests;

// #193: USB_NODE_CONNECTION_INFORMATION_EX_V2 (usbioctl.h): ConnectionIndex, Length, SupportedUsbProtocols
// (Usb110 = 0x1, Usb200 = 0x2, Usb300 = 0x4), Flags.
public class PortProtocolsTests
{
    private static byte[] Answer(uint protocols)
    {
        var buffer = PortProtocols.Request(3);
        BitConverter.TryWriteBytes(buffer.AsSpan(8), protocols);
        return buffer;
    }

    [Fact]
    public void RequestAsksForEveryProtocolOnThePort()
    {
        var request = PortProtocols.Request(3);

        Assert.Equal(16, request.Length);
        Assert.Equal(3u, BitConverter.ToUInt32(request, 0));
        Assert.Equal(16u, BitConverter.ToUInt32(request, 4));
        Assert.Equal(0x7u, BitConverter.ToUInt32(request, 8));
    }

    [Theory]
    [InlineData(0x4u, UsbBus.Usb3)] // USB 3 only: a SuperSpeed port
    [InlineData(0x3u, UsbBus.Usb2)] // USB 1.1 and 2.0
    [InlineData(0x2u, UsbBus.Usb2)]
    [InlineData(0x1u, UsbBus.Usb2)]
    public void TheBusComesFromTheProtocolsThePortSupports(uint protocols, UsbBus bus)
    {
        Assert.Equal(bus, PortProtocols.Bus(Answer(protocols), bytesReturned: 16));
    }

    [Theory]
    [InlineData(0x7u)] // USB 2 and USB 3 on one port: Windows does not say which bus the device is on
    [InlineData(0x6u)]
    [InlineData(0x0u)] // nothing
    public void AnAmbiguousOrEmptyAnswerIsUnknown(uint protocols)
    {
        Assert.Null(PortProtocols.Bus(Answer(protocols), bytesReturned: 16));
    }

    [Fact]
    public void ATooShortAnswerIsUnknown()
    {
        Assert.Null(PortProtocols.Bus(Answer(0x4), bytesReturned: 8));
    }
}
