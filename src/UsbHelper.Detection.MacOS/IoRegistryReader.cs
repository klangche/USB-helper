using System.Runtime.Versioning;

namespace UsbHelper.Detection.MacOS;

/// <summary>Reads the live IORegistry through IOKit. Read-only; no administrator rights needed.</summary>
[SupportedOSPlatform("macos")]
internal sealed class IoRegistryReader : IIoRegistryReader
{
    private const string UsbPlane = "IOUSB";
    private const string ServicePlane = "IOService";
    private const string UsbDeviceClass = "IOUSBHostDevice";
    private const string UsbInterfaceClass = "IOUSBHostInterface";

    // Only these properties are read; a serial number or UUID is never touched.
    private static readonly string[] Keys =
        ["locationID", "idVendor", "idProduct", "bDeviceClass", "USB Product Name", "kUSBProductString", "PortNumber", "port", "bInterfaceClass",
        // On the controller (#330): the tiers it allows, 6 on an M1 Max, which hides one hub inside the computer.
        "UsbHostControllerTierLimit"];

    public IReadOnlyList<RegistryEntry> ReadControllers()
    {
        var root = IoKit.IORegistryGetRootEntry(0);
        if (root == 0)
        {
            throw new InvalidOperationException("The IORegistry could not be opened.");
        }

        try
        {
            return Children(root, UsbPlane).Select(ReadController).ToList();
        }
        finally
        {
            IoKit.IOObjectRelease(root);
        }
    }

    // Devices are looked up in both planes, because which plane lists a device under its port depends on the chip generation.
    private static RegistryEntry ReadController(uint controller)
    {
        var seen = new HashSet<ulong>();
        var tree = Read(controller, ServicePlane, seen, out var error);
        var extra = new List<RegistryEntry>();
        foreach (var child in Children(controller, UsbPlane))
        {
            if (IoKit.IORegistryEntryGetRegistryEntryID(child, out var id) != 0 || seen.Add(id))
            {
                extra.Add(Read(child, UsbPlane, seen, out _));
            }

            IoKit.IOObjectRelease(child);
        }

        return tree with { Children = [.. tree.Children, .. extra], Error = error };
    }

    private static RegistryEntry Read(uint entry, string plane, HashSet<ulong> seen, out string? error)
    {
        error = null;
        var properties = Properties(entry);
        var isUsbDevice = IoKit.IOObjectConformsTo(entry, UsbDeviceClass);

        var children = new List<RegistryEntry>();
        // #274: the IOUSB plane lists devices only; a device's interfaces, with their bInterfaceClass, are its children
        // in the IOService plane.
        if (isUsbDevice && plane == UsbPlane)
        {
            children.AddRange(Interfaces(entry));
        }

        foreach (var child in Children(entry, plane))
        {
            try
            {
                if (IoKit.IORegistryEntryGetRegistryEntryID(child, out var id) == 0 && !seen.Add(id))
                {
                    continue;
                }

                children.Add(Read(child, plane, seen, out _));
            }
            finally
            {
                IoKit.IOObjectRelease(child);
            }
        }

        var className = IoKit.ClassName(entry);
        var name = IoKit.EntryName(entry);
        if (className == null || name == null)
        {
            error = "the registry did not return the entry's name";
        }

        return new RegistryEntry(className ?? "", name ?? "", isUsbDevice, properties, children, error);
    }

    private static Dictionary<string, object> Properties(uint entry)
    {
        var properties = new Dictionary<string, object>();
        foreach (var key in Keys)
        {
            if (IoKit.Property(entry, key) is { } value)
            {
                properties[key] = value;
            }
        }

        return properties;
    }

    // The device's IOUSBHostInterface entries in the IOService plane, without what is below them.
    private static List<RegistryEntry> Interfaces(uint device)
    {
        var interfaces = new List<RegistryEntry>();
        foreach (var child in Children(device, ServicePlane))
        {
            try
            {
                if (IoKit.IOObjectConformsTo(child, UsbInterfaceClass))
                {
                    interfaces.Add(new RegistryEntry(IoKit.ClassName(child) ?? "", IoKit.EntryName(child) ?? "", false, Properties(child), []));
                }
            }
            finally
            {
                IoKit.IOObjectRelease(child);
            }
        }

        return interfaces;
    }

    private static List<uint> Children(uint entry, string plane)
    {
        var result = new List<uint>();
        if (IoKit.IORegistryEntryGetChildIterator(entry, plane, out var iterator) != 0)
        {
            return result;
        }

        try
        {
            for (var child = IoKit.IOIteratorNext(iterator); child != 0; child = IoKit.IOIteratorNext(iterator))
            {
                result.Add(child);
            }
        }
        finally
        {
            IoKit.IOObjectRelease(iterator);
        }

        return result;
    }
}
