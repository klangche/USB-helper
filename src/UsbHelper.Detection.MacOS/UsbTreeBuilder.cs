using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.MacOS;

/// <summary>
/// Turns IORegistry entries into the operating-system-neutral tree. The registry parent of a device differs between
/// chip generations, so the place of a device comes only from its locationID: the top byte is the bus, then one
/// nibble per hub level holds the port number (0x01120000 is port 2 of the hub on port 1 of bus 1).
/// </summary>
internal static class UsbTreeBuilder
{
    private const int HubClass = 9;

    public static IReadOnlyList<UsbNode> Build(IReadOnlyList<RegistryEntry> controllers) =>
        controllers.Select(Controller).ToList();

    /// <summary>Port numbers from the nibbles of a locationID, ending at the first zero nibble.</summary>
    internal static IReadOnlyList<int> PortPath(long locationId)
    {
        var path = new List<int>();
        for (var shift = 20; shift >= 0; shift -= 4)
        {
            var nibble = (int)((locationId >> shift) & 0xF);
            if (nibble == 0)
            {
                break;
            }

            path.Add(nibble);
        }

        return path;
    }

    private sealed record Placed(RegistryEntry Entry, long LocationId, IReadOnlyList<int> Path)
    {
        /// <summary>Ports the hub's own port entries list, so a hub with nothing connected still has its ports.</summary>
        public IReadOnlySet<int> HubPorts { get; } = PortsOf(Entry);
    }

    // A port entry's place on its hub is "port", the same number as its locationID nibble. On Apple silicon a root port
    // also has "PortNumber", which is the USB-C receptacle and is the same on its USB 2 and USB 3 entry (#187), so it is
    // only a fallback. Port entries sit below the hub, not below the devices on it.
    private static long? PortNumberOf(RegistryEntry entry) => entry.GetNumber("port") ?? entry.GetNumber("PortNumber");

    private static HashSet<int> PortsOf(RegistryEntry hub)
    {
        var numbers = new HashSet<int>();
        void Walk(RegistryEntry entry)
        {
            foreach (var child in entry.Children)
            {
                if (child.IsUsbDevice)
                {
                    continue;
                }

                if (PortNumberOf(child) is { } number and > 0)
                {
                    numbers.Add((int)number);
                }

                Walk(child);
            }
        }

        Walk(hub);
        return numbers;
    }

    private static UsbNode Controller(RegistryEntry controller, int index)
    {
        var location = controller.GetNumber("locationID");
        var id = location is { } loc ? $"usb-controller-{loc:x8}" : $"usb-controller-unknown{index}";

        var devices = new List<Placed>();
        var hubPorts = new List<IReadOnlyList<int>>();
        var portNumbers = new SortedSet<int>();
        var unplaced = 0;
        Collect(controller.Children, devices, hubPorts, portNumbers, ref unplaced);

        foreach (var device in devices)
        {
            portNumbers.Add(device.Path[0]);
        }

        var rootHubId = id + "|roothub";
        var ports = portNumbers.Select(number => Port(rootHubId, [], number, devices, hubPorts)).ToList();
        var problem = controller.Error != null ? $"Could not read the controller: {controller.Error}"
            : unplaced > 0 ? $"{unplaced} USB device(s) could not be placed because they report no location."
            : null;
        return new UsbNode(id, UsbNodeKind.Controller, controller.Name, [new UsbNode(rootHubId, UsbNodeKind.Hub, "USB root hub", ports)])
        {
            Problem = problem,
        };
    }

    private static void Collect(IReadOnlyList<RegistryEntry> entries, List<Placed> devices, List<IReadOnlyList<int>> hubPorts, SortedSet<int> portNumbers, ref int unplaced)
    {
        foreach (var entry in entries)
        {
            if (entry.IsUsbDevice)
            {
                var location = entry.GetNumber("locationID");
                var path = location is { } l ? PortPath(l) : [];
                if (path.Count > 0)
                {
                    devices.Add(new Placed(entry, location!.Value, path));
                }
                else if (location == null)
                {
                    unplaced++;
                }
                // A device with a location but no port is the controller's root hub entry: its children are collected below.
            }
            else if (entry.ClassName.EndsWith("HubPort", StringComparison.Ordinal) && entry.GetNumber("locationID") is { } portLocation)
            {
                // A hub port exists whether or not something is plugged in; without it an unplugged device has no port to show.
                hubPorts.Add(PortPath(portLocation));
            }
            else if (PortNumberOf(entry) is { } number and > 0)
            {
                portNumbers.Add((int)number);
            }

            // Ports below a hub on a port belong to that hub, not to the root hub.
            var below = entry.IsUsbDevice && entry.GetNumber("locationID") is { } at && PortPath(at).Count > 0 ? new SortedSet<int>() : portNumbers;
            Collect(entry.Children, devices, hubPorts, below, ref unplaced);
        }
    }

    private static UsbNode Port(string hubId, IReadOnlyList<int> hubPath, int number, List<Placed> devices, List<IReadOnlyList<int>> hubPorts)
    {
        var id = $"{hubId}|port{number}";
        var path = hubPath.Append(number).ToList();
        var occupant = devices.FirstOrDefault(d => d.Path.SequenceEqual(path));
        return new UsbNode(id, UsbNodeKind.Port, $"Port {number}", occupant == null ? [] : [Occupant(occupant, devices, hubPorts)])
        {
            PortNumber = number,
        };
    }

    private static UsbNode Occupant(Placed placed, List<Placed> devices, List<IReadOnlyList<int>> hubPorts)
    {
        var entry = placed.Entry;
        var id = $"usb-device-{placed.LocationId:x8}";
        var isHub = entry.GetNumber("bDeviceClass") == HubClass;
        var vendor = entry.GetNumber("idVendor");
        var product = entry.GetNumber("idProduct");
        var name = entry.GetString("USB Product Name") ?? entry.GetString("kUSBProductString")
            ?? (isHub ? "USB hub" : "Unknown USB device");

        IReadOnlyList<UsbNode> children = [];
        if (isHub)
        {
            var below = devices.Select(d => d.Path).Concat(hubPorts)
                .Where(path => path.Count == placed.Path.Count + 1 && path.Take(placed.Path.Count).SequenceEqual(placed.Path));
            children = below.Select(path => path[^1]).Concat(placed.HubPorts).Distinct().Order()
                .Select(number => Port(id, placed.Path, number, devices, hubPorts)).ToList();
        }

        return new UsbNode(id, isHub ? UsbNodeKind.Hub : UsbNodeKind.Device, name, children)
        {
            VendorId = vendor is >= 0 and <= ushort.MaxValue ? (ushort)vendor : null,
            ProductId = product is >= 0 and <= ushort.MaxValue ? (ushort)product : null,
            Problem = entry.Error == null ? null : $"Could not read the device: {entry.Error}",
        };
    }
}
