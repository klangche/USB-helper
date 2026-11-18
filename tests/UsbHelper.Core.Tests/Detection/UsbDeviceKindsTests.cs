using UsbHelper.Core.Detection;

namespace UsbHelper.Core.Tests.Detection;

// #274: the kind of a device comes only from the interface classes the operating system reports (USB-IF class codes:
// 0x01 audio, 0x0E video).
public class UsbDeviceKindsTests
{
    [Fact]
    public void AWebcamWithAMicrophoneIsCameraAndAudio()
    {
        // Video control, video streaming, audio control, audio streaming.
        Assert.Equal(UsbDeviceKind.CameraAndAudio, UsbDeviceKinds.FromInterfaceClasses([0x0E, 0x0E, 0x01, 0x01]));
    }

    [Fact]
    public void AHeadsetIsAudio()
    {
        // Audio control, two audio streaming interfaces and HID for the buttons.
        Assert.Equal(UsbDeviceKind.Audio, UsbDeviceKinds.FromInterfaceClasses([0x01, 0x01, 0x01, 0x03]));
    }

    [Fact]
    public void AWebcamWithoutAMicrophoneIsCamera()
    {
        Assert.Equal(UsbDeviceKind.Camera, UsbDeviceKinds.FromInterfaceClasses([0x0E, 0x0E]));
    }

    [Fact]
    public void AKeyboardIsNeither()
    {
        Assert.Equal(UsbDeviceKind.Neither, UsbDeviceKinds.FromInterfaceClasses([0x03]));
    }

    [Fact]
    public void InterfacesThatCouldNotBeReadAreUnknown()
    {
        Assert.Equal(UsbDeviceKind.Unknown, UsbDeviceKinds.FromInterfaceClasses(null));
        Assert.Equal(UsbDeviceKind.Unknown, UsbDeviceKinds.FromInterfaceClasses([]));
    }
}
