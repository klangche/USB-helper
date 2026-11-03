using System.Text;

namespace UsbHelper.Detection.Windows.Tests;

// #143: DEVPKEY_Device_ContainerId of a USB device and of a monitor.
public class ContainerIdTests
{
    private static readonly Guid Id = new("6f1d2a6e-0000-4000-8000-000000000001");

    [Fact]
    public void AGuidPropertyIsTheContainerId()
    {
        Assert.Equal(Id, ContainerIds.FromProperty(ContainerIds.DevpropTypeGuid, Id.ToByteArray(), 16));
    }

    [Fact]
    public void AnotherTypeOrAnEmptyGuidIsNoContainerId()
    {
        Assert.Null(ContainerIds.FromProperty(0x12, Id.ToByteArray(), 16)); // A string, not a GUID.
        Assert.Null(ContainerIds.FromProperty(ContainerIds.DevpropTypeGuid, Id.ToByteArray(), 8));
        Assert.Null(ContainerIds.FromProperty(ContainerIds.DevpropTypeGuid, Guid.Empty.ToByteArray(), 16));
    }

    [Fact]
    public void TheMonitorsDevicePathIsReadFromTheTargetNameReply()
    {
        const string path = @"\\?\DISPLAY#SAM0F0E#5&1b2c3d&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var reply = DisplayTargetName.Request(1, 7);
        Encoding.Unicode.GetBytes(path).CopyTo(reply, 164);

        Assert.Equal(path, DisplayTargetName.DevicePath(reply));
    }

    [Fact]
    public void TheMonitorsInstanceIdComesFromItsInterfacePath()
    {
        Assert.Equal(@"DISPLAY\SAM0F0E\5&1b2c3d&0&UID4352",
            ContainerIds.InstanceId(@"\\?\DISPLAY#SAM0F0E#5&1b2c3d&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"));
        Assert.Null(ContainerIds.InstanceId(""));
    }

    [Fact]
    public void HubsAndDevicesKeepTheirContainerId()
    {
        var hubNode = new RawDevNode(@"USB\VID_0BDA&PID_5411\6&1&0&2", "Generic USB Hub", null) { ContainerId = Id };
        var camera = new RawDevNode(@"USB\VID_04E8&PID_2058\7&1&0&1", "Camera", null) { ContainerId = Id };
        var hub = new RawHub(hubNode, [new RawPort(1, 1, false, 0x04E8, 0x2058, camera, null)], null);
        var root = new RawHub(new RawDevNode("ROOT", "Root hub", null), [new RawPort(1, 1, true, 0x0BDA, 0x5411, hubNode, hub)], null);

        var tree = UsbTreeBuilder.Build([new RawController(new RawDevNode("C1", "Controller", null), root, null)]);

        var monitorHub = tree[0].Children[0].Children[0].Children[0];
        Assert.Equal(Id, monitorHub.ContainerId);
        Assert.Equal(Id, monitorHub.Children[0].Children[0].ContainerId);
    }
}
