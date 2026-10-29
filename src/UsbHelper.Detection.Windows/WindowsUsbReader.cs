using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static UsbHelper.Detection.Windows.NativeMethods;

namespace UsbHelper.Detection.Windows;

/// <summary>
/// Reads host controllers, hubs and ports through SetupAPI and the USB hub IOCTLs, the way USBView does.
/// Needs no administrator rights. Reads no speed or bandwidth information.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsUsbReader
{
    // Byte offsets in the packed (pshpack1) structures of usbioctl.h.
    private const int NodeInformationSize = 76; // USB_NODE_INFORMATION
    private const int NumberOfPortsOffset = 6; // NodeType (4), bDescriptorLength, bDescriptorType
    private const int ConnectionInformationSize = 512; // USB_NODE_CONNECTION_INFORMATION_EX with room for the pipe list
    private const int VendorIdOffset = 12; // ConnectionIndex (4) + USB_DEVICE_DESCRIPTOR.idVendor (8)
    private const int ProductIdOffset = 14;
    private const int DeviceIsHubOffset = 24;
    private const int ConnectionStatusOffset = 31;
    private const int RootHubNameOffset = 4; // USB_ROOT_HUB_NAME.ActualLength
    private const int ConnectionNameOffset = 8; // ConnectionIndex, ActualLength
    private const int NameBufferSize = 2048;

    private sealed record DevNodeIndex(Dictionary<string, RawDevNode> ByDriverKey, Dictionary<string, RawDevNode> ByInstanceId);

    public static IReadOnlyList<RawController> Read()
    {
        var devNodes = ReadUsbDevNodes();
        var controllers = new List<RawController>();
        var guid = GuidDevInterfaceUsbHostController;
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == InvalidHandleValue)
        {
            throw new Win32Exception();
        }
        try
        {
            var deviceInterface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref deviceInterface); i++)
            {
                var info = NewDevInfoData();
                var path = InterfacePath(set, ref deviceInterface, ref info);
                var node = DevNode(set, ref info);
                try
                {
                    using var handle = Open(path);
                    var rootHubName = ReadName(handle, IOCTL_USB_GET_ROOT_HUB_NAME, null, RootHubNameOffset);
                    controllers.Add(new RawController(node, ReadHub(rootHubName, devNodes), null));
                }
                catch (Win32Exception e)
                {
                    controllers.Add(new RawController(node, null, e.Message));
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return controllers;
    }

    private static RawHub ReadHub(string symbolicName, DevNodeIndex devNodes)
    {
        devNodes.ByInstanceId.TryGetValue(HubName.ToInstanceId(symbolicName), out var node);
        try
        {
            using var handle = Open(HubName.ToDevicePath(symbolicName));
            var information = new byte[NodeInformationSize];
            Ioctl(handle, IOCTL_USB_GET_NODE_INFORMATION, information, information.Length);
            var ports = new List<RawPort>();
            for (var port = 1; port <= information[NumberOfPortsOffset]; port++)
            {
                ports.Add(ReadPort(handle, port, devNodes));
            }
            return new RawHub(node, ports, null);
        }
        catch (Win32Exception e)
        {
            return new RawHub(node, [], e.Message);
        }
    }

    private static RawPort ReadPort(SafeFileHandle hub, int port, DevNodeIndex devNodes)
    {
        var information = new byte[ConnectionInformationSize];
        BitConverter.TryWriteBytes(information, (uint)port);
        Ioctl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, information, information.Length);
        var status = BitConverter.ToInt32(information, ConnectionStatusOffset);
        var isHub = information[DeviceIsHubOffset] != 0;

        RawDevNode? device = null;
        RawHub? childHub = null;
        if (status != 0)
        {
            if (TryReadName(hub, IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME, port) is { } driverKey)
            {
                devNodes.ByDriverKey.TryGetValue(driverKey, out device);
            }
            if (isHub && TryReadName(hub, IOCTL_USB_GET_NODE_CONNECTION_NAME, port) is { } hubName)
            {
                childHub = ReadHub(hubName, devNodes);
            }
        }
        return new RawPort(
            port,
            status,
            isHub,
            BitConverter.ToUInt16(information, VendorIdOffset),
            BitConverter.ToUInt16(information, ProductIdOffset),
            device,
            childHub);
    }

    // All present devices enumerated by the USB bus driver: hubs and the devices on their ports.
    private static DevNodeIndex ReadUsbDevNodes()
    {
        var index = new DevNodeIndex(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));
        var set = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
        if (set == InvalidHandleValue)
        {
            throw new Win32Exception();
        }
        try
        {
            var info = NewDevInfoData();
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
            {
                var node = DevNode(set, ref info);
                index.ByInstanceId[node.InstanceId] = node;
                if (StringProperty(set, ref info, SPDRP_DRIVER) is { } driverKey)
                {
                    index.ByDriverKey[driverKey] = node;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return index;
    }

    private static RawDevNode DevNode(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var buffer = new byte[NameBufferSize];
        if (!SetupDiGetDeviceInstanceId(set, ref info, buffer, NameBufferSize / 2, out _))
        {
            throw new Win32Exception();
        }
        var instanceId = Decode(buffer, 0);
        var name = StringProperty(set, ref info, SPDRP_FRIENDLYNAME) ?? StringProperty(set, ref info, SPDRP_DEVICEDESC) ?? instanceId;
        int? problem = CM_Get_DevNode_Status(out var status, out var problemNumber, info.DevInst, 0) == CR_SUCCESS
            && (status & DN_HAS_PROBLEM) != 0 ? (int)problemNumber : null;
        return new RawDevNode(instanceId, name, problem);
    }

    private static string? StringProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var buffer = new byte[NameBufferSize];
        return SetupDiGetDeviceRegistryProperty(set, ref info, property, out _, buffer, NameBufferSize, out _)
            ? Decode(buffer, 0)
            : null;
    }

    private static string InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA deviceInterface, ref SP_DEVINFO_DATA info)
    {
        // The first call only reports the size needed.
        SetupDiGetDeviceInterfaceDetail(set, ref deviceInterface, IntPtr.Zero, 0, out var size, ref info);
        if (size == 0)
        {
            throw new Win32Exception();
        }
        var detail = Marshal.AllocHGlobal((int)size);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on 64-bit, 6 on 32-bit.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref deviceInterface, detail, size, out _, ref info))
            {
                throw new Win32Exception();
            }
            return Marshal.PtrToStringUni(detail + 4)!;
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static SafeFileHandle Open(string path)
    {
        var handle = CreateFile(path, GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = new Win32Exception();
            handle.Dispose();
            throw error;
        }
        return handle;
    }

    private static string? TryReadName(SafeFileHandle hub, uint ioControlCode, int port)
    {
        try
        {
            return ReadName(hub, ioControlCode, port, ConnectionNameOffset);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static string ReadName(SafeFileHandle device, uint ioControlCode, int? port, int nameOffset)
    {
        var buffer = new byte[NameBufferSize];
        var inputSize = 0;
        if (port is int index)
        {
            BitConverter.TryWriteBytes(buffer, (uint)index);
            inputSize = buffer.Length;
        }
        Ioctl(device, ioControlCode, buffer, inputSize);
        return Decode(buffer, nameOffset);
    }

    private static void Ioctl(SafeFileHandle device, uint ioControlCode, byte[] buffer, int inputSize)
    {
        if (!DeviceIoControl(device, ioControlCode, buffer, inputSize, buffer, buffer.Length, out _, IntPtr.Zero))
        {
            throw new Win32Exception();
        }
    }

    private static SP_DEVINFO_DATA NewDevInfoData() => new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };

    // UTF-16 text up to the first NUL.
    private static string Decode(byte[] buffer, int offset)
    {
        var text = Encoding.Unicode.GetString(buffer, offset, buffer.Length - offset);
        var end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }
}
