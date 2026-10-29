using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace UsbHelper.Detection.Windows;

/// <summary>SetupAPI, cfgmgr32 and kernel32 functions and constants used for USB enumeration.</summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public static readonly Guid GuidDevInterfaceUsbHostController = new("3abf6f2d-71c4-462a-8a92-1e6861e6af27");
    public static readonly Guid GuidDevInterfaceUsbHub = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");

    public const uint DIGCF_PRESENT = 0x2;
    public const uint DIGCF_ALLCLASSES = 0x4;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;

    public const uint SPDRP_DEVICEDESC = 0x0;
    public const uint SPDRP_DRIVER = 0x9;
    public const uint SPDRP_FRIENDLYNAME = 0xC;

    public const uint DN_HAS_PROBLEM = 0x400;
    public const uint CR_SUCCESS = 0;

    // CM_NOTIFY_FILTER: cbSize, Flags, FilterType, Reserved, then a union whose largest member is
    // WCHAR InstanceId[MAX_DEVICE_ID_LEN = 200].
    public const int CM_NOTIFY_FILTER_SIZE = 16 + 400;
    public const uint CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES = 0x2;
    public const uint CM_NOTIFY_FILTER_TYPE_DEVICEINSTANCE = 2;
    public const uint CM_NOTIFY_ACTION_DEVICEINSTANCEENUMERATED = 7;
    public const uint CM_NOTIFY_ACTION_DEVICEINSTANCESTARTED = 8;
    public const uint CM_NOTIFY_ACTION_DEVICEINSTANCEREMOVED = 9;

    // CM_NOTIFY_EVENT_DATA: FilterType, Reserved, then the instance id.
    public const int CM_NOTIFY_EVENT_DATA_INSTANCE_ID_OFFSET = 8;

    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;

    // CTL_CODE(FILE_DEVICE_USB, function, METHOD_BUFFERED, FILE_ANY_ACCESS) from usbioctl.h.
    public const uint IOCTL_USB_GET_ROOT_HUB_NAME = 0x220408;
    public const uint IOCTL_USB_GET_NODE_INFORMATION = 0x220408;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_NAME = 0x220414;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME = 0x220420;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;

    public static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType,
        byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, byte[] deviceInstanceId, uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate uint CmNotifyCallback(IntPtr notify, IntPtr context, uint action, IntPtr eventData, uint eventDataSize);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Register_Notification(IntPtr filter, IntPtr context, CmNotifyCallback callback, out IntPtr notifyContext);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Unregister_Notification(IntPtr notifyContext);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    public static extern uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW")]
    public static extern uint CM_Get_Device_ID(uint devInst, byte[] buffer, uint bufferLength, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, byte[] inBuffer, int inBufferSize, byte[] outBuffer,
        int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
