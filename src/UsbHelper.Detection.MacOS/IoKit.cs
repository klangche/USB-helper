using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace UsbHelper.Detection.MacOS;

/// <summary>The few IOKit and CoreFoundation calls needed to read the registry. All are read-only.</summary>
[SupportedOSPlatform("macos")]
internal static partial class IoKit
{
    private const string IoKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    private const int NumberSInt64Type = 4;

    [LibraryImport(IoKitLib)]
    public static partial uint IORegistryGetRootEntry(uint mainPort);

    [LibraryImport(IoKitLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int IORegistryEntryGetChildIterator(uint entry, string plane, out uint iterator);

    [LibraryImport(IoKitLib)]
    public static partial uint IOIteratorNext(uint iterator);

    [LibraryImport(IoKitLib)]
    public static partial int IOObjectRelease(uint obj);

    [LibraryImport(IoKitLib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IOObjectConformsTo(uint obj, string className);

    [LibraryImport(IoKitLib)]
    private static partial int IOObjectGetClass(uint obj, byte[] className);

    [LibraryImport(IoKitLib)]
    private static partial int IORegistryEntryGetName(uint entry, byte[] name);

    [LibraryImport(IoKitLib)]
    public static partial int IORegistryEntryGetRegistryEntryID(uint entry, out ulong entryId);

    [LibraryImport(IoKitLib)]
    private static partial IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [LibraryImport(CoreFoundationLib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [LibraryImport(CoreFoundationLib)]
    private static partial nuint CFGetTypeID(IntPtr value);

    [LibraryImport(CoreFoundationLib)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(CoreFoundationLib)]
    private static partial nuint CFNumberGetTypeID();

    [LibraryImport(CoreFoundationLib)]
    private static partial nuint CFBooleanGetTypeID();

    [LibraryImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(IntPtr number, int type, out long value);

    [LibraryImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFBooleanGetValue(IntPtr value);

    [LibraryImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr text, byte[] buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundationLib)]
    private static partial void CFRelease(IntPtr value);

    public static string? ClassName(uint entry) => CName(buffer => IOObjectGetClass(entry, buffer));

    public static string? EntryName(uint entry) => CName(buffer => IORegistryEntryGetName(entry, buffer));

    private static string? CName(Func<byte[], int> read)
    {
        var buffer = new byte[128];
        if (read(buffer) != 0)
        {
            return null;
        }

        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    /// <summary>A number, string or boolean property; null when the entry has none or it has another type.</summary>
    public static object? Property(uint entry, string key)
    {
        var cfKey = CFStringCreateWithCString(IntPtr.Zero, key, Utf8);
        if (cfKey == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var value = IORegistryEntryCreateCFProperty(entry, cfKey, IntPtr.Zero, 0);
            if (value == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var type = CFGetTypeID(value);
                if (type == CFNumberGetTypeID())
                {
                    return CFNumberGetValue(value, NumberSInt64Type, out var number) ? number : null;
                }

                if (type == CFBooleanGetTypeID())
                {
                    return CFBooleanGetValue(value);
                }

                if (type == CFStringGetTypeID())
                {
                    var buffer = new byte[1024];
                    if (!CFStringGetCString(value, buffer, buffer.Length, Utf8))
                    {
                        return null;
                    }

                    return Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0));
                }

                return null;
            }
            finally
            {
                CFRelease(value);
            }
        }
        finally
        {
            CFRelease(cfKey);
        }
    }
}
