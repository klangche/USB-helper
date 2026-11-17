using System.Text;

namespace UsbHelper.Detection.Windows.Tests;

// #274, owner decision 2026-11-17 ("both"): the kind is read first from what Windows already knows, the compatible ids of
// the device node and of its interface child nodes (MI_nn), with no traffic to the device. Only when they give nothing
// is the configuration descriptor read. Ids as Windows builds them ("Identifiers Generated for USB Devices"): a
// composite device has USB\DevClass_xx and USB\COMPOSITE, each interface child USB\Class_xx, a single-function device
// USB\Class_xx.
public class CompatibleIdsTests
{
    private static RawDevNode Node(string instanceId, params string[] compatibleIds) =>
        new(instanceId, instanceId, null) { CompatibleIds = compatibleIds };

    private static readonly RawDevNode Webcam = Node(@"USB\VID_046D&PID_085C\5&2B0E5E8&0&1",
        @"USB\DevClass_EF&SubClass_02&Prot_01", @"USB\DevClass_EF&SubClass_02", @"USB\DevClass_EF", @"USB\COMPOSITE");

    private static readonly RawDevNode[] WebcamInterfaces =
    [
        Node(@"USB\VID_046D&PID_085C&MI_00\6&1&0&0000", @"USB\Class_0e&SubClass_03&Prot_00", @"USB\Class_0e&SubClass_03", @"USB\Class_0e"),
        Node(@"USB\VID_046D&PID_085C&MI_02\6&1&0&0002", @"USB\Class_01&SubClass_01&Prot_00", @"USB\Class_01&SubClass_01", @"USB\Class_01"),
    ];

    private static IReadOnlyList<int> NoDescriptor() => throw new InvalidOperationException("The descriptor must not be read.");

    [Fact]
    public void AWebcamWithAMicrophoneIsReadFromItsInterfaceNodesWithoutTheDescriptor()
    {
        Assert.Equal([0x0E, 0x01], CompatibleIds.InterfaceClasses(Webcam, WebcamInterfaces, NoDescriptor));
    }

    [Fact]
    public void ASingleFunctionDeviceIsReadFromItsOwnNode()
    {
        var headset = Node(@"USB\VID_046D&PID_0A87\5&2B0E5E8&0&2", @"USB\Class_01&SubClass_01&Prot_00", @"USB\Class_01&SubClass_01", @"USB\Class_01");
        var keyboard = Node(@"USB\VID_046D&PID_C31C\5&2B0E5E8&0&3", @"USB\Class_03&SubClass_01&Prot_01", @"USB\Class_03&SubClass_01", @"USB\Class_03");

        Assert.Equal([0x01], CompatibleIds.InterfaceClasses(headset, [], NoDescriptor));
        Assert.Equal([0x03], CompatibleIds.InterfaceClasses(keyboard, [], NoDescriptor));
    }

    [Fact]
    public void ACompositeDeviceWithoutInterfaceNodesFallsBackToTheDescriptor()
    {
        // No composite driver loaded: Windows made no MI_nn children, so it does not know the interfaces.
        Assert.Equal([0x0E, 0x01], CompatibleIds.InterfaceClasses(Webcam, [], () => [0x0E, 0x01]));
    }

    [Fact]
    public void ADeviceWithoutADeviceNodeFallsBackToTheDescriptor()
    {
        Assert.Equal([0x03], CompatibleIds.InterfaceClasses(null, [], () => [0x03]));
    }

    [Fact]
    public void ADeviceNeitherPathCanReadIsUnknown()
    {
        Assert.Null(CompatibleIds.InterfaceClasses(Node(@"USB\VID_1234&PID_5678\1"), [], () => null));
    }

    [Theory]
    [InlineData(@"USB\Class_0E", 0x0E)]
    [InlineData(@"usb\class_0e&subclass_01", 0x0E)]
    [InlineData(@"USB\Class_01&SubClass_02&Prot_00", 0x01)]
    public void TheClassComesFromAUsbClassId(string id, int expected)
    {
        Assert.Equal(expected, CompatibleIds.Class([id]));
    }

    [Theory]
    [InlineData(@"USB\DevClass_EF")] // the device's own class, not an interface's
    [InlineData(@"USB\COMPOSITE")]
    [InlineData(@"USB\Class_")]
    [InlineData(@"USB\Class_ZZ")]
    [InlineData(@"HID\Class_03")]
    public void OtherIdsGiveNoClass(string id)
    {
        Assert.Null(CompatibleIds.Class([id]));
    }

    [Fact]
    public void AMultiStringReplyIsSplitIntoItsIds()
    {
        var bytes = Encoding.Unicode.GetBytes("USB\\Class_0e&SubClass_03\0USB\\Class_0e\0\0");
        var buffer = new byte[2048];
        bytes.CopyTo(buffer, 0);

        Assert.Equal([@"USB\Class_0e&SubClass_03", @"USB\Class_0e"], CompatibleIds.Parse(buffer, (uint)bytes.Length));
        Assert.Empty(CompatibleIds.Parse(buffer, 0));
    }
}
