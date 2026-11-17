namespace UsbHelper.Core.Detection;

/// <summary>
/// What a device is, as the operating system reports it through the classes of its USB interfaces (#274). Never guessed
/// from a name, a vendor id or a speed.
/// </summary>
public enum UsbDeviceKind
{
    /// <summary>The operating system does not say: the device's interfaces could not be read.</summary>
    Unknown,

    /// <summary>The interfaces were read and none is video or audio.</summary>
    Neither,

    /// <summary>A USB video class interface (0x0E) and no audio class interface.</summary>
    Camera,

    /// <summary>A USB audio class interface (0x01) and no video class interface.</summary>
    Audio,

    /// <summary>Both, such as a webcam with a microphone.</summary>
    CameraAndAudio,
}

public static class UsbDeviceKinds
{
    /// <summary>bInterfaceClass of the USB audio class (USB-IF defined class codes).</summary>
    public const int AudioClass = 0x01;

    /// <summary>bInterfaceClass of the USB video class (USB-IF defined class codes).</summary>
    public const int VideoClass = 0x0E;

    /// <summary>
    /// The kind from the bInterfaceClass of every interface of the device. A composite device has several; its own
    /// device class (0x00 or 0xEF) says nothing, so only the interfaces count. Null or none read: unknown.
    /// </summary>
    public static UsbDeviceKind FromInterfaceClasses(IReadOnlyCollection<int>? classes)
    {
        if (classes is not { Count: > 0 })
        {
            return UsbDeviceKind.Unknown;
        }
        var video = classes.Contains(VideoClass);
        var audio = classes.Contains(AudioClass);
        return video && audio ? UsbDeviceKind.CameraAndAudio
            : video ? UsbDeviceKind.Camera
            : audio ? UsbDeviceKind.Audio
            : UsbDeviceKind.Neither;
    }
}
