namespace UsbHelper.Detection.Windows.Tests;

// #274: USB_DESCRIPTOR_REQUEST (usbioctl.h) for IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION: ConnectionIndex (4), the
// setup packet (bmRequest, bRequest, wValue, wIndex, wLength), then the configuration descriptor (USB 2.0 specification
// 9.6.3) followed by its interface (9.6.5), class-specific and endpoint descriptors. The descriptors below are written in
// the shape USBView shows for such devices.
public class ConfigurationDescriptorTests
{
    private const int Header = 12;

    // The reply Windows writes into the request buffer: the request header, then the descriptors.
    private static (byte[] Buffer, int Returned) Reply(params byte[][] descriptors)
    {
        var data = descriptors.SelectMany(descriptor => descriptor).ToArray();
        var total = data.Length;
        var buffer = ConfigurationDescriptor.Request(3, total);
        data.CopyTo(buffer, Header);
        return (buffer, buffer.Length);
    }

    // bLength, bDescriptorType 2, wTotalLength (filled in), bNumInterfaces, bConfigurationValue, iConfiguration, bmAttributes, bMaxPower.
    private static byte[] Configuration(byte interfaces, int total) =>
        [9, 2, (byte)(total & 0xFF), (byte)(total >> 8), interfaces, 1, 0, 0x80, 250];

    // bLength, bDescriptorType 4, bInterfaceNumber, bAlternateSetting, bNumEndpoints, bInterfaceClass, bInterfaceSubClass, bInterfaceProtocol, iInterface.
    private static byte[] Interface(byte number, byte alternate, byte endpoints, byte interfaceClass, byte subClass) =>
        [9, 4, number, alternate, endpoints, interfaceClass, subClass, 0, 0];

    // Interface association descriptor: bLength, 0x0B, bFirstInterface, bInterfaceCount, bFunctionClass, bFunctionSubClass, bFunctionProtocol, iFunction.
    private static byte[] Association(byte first, byte count, byte functionClass) => [8, 0x0B, first, count, functionClass, 3, 0, 0];

    private static byte[] Endpoint(byte address) => [7, 5, address, 3, 16, 0, 8];

    private static byte[] ClassSpecific(byte length) => [length, 0x24, .. new byte[length - 2]];

    private static byte[][] WithConfiguration(byte interfaces, params byte[][] rest)
    {
        var total = 9 + rest.Sum(descriptor => descriptor.Length);
        return [Configuration(interfaces, total), .. rest];
    }

    private static readonly byte[][] WebcamWithMicrophone = WithConfiguration(4,
        Association(0, 2, 0x0E), Interface(0, 0, 1, 0x0E, 1), ClassSpecific(13), Endpoint(0x83),
        Interface(1, 0, 0, 0x0E, 2), ClassSpecific(14), Interface(1, 1, 1, 0x0E, 2), Endpoint(0x81),
        Association(2, 2, 0x01), Interface(2, 0, 0, 0x01, 1), ClassSpecific(9),
        Interface(3, 0, 0, 0x01, 2), Interface(3, 1, 1, 0x01, 2), ClassSpecific(7), Endpoint(0x84));

    private static readonly byte[][] Headset = WithConfiguration(4,
        Interface(0, 0, 0, 0x01, 1), ClassSpecific(10),
        Interface(1, 0, 0, 0x01, 2), Interface(1, 1, 1, 0x01, 2), Endpoint(0x01),
        Interface(2, 0, 0, 0x01, 2), Interface(2, 1, 1, 0x01, 2), Endpoint(0x82),
        Interface(3, 0, 1, 0x03, 0), [9, 0x21, 0x11, 1, 0, 1, 0x22, 50, 0], Endpoint(0x83));

    private static readonly byte[][] Keyboard = WithConfiguration(1,
        Interface(0, 0, 1, 0x03, 1), [9, 0x21, 0x11, 1, 0, 1, 0x22, 63, 0], Endpoint(0x81));

    [Fact]
    public void RequestAsksForTheFirstConfigurationDescriptorOfThePort()
    {
        var request = ConfigurationDescriptor.Request(3, 9);

        Assert.Equal(Header + 9, request.Length);
        Assert.Equal(3u, BitConverter.ToUInt32(request, 0));
        Assert.Equal(0x0200, BitConverter.ToUInt16(request, 6)); // wValue: configuration descriptor, index 0
        Assert.Equal(0, BitConverter.ToUInt16(request, 8)); // wIndex
        Assert.Equal(9, BitConverter.ToUInt16(request, 10)); // wLength
    }

    [Fact]
    public void TheTotalLengthComesFromTheConfigurationDescriptorHeader()
    {
        var (buffer, _) = Reply(WebcamWithMicrophone);
        var head = buffer[..(Header + 9)];

        Assert.Equal(buffer.Length - Header, ConfigurationDescriptor.TotalLength(head, head.Length));
    }

    [Fact]
    public void AShortOrForeignHeaderHasNoTotalLength()
    {
        var (buffer, _) = Reply(Keyboard);

        Assert.Null(ConfigurationDescriptor.TotalLength(buffer, Header + 4));
        buffer[Header + 1] = 1; // a device descriptor, not a configuration descriptor
        Assert.Null(ConfigurationDescriptor.TotalLength(buffer, buffer.Length));
    }

    [Fact]
    public void AWebcamWithAMicrophoneListsVideoAndAudioInterfaces()
    {
        var (buffer, returned) = Reply(WebcamWithMicrophone);

        Assert.Equal([0x0E, 0x0E, 0x0E, 0x01, 0x01, 0x01], ConfigurationDescriptor.InterfaceClasses(buffer, returned));
    }

    [Fact]
    public void AHeadsetListsAudioAndHidInterfaces()
    {
        var (buffer, returned) = Reply(Headset);

        Assert.Equal([0x01, 0x01, 0x01, 0x01, 0x01, 0x03], ConfigurationDescriptor.InterfaceClasses(buffer, returned));
    }

    [Fact]
    public void AKeyboardListsOneHidInterface()
    {
        var (buffer, returned) = Reply(Keyboard);

        Assert.Equal([0x03], ConfigurationDescriptor.InterfaceClasses(buffer, returned));
    }

    [Fact]
    public void AnIncompleteReplyIsUnknown()
    {
        var (buffer, returned) = Reply(WebcamWithMicrophone);

        Assert.Null(ConfigurationDescriptor.InterfaceClasses(buffer, returned - 1));
        Assert.Null(ConfigurationDescriptor.InterfaceClasses(buffer, Header + 4));
    }

    [Fact]
    public void ABrokenDescriptorChainIsUnknown()
    {
        var (buffer, returned) = Reply(Keyboard);
        buffer[Header + 9] = 0; // the interface descriptor's bLength

        Assert.Null(ConfigurationDescriptor.InterfaceClasses(buffer, returned));
    }

    [Fact]
    public void ADescriptorThatRunsPastTheEndIsUnknown()
    {
        var (buffer, returned) = Reply(Keyboard);
        buffer[Header + 9 + 9 + 9] = 20; // the endpoint descriptor claims more bytes than there are

        Assert.Null(ConfigurationDescriptor.InterfaceClasses(buffer, returned));
    }
}
