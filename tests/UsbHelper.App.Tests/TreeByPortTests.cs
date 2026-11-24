using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Topology;
using UsbHelper.Detection.MacOS;
using UsbHelper.Detection.Windows;
using MacTree = UsbHelper.Detection.MacOS.UsbTreeBuilder;
using WindowsTree = UsbHelper.Detection.Windows.UsbTreeBuilder;

namespace UsbHelper.App.Tests;

// #370: the tree by the computer's physical ports, the same on a Mac and a PC for the same setup.
public class TreeByPortTests
{
    // ---- The owner's M1 Max with the Parade PS9010 dock in receptacle 1 (Mac run B on #267, 2026-11-23) ----
    // Names and locations as recorded. Vendor and product ids were not recorded on the Mac; they are the dock's own, as
    // the Asus recorded them (#354): a device's descriptor is the same on every computer. The hub ports are the entries
    // IOKit lists for each hub port in use (AppleUSB20HubPort, AppleUSB30HubPort), which give each port its bus: they
    // are not in the recording. The "Parade Tech USB2.0 HUB" is a hub with nothing on it, as recorded; on Windows it is a
    // device (WinUSB), and the text shows both the same.
    internal const string MacThunderboltJson = """
        {"SPThunderboltDataType":[
          {"_name":"thunderboltusb4_bus_2","device_name_key":"MacBook Pro","receptacle_1_tag":{"current_speed_key":"Up to 40 Gb/s","receptacle_id_key":"3","receptacle_status_key":"receptacle_no_devices_connected"},"route_string_key":"0","vendor_name_key":"Apple Inc."},
          {"_name":"thunderboltusb4_bus_1","device_name_key":"MacBook Pro","receptacle_1_tag":{"current_speed_key":"Up to 40 Gb/s","receptacle_id_key":"2","receptacle_status_key":"receptacle_no_devices_connected"},"route_string_key":"0","vendor_name_key":"Apple Inc."},
          {"_items":[{"_name":"PS9010","device_id_key":"0x9010","device_name_key":"PS9010","mode_key":"usb_four","route_string_key":"1","switch_version_key":"1.3","vendor_id_key":"0x1DA0","vendor_name_key":"Parade Technologies"}],
           "_name":"thunderboltusb4_bus_0","device_name_key":"MacBook Pro","receptacle_1_tag":{"current_speed_key":"40 Gb/s","receptacle_id_key":"1","receptacle_status_key":"receptacle_connected"},"route_string_key":"0","vendor_name_key":"Apple Inc."}]}
        """;

    private static RegistryEntry MacDevice(long location, long vendor, long product, string name, long deviceClass = 0, params RegistryEntry[] children) =>
        new("IOUSBHostDevice", "dev", true, new Dictionary<string, object>
        {
            ["locationID"] = location,
            ["idVendor"] = vendor,
            ["idProduct"] = product,
            ["bDeviceClass"] = deviceClass,
            ["USB Product Name"] = name,
        }, children);

    // A hub with the port entries of its ports in use, then the devices on them.
    private static RegistryEntry MacHub(long location, long vendor, long product, string name, bool usb3, params RegistryEntry[] devices)
    {
        var driver = usb3 ? "AppleUSB30Hub" : "AppleUSB20Hub";
        var ports = devices.Select(device => new RegistryEntry(usb3 ? "AppleUSB30HubPort" : "AppleUSB20HubPort", "port", false,
            new Dictionary<string, object> { ["locationID"] = device.GetNumber("locationID")! }, []));
        return MacDevice(location, vendor, product, name, 9,
            new RegistryEntry(driver, driver, false, new Dictionary<string, object>(), [.. ports, .. devices]));
    }

    private static RegistryEntry MacRootPort(string name, string className, long location, params RegistryEntry[] children) =>
        new(className, name, false, new Dictionary<string, object> { ["locationID"] = location, ["PortNumber"] = 1L }, children);

    internal static IReadOnlyList<UsbNode> MacDock() => MacTree.Build(
    [
        new RegistryEntry("AppleT6000USBXHCI", "AppleT6000USBXHCI@00000000", false,
            new Dictionary<string, object> { ["locationID"] = 0L, ["UsbHostControllerTierLimit"] = 6L },
            [
                MacRootPort("usb-drd0-port-hs", "AppleUSB20XHCIARMPort", 0x00100000, MacHub(0x00100000, 0x1DA0, 0x901B, "USB2.0 Hub", usb3: false,
                    MacHub(0x00120000, 0x05E3, 0x0610, "USB2.1 Hub", usb3: false, MacDevice(0x00122000, 0x6993, 0xB022, "WPP20")),
                    MacHub(0x00130000, 0x1DA0, 0x5513, "Parade Tech USB2.0 HUB", usb3: false),
                    MacDevice(0x00140000, 0x1DA0, 0x901C, "Billboard Device"),
                    MacDevice(0x00150000, 0x1DA0, 0x901D, "HID device"))),
                MacRootPort("usb-drd0-port-ss", "AppleT8103USB30XHCIPort", 0x00200000, MacHub(0x00200000, 0x1DA0, 0x901A, "USB3.2 Hub", usb3: true,
                    MacHub(0x00220000, 0x05E3, 0x0626, "USB3.1 Hub", usb3: true, MacDevice(0x00224000, 0x0BDA, 0x8153, "USB 10/100/1000 LAN")))),
            ]),
        new RegistryEntry("AppleT6000USBXHCI", "AppleT6000USBXHCI@01000000", false,
            new Dictionary<string, object> { ["locationID"] = 0x01000000L, ["UsbHostControllerTierLimit"] = 6L },
            [MacRootPort("usb-drd1-port-hs", "AppleUSB20XHCIARMPort", 0x01100000), MacRootPort("usb-drd1-port-ss", "AppleT8103USB30XHCIPort", 0x01200000)]),
    ], MacThunderbolt.Parse(MacThunderboltJson));

    // ---- The owner's Asus ExpertBook B5402CBA with the same dock (#354, 2026-11-23) ----
    // Instance ids, names (the bus-reported ones, which the app shows, #39), locations, containers, _PLD buffers and
    // InLocalMachineContainer as recorded. Assumed, as the recording has no connector properties (IOCTL_USB_GET_PORT_
    // CONNECTOR_PROPERTIES): the dock's two root ports are user-connectable and USB-C (the _PLD's user-visible bit is 1
    // for them, and it is a USB4 dock), the ports of the webcam, fingerprint reader and Bluetooth internal (their _PLD's
    // bit is 0). The bus of each port is assumed from its controller and hub (Port_#….Hub_#…, SS01 or HS02).
    private static readonly Guid LocalMachine = new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");
    private static readonly Guid DockBox = new("1DA04602-B000-2107-0000-000000000000");
    private static readonly Guid Genesys = new("9F4B56F0-1DF6-11E0-AC64-0800200C9A66");
    private const string DockPld = "01000000080003000104000100000000FFFFFFFF";

    private static RawDevNode Node(string id, string name, Guid container, bool local, string? pld = null) =>
        new(id, name, null) { ContainerId = container, InLocalMachine = local, PhysicalLocation = pld };

    private static RawPort WinPort(int number, UsbBus bus, ushort vendor, ushort product, RawDevNode device, RawHub? hub = null, bool? connectable = null, bool? usbC = null) =>
        new(number, 1, hub != null, vendor, product, device, hub) { Bus = bus, UserConnectable = connectable, UsbC = usbC };

    private static RawHub WinHub(RawDevNode node, params RawPort[] ports) => new(node, ports, null);

    internal static IReadOnlyList<UsbNode> AsusDock()
    {
        var genesys2 = Node(@"USB\VID_05E3&PID_0610\6&2F43B78F&0&2", "USB2.1 Hub", Genesys, false);
        var dock2 = Node(@"USB\VID_1DA0&PID_901B\5&242A2F40&0&2", "USB2.0 Hub", DockBox, false, DockPld);
        var pch = WinHub(Node(@"USB\ROOT_HUB30\4&3AF0ECE5&0&0", "USB Root Hub (USB 3.0)", LocalMachine, true),
            WinPort(2, UsbBus.Usb2, 0x1DA0, 0x901B, dock2, WinHub(dock2,
                WinPort(2, UsbBus.Usb2, 0x05E3, 0x0610, genesys2, WinHub(genesys2,
                    WinPort(2, UsbBus.Usb2, 0x6993, 0xB022, Node(@"USB\VID_6993&PID_B022\serial", "WPP20", new("2D1B34FA-8C4A-5ACA-B3FD-7F6C9B7256E5"), false)))),
                WinPort(3, UsbBus.Usb2, 0x1DA0, 0x5513, Node(@"USB\VID_1DA0&PID_5513\6&2F43B78F&0&3", "Parade Tech USB2.0 HUB", new("5373EDE4-E712-11F1-A5F5-A0595077B577"), false)),
                WinPort(4, UsbBus.Usb2, 0x1DA0, 0x901C, Node(@"USB\VID_1DA0&PID_901C\6&2F43B78F&0&4", "Billboard Device", DockBox, false)),
                WinPort(5, UsbBus.Usb2, 0x1DA0, 0x901D, Node(@"USB\VID_1DA0&PID_901D\6&2F43B78F&0&5", "HID device", DockBox, false))),
                connectable: true, usbC: true),
            WinPort(7, UsbBus.Usb2, 0x13D3, 0x56EB, Node(@"USB\VID_13D3&PID_56EB\serial", "USB2.0 HD UVC WebCam", LocalMachine, true, "01000000000000000000800300000000FFFFFFFF"), connectable: false),
            WinPort(9, UsbBus.Usb2, 0x04F3, 0x0C77, Node(@"USB\VID_04F3&PID_0C77\5&242A2F40&0&9", "ELAN:ARM-M4", LocalMachine, true, "01000000080003000004800400000000FFFFFFFF"), connectable: false),
            WinPort(10, UsbBus.Usb2, 0x8087, 0x0033, Node(@"USB\VID_8087&PID_0033\5&242A2F40&0&10", "Intel(R) Wireless Bluetooth(R)", LocalMachine, true, "01000000080003000004000500000000FFFFFFFF"), connectable: false));
        var genesys3 = Node(@"USB\VID_05E3&PID_0626\6&3B55C92D&0&2", "USB3.1 Hub", Genesys, false);
        var dock3 = Node(@"USB\VID_1DA0&PID_901A\5&A1885C6&0&2", "USB3.2 Hub", DockBox, false, DockPld);
        var typeC = WinHub(Node(@"USB\ROOT_HUB30\4&11BF4672&0&0", "USB Root Hub (USB 3.0)", LocalMachine, true),
            WinPort(2, UsbBus.Usb3, 0x1DA0, 0x901A, dock3, WinHub(dock3,
                WinPort(2, UsbBus.Usb3, 0x05E3, 0x0626, genesys3, WinHub(genesys3,
                    WinPort(4, UsbBus.Usb3, 0x0BDA, 0x8153, Node(@"USB\VID_0BDA&PID_8153\serial", "USB 10/100/1000 LAN", new("3E86D5FF-8F99-50EA-B13B-91971A45286A"), false))))),
                connectable: true, usbC: true));
        return WindowsTree.Build(
        [
            new RawController(Node(@"PCI\VEN_8086&DEV_51ED&SUBSYS_201F1043&REV_01\3&11583659&0&A0", "USB-styrenhet (Universal Serial Bus)", LocalMachine, true), pch, null),
            new RawController(Node(@"PCI\VEN_8086&DEV_461E&SUBSYS_00000000&REV_02\3&11583659&0&68", "USB-styrenhet (Universal Serial Bus)", LocalMachine, true), typeC, null)
            {
                Usb4Routers = 1,
                Usb4Link = "USB4 link: USB4-router (1.0), Parade Technologies – PS9010",
            },
        ]);
    }

    private static MainViewModel Loaded(IReadOnlyList<UsbNode> tree)
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity("Maker", "Model"), () => [], new EventLog());
        viewModel.Load(tree);
        return viewModel;
    }

    // The tree as text below the computer's line, without the built-in group and without the host's own line column,
    // which a group after the port continues.
    private static IReadOnlyList<string> PortRows(MainViewModel viewModel)
    {
        var lines = TreeText.Render(viewModel.Roots[0]).Split(Environment.NewLine).Skip(1).ToList();
        var builtIn = lines.FindIndex(line => line.EndsWith("Built into the computer", StringComparison.Ordinal));
        return [.. (builtIn < 0 ? lines : lines.Take(builtIn)).Select(line => line[4..])];
    }

    private static readonly string[] DockRows =
    [
        "USB-C port 1 [USB-C]",
        "`-- USB4 dock: PS9010 (router, depth 1)",
        "    `-- USB3.2 Hub + USB2.0 Hub (1DA0:901A, USB 2 + USB 3) - unknown",
        "        |-- Port 2",
        "        |   `-- USB3.1 Hub + USB2.1 Hub (05E3:0626, USB 2 + USB 3) - unknown",
        "        |       |-- Port 2",
        "        |       |   `-- WPP20 (6993:B022, USB 2) - unknown",
        "        |       `-- Port 4",
        "        |           `-- USB 10/100/1000 LAN (0BDA:8153, USB 3) - unknown",
        "        |-- Port 3",
        "        |   `-- Parade Tech USB2.0 HUB (1DA0:5513, USB 2) - unknown",
        "        |-- Port 4",
        "        |   `-- Billboard Device (1DA0:901C, USB 2) - unknown",
        "        `-- Port 5",
        "            `-- HID device (1DA0:901D, USB 2) - unknown",
    ];

    // The check of #370: the same dock gives the same tree on the Mac and on the PC, apart from the computer's line and
    // the built-in group: a port with both halves, the USB4 router as its own row, each hub's two halves as one.
    [Fact]
    public void TheRecordedMacAndPcGiveTheSameTreeForTheSameDock()
    {
        var mac = PortRows(Loaded(MacDock()));
        var pc = PortRows(Loaded(AsusDock()));

        Assert.Equal(DockRows, mac.Take(DockRows.Length));
        Assert.Equal(mac, pc);
    }

    // The window shows the same: the port, the router, then the hubs and devices with their ports folded (#264).
    [Fact]
    public void ThePortRowHoldsTheRouterAndTheDocksHubAsOne()
    {
        foreach (var tree in new[] { MacDock(), AsusDock() })
        {
            var host = Loaded(tree).Roots[0];
            var port = host.Rows[0];
            Assert.Equal("USB-C port 1", port.Text);
            Assert.True(port.CanExpand);
            Assert.NotNull(port.HoverPortId);
            var router = Assert.Single(port.Rows);
            Assert.Equal("USB4 dock: PS9010 (router, depth 1)", router.Text);
            var dock = Assert.Single(router.Rows);
            Assert.Equal("USB3.2 Hub + USB2.0 Hub (1DA0:901A, USB 2 + USB 3)", dock.Text);
            Assert.Null(dock.PortNumber); // The port row is the port.
            Assert.Equal(["USB3.1 Hub + USB2.1 Hub (05E3:0626, USB 2 + USB 3)", "Parade Tech USB2.0 HUB (1DA0:5513, USB 2)",
                "Billboard Device (1DA0:901C, USB 2)", "HID device (1DA0:901D, USB 2)"], dock.Rows.Select(row => row.Text));
            Assert.Equal(["2", "3", "4", "5"], dock.Rows.Select(row => row.PortNumber));
        }
    }

    // The webcam, fingerprint reader and Bluetooth of the Asus: in the computer's own container, on internal ports.
    [Fact]
    public void TheDevicesInsideThePcAreBuiltIntoTheComputer()
    {
        var lines = TreeText.Render(Loaded(AsusDock()).Roots[0]).Split(Environment.NewLine);
        var at = Array.FindIndex(lines, line => line.EndsWith("Built into the computer", StringComparison.Ordinal));

        Assert.Equal(
        [
            "`-- Built into the computer",
            "    |-- Port 7",
            "    |   `-- USB2.0 HD UVC WebCam (13D3:56EB, USB 2) - unknown",
            "    |-- Port 9",
            "    |   `-- ELAN:ARM-M4 (04F3:0C77, USB 2) - unknown",
            "    `-- Port 10",
            "        `-- Intel(R) Wireless Bluetooth(R) (8087:0033, USB 2) - unknown",
        ], lines[at..]);
        // The Mac has no USB device inside it.
        Assert.DoesNotContain(TreeText.Render(Loaded(MacDock()).Roots[0]).Split(Environment.NewLine), line => line.Contains("Built into", StringComparison.Ordinal));
    }

    // What cannot be placed on a port: a device outside the computer on a port Windows calls internal, and a controller
    // that could not be read. Its row's tool tip says why.
    [Fact]
    public void WhatCannotBePlacedIsUnderOtherUsbPortsWithWhy()
    {
        static UsbNode Port(string id, int number, UsbNode child, bool? connectable) =>
            new(id, UsbNodeKind.Port, $"Port {number}", [child]) { PortNumber = number, UserConnectable = connectable };
        var keyboard = new UsbNode("KEYBOARD", UsbNodeKind.Device, "Keyboard", []) { InLocalMachine = false };
        var mouse = new UsbNode("MOUSE", UsbNodeKind.Device, "Mouse", []);
        IReadOnlyList<UsbNode> tree =
        [
            new("C1", UsbNodeKind.Controller, "Controller", [new("ROOT", UsbNodeKind.Hub, "Root", [Port("P1", 1, keyboard, false), Port("P2", 2, mouse, true)])]),
            new("C2", UsbNodeKind.Controller, "Broken controller", []) { Problem = "Could not read the root hub: access denied" },
        ];

        var host = Loaded(tree).Roots[0];

        Assert.Equal(["Port 2", "Other USB ports (not identified)"], host.Rows.Select(row => row.Text));
        var other = host.Rows[1];
        Assert.Equal(["Keyboard - unknown", "Broken controller: Could not read the root hub: access denied"],
            other.Rows.Select(row => row.Text + (row.StatusWord is { } word ? " - " + word : "")));
        Assert.Contains("Keyboard: the system calls Port 1 internal, but says the device is not part of the computer.", other.RowTip);
        Assert.Contains("Broken controller: Could not read the root hub: access denied", other.RowTip);
        Assert.Equal(PortTree.OtherId, other.Id);
        Assert.True(other.IsExpanded); // Kept open while the tree follows, so it is not missed.
    }

    // The USB 2 half of the dock's hub is kept with its port, for the row's status and its line's mark.
    [Fact]
    public void TheUsb2HalfOfTheDocksHubIsKnownWithItsPort()
    {
        var tree = MacDock();
        var layout = PortTree.Build(tree);
        var (usb3, half) = Assert.Single(layout.OtherHalves, each => each.Value.Hub.Name == "USB2.0 Hub");
        Assert.Equal("usb-device-00200000", usb3);
        Assert.Equal("usb-controller-00000000|roothub|port1", half.PortId);
    }

    // The port's hover names both controllers of the Asus's USB-C port, with their root hubs, though their names are the same.
    [Fact]
    public void ThePortsHoverNamesTheControllerAndRootHubOfEachHalf()
    {
        var viewModel = Loaded(AsusDock());
        var port = viewModel.Roots[0].Rows[0];

        viewModel.HoverPort(port);

        Assert.Equal(["Controller (USB 3): USB-styrenhet (Universal Serial Bus), USB Root Hub (USB 3.0)",
            "Controller (USB 2): USB-styrenhet (Universal Serial Bus), USB Root Hub (USB 3.0)"],
            port.PortHover!.Lines.Where(line => line.StartsWith("Controller", StringComparison.Ordinal)));
    }

    // Hovering a device on the USB 2 side of a merged hub lights the whole way: the port, the router and both merged hubs.
    [Fact]
    public void HoveringBelowTheUsb2HalfLightsThePathThroughTheMergedHubs()
    {
        var viewModel = Loaded(MacDock());
        var port = viewModel.Roots[0].Rows[0];
        var router = port.Rows[0];
        var dock = router.Rows[0];
        var genesys = dock.Rows[0];
        var wpp20 = genesys.Rows.Single(row => row.Text.StartsWith("WPP20", StringComparison.Ordinal));

        viewModel.HoverPort(wpp20);

        Assert.All(new[] { port, router, dock, genesys, wpp20 }, row => Assert.True(row.IsOnHoveredPath, row.Text));
        Assert.False(genesys.Rows.Single(row => row.Text.StartsWith("USB 10/100", StringComparison.Ordinal)).IsOnHoveredPath);
    }

    // The router read after the hubs: its row comes in above them, and the rows below keep their identity.
    [Fact]
    public void TheRouterRowComesAndGoesWithoutRebuildingTheRowsBelow()
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity("Maker", "Model"), () => [], new EventLog());
        var withoutRouter = MacDock().Select(controller => controller with { Usb4Routers = null, Usb4Link = null }).ToList();
        viewModel.Load(withoutRouter);
        var dock = viewModel.Roots[0].Rows[0].Rows[0];
        Assert.StartsWith("USB3.2 Hub", dock.Text, StringComparison.Ordinal);

        viewModel.Load(MacDock());
        Assert.Same(dock, viewModel.Roots[0].Rows[0].Rows[0].Rows[0]);

        viewModel.Load(withoutRouter);
        Assert.Same(dock, viewModel.Roots[0].Rows[0].Rows[0]);
    }

    // A dropped dock stays one box: its two disconnected halves are one row at the port (#212).
    [Fact]
    public void ADroppedDockIsOneDisconnectedRow()
    {
        var tree = MacDock();
        var viewModel = Loaded(tree);
        var root = tree[0].Children[0];

        // The detector sends each root port as it is now: the same port, with nothing on it.
        viewModel.Apply([.. root.Children.Select(port => new UsbChange(UsbChangeKind.Removed, root.Id, port with { Children = [] }))]);

        var port = viewModel.Roots[0].Rows[0];
        Assert.Equal("USB-C port 1", port.Text);
        // With nothing connected on the port, the router is not placed on it: the dropped dock is right below the port.
        var dropped = Assert.Single(port.Rows);
        Assert.Equal("USB3.2 Hub + USB2.0 Hub (1DA0:901A, USB 2 + USB 3) (disconnected)", dropped.Text);
        Assert.True(dropped.IsDisconnected);
    }

    // #371: Ochno selected on the M1 Max: its max 5 hubs is marked on the dock's deepest hubs with the room left, once on
    // a hub shown with its other half, in the window, the text and the report.
    [Fact]
    public void TheSelectedPhilosophysLimitIsMarkedOnTheDocksDeepestHubs()
    {
        var db = DatabaseFormats.Read(4, EmbeddedDatabase.Files()[4]);
        var viewModel = new MainViewModel(db, [], new ComputerIdentity("Maker", "Model", Platform: "macos-apple-silicon"), () => [], new EventLog())
        {
            Guide = db.Guides["ochno"],
        };
        viewModel.Load(MacDock());
        var dock = viewModel.Roots[0].Rows[0].Rows[0].Rows[0];
        var genesys = dock.Rows[0];

        Assert.Empty(dock.LimitBadges);
        Assert.Equal(["Ochno: chain max 5 hubs · 2 more fit behind", "Ochno: room max 4 hubs · 2 more fit behind"], genesys.LimitBadges.Select(badge => badge.Text));
        Assert.All(genesys.LimitBadges, badge => Assert.Equal(("#8E24AA", false), (badge.Color, badge.Dashed)));
        Assert.Contains(TreeText.Render(viewModel.Roots[0]).Split(Environment.NewLine),
            line => line.EndsWith("USB3.1 Hub + USB2.1 Hub (05E3:0626, USB 2 + USB 3) - stable (Ochno: chain max 5 hubs · 2 more fit behind) (Ochno: room max 4 hubs · 2 more fit behind)", StringComparison.Ordinal));
    }

    // The two halves of a hub shown as one with different markings of one rule (Ochno's example E, a cable marked on USB 2
    // only): each badge names its half's bus. The same marking on both halves is one badge without a bus.
    [Fact]
    public void HalvesWithDifferentMarkingsNameTheirBus()
    {
        LimitMark Chain(LimitPlace place, int count, UsbBus bus) =>
            new("Ochno", "chain", LimitUnit.Hubs, LimitLevel.Max, 5, place, count, false, [], [], null, bus);

        var differ = LimitBadge.For([Chain(LimitPlace.Room, 3, UsbBus.Usb3), Chain(LimitPlace.Reached, 5, UsbBus.Usb2)]);
        var same = LimitBadge.For([Chain(LimitPlace.Room, 3, UsbBus.Usb3), Chain(LimitPlace.Room, 3, UsbBus.Usb2)]);

        Assert.Equal(["Ochno: chain max 5 hubs (USB 3) · 2 more fit behind", "Ochno: chain max 5 hubs (USB 2) · reached here"], differ.Select(badge => badge.Text));
        Assert.Equal(["Ochno: chain max 5 hubs · 2 more fit behind"], same.Select(badge => badge.Text));
        // A device rule's room is in front of the device.
        Assert.Equal("Cameras: tiers max 4 tiers (camera) · 1 more hubs fit in front",
            LimitBadge.Of(new LimitMark("Cameras", "tiers", LimitUnit.Tiers, LimitLevel.Max, 4, LimitPlace.RoomInFront, 3, false, [], ["camera"], null)).Text);
    }
}
