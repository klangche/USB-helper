using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

/// <summary>Testing a setup on several of the computer's ports (#342).</summary>
public class PortTestingTests
{
    private static UsbNode Ids(UsbNode node, ushort vendor, ushort product) => node with { VendorId = vendor, ProductId = product };

    // A dock with a hub, a camera and an audio device; ids carry the port, as a system gives new ids on another port.
    private static UsbNode Dock(string at, bool withCamera = true) => Ids(Hub($"DOCK{at}",
        withCamera ? Ids(Device($"CAMERA{at}") with { Name = "Camera" }, 0x046D, 0x0893) : null,
        Ids(Device($"AUDIO{at}") with { Name = "Speaker" }, 0x0B0E, 0x0420)), 0x2109, 0x2817);

    // The computer with nothing plugged in.
    private static readonly IReadOnlyList<UsbNode> Empty = Computer(null);

    // A computer with three ports: Port 1 (USB-C) and 3 on USB 3, Port 2 on USB 2; the dock on the given port, if any.
    private static IReadOnlyList<UsbNode> Computer(int? dockOn, UsbNode? dock = null)
    {
        UsbNode? On(int port) => dockOn == port ? (dock ?? Dock($"P{port}")).OnBus(port == 2 ? UsbBus.Usb2 : UsbBus.Usb3) : null;
        var root = Hub("ROOT", On(1), On(2), On(3));
        return [Controller("C1", root with
        {
            Children = [root.Children[0] with { Bus = UsbBus.Usb3, UserConnectable = true, UsbC = true }, root.Children[1] with { Bus = UsbBus.Usb2, UserConnectable = true },
                root.Children[2] with { Bus = UsbBus.Usb3, UserConnectable = true }],
        })];
    }

    [Fact]
    public void TheSameSetupOnAnotherPortIsAMoveNotADisconnect()
    {
        var history = new SessionHistory();
        var testing = new PortTesting();
        var before = Computer(1);
        var after = Computer(2);
        history.Record(before);

        // Unplugged and plugged in again arrive as two changes, with the empty computer in between.
        Assert.Null(testing.Note(before, Empty));
        history.Record(Empty);
        var move = testing.Note(Empty, after);
        history.Record(after);
        history.ApplyMove(move!);

        Assert.NotNull(move);
        Assert.Equal("ROOT|port1", move.From.Id);
        Assert.Equal("ROOT|port2", move.To.Id);
        Assert.Equal("Port 1 (USB-C, USB 3)", move.From.Label);
        Assert.Equal("Port 2 (USB 2)", move.To.Label);
        Assert.Empty(move.NotBack);
        Assert.Empty(history.Missing(after));
        Assert.Empty(history.Reconnected(after));
    }

    // The same change with both at once also counts.
    [Fact]
    public void AMoveInOneChangeIsAlsoAMove() => Assert.NotNull(new PortTesting().Note(Computer(1), Computer(2)));

    // Plugged back into the same port: a replug, not a reconnect (#358), and nothing is left to match later.
    [Fact]
    public void BackOnTheSamePortIsAReplugAndIsForgotten()
    {
        var testing = new PortTesting();
        testing.Note(Computer(1), Empty);
        var replug = testing.Note(Empty, Computer(1));
        Assert.Equal(replug!.From.Id, replug.To.Id);

        Assert.Null(testing.Note(Empty, Computer(2))); // Port 1 is not empty any more: nothing moved from it.
    }

    // One device of the setup drops and comes back while the rest stays connected: no replug, still "reconnected" (#54).
    [Fact]
    public void ASingleDeviceDroppingIsNotAReplug()
    {
        var testing = new PortTesting();
        var withoutCamera = Computer(1, Dock("P1", withCamera: false));
        testing.Note(Computer(1), withoutCamera);

        Assert.Null(testing.Note(withoutCamera, Computer(1)));
    }

    // A device that dropped and came back on a port that stayed connected is no longer waiting (#358 review): after the
    // setup moves, the same device dropping on the new port is still "reconnected", not a move from the old one.
    [Fact]
    public void ADeviceThatCameBackIsNotLeftWaiting()
    {
        var testing = new PortTesting();
        var withoutCamera1 = Computer(1, Dock("P1", withCamera: false));
        var withoutCamera3 = Computer(3, Dock("P3", withCamera: false));
        testing.Note(Computer(1), withoutCamera1);
        testing.Note(withoutCamera1, Computer(1));
        testing.Note(Computer(1), Empty);
        var move = testing.Note(Empty, Computer(3))!;
        Assert.Empty(move.NotBack);

        testing.Note(Computer(3), withoutCamera3);
        Assert.Null(testing.Note(withoutCamera3, Computer(3)));
    }

    // What a move left behind is not claimed by a later arrival once the rest of the setup stopped arriving.
    [Fact]
    public void WhatAMoveLeftBehindIsForgotten()
    {
        var testing = new PortTesting();
        var withoutCamera3 = Computer(3, Dock("P3", withCamera: false));
        testing.Note(Computer(1), Empty);
        Assert.Single(testing.Note(Empty, withoutCamera3)!.NotBack); // The camera stayed behind.
        testing.Note(withoutCamera3, withoutCamera3); // A change with nothing new.

        Assert.Null(testing.Note(withoutCamera3, Computer(3))); // The camera plugged in later is not a move from port 1.
    }

    // The halves of a USB-C port arrive as two changes (#358): the USB 2 half first, then the USB 3 half; both join the
    // move from the empty port, and nothing is left behind.
    [Fact]
    public void AMoveWhoseHalvesArriveSeparatelyIsOneMove()
    {
        var history = new SessionHistory();
        var testing = new PortTesting();
        history.Record(Asus(dock: true));
        testing.Update(Asus(dock: true), Green(Asus(dock: true)));
        testing.Note(Asus(dock: true), Asus(dock: false));
        history.Record(Asus(dock: false));
        // Back on the same connector, the USB 2 half first.
        var first = testing.Note(Asus(dock: false), AsusUsb2Only())!;
        history.Record(AsusUsb2Only());
        history.ApplyMove(first);
        var second = testing.Note(AsusUsb2Only(), Asus(dock: true))!;
        history.Record(Asus(dock: true));
        history.ApplyMove(second);

        Assert.Equal("TXHC|port1", first.To.Id);
        Assert.Equal(first.From.Id, second.From.Id);
        Assert.Empty(second.NotBack);
        Assert.Empty(history.Missing(Asus(dock: true)));
        Assert.Empty(history.Reconnected(Asus(dock: true)));
    }

    [Fact]
    public void AMoveWhereTheCameraDidNotComeBackListsItAsMissing()
    {
        var history = new SessionHistory();
        var testing = new PortTesting();
        var before = Computer(1);
        var after = Computer(2, Dock("P2", withCamera: false));
        history.Record(before);
        testing.Note(before, Empty);
        history.Record(Empty);

        var move = testing.Note(Empty, after)!;
        history.Record(after);
        history.ApplyMove(move);

        Assert.Equal(["CAMERAP1"], move.NotBack.Select(node => node.Id));
        Assert.Equal(["CAMERAP1"], history.Missing(after).Select(device => device.Node.Id));
    }

    // Back on a port it was on before: the old ids are seen again, and still not "reconnected".
    [Fact]
    public void MovingBackIsNotAReconnect()
    {
        var history = new SessionHistory();
        var testing = new PortTesting();
        history.Record(Computer(1));
        testing.Note(Computer(1), Empty);
        history.Record(Empty);
        history.Record(Computer(2));
        history.ApplyMove(testing.Note(Empty, Computer(2))!);

        testing.Note(Computer(2), Empty);
        history.Record(Empty);
        history.Record(Computer(1));
        history.ApplyMove(testing.Note(Empty, Computer(1))!);

        Assert.Empty(history.Reconnected(Computer(1)));
        Assert.Empty(history.Missing(Computer(1)));
    }

    [Fact]
    public void ADifferentSetupIsNotAMove()
    {
        var other = Ids(Hub("OTHER", Ids(Device("MOUSE"), 0x046D, 0xC077)), 0x05E3, 0x0610);

        var testing = new PortTesting();
        testing.Note(Computer(1), Empty);

        Assert.Null(testing.Note(Empty, Computer(2, other)));
    }

    private static IEnumerable<UsbNode> All(IReadOnlyList<UsbNode> nodes) => nodes.SelectMany(node => All(node.Children).Prepend(node));

    private static Dictionary<string, ConnectionStatus> Green(IReadOnlyList<UsbNode> tree) =>
        All(tree).ToDictionary(node => node.Id, _ => ConnectionStatus.Green);

    [Fact]
    public void ACameraOnUsb2OnOnePortIsADifferenceNamingBothPorts()
    {
        var testing = new PortTesting();
        testing.Update(Computer(1), Green(Computer(1)));
        testing.Update(Computer(2), Green(Computer(2)));

        var difference = Assert.Single(testing.Differences(), each => each.Kind == PortDifferenceKind.Usb2 && each.Device == "Camera");
        Assert.Equal("On Port 2 the Camera runs on USB 2 (USB 3 on Port 1).", PortTesting.DifferenceText(difference));
    }

    // Different setups are not compared (#358): the camera was not plugged in on Port 3, which is no difference.
    [Fact]
    public void DifferentSetupsAreNotCompared()
    {
        var testing = new PortTesting();
        var poor = Computer(3, Dock("P3", withCamera: false));
        var statuses = Green(poor);
        statuses["DOCKP3"] = ConnectionStatus.Orange;
        testing.Update(poor, statuses);
        testing.Update(Computer(1), Green(Computer(1)));

        Assert.Empty(testing.Differences());
    }

    [Fact]
    public void ThePortsTestedSummary()
    {
        var testing = new PortTesting();
        testing.Update(Computer(1), Green(Computer(1)));
        Assert.StartsWith("Tested on 1 of 3 ports, all stable. Port 2 not tested. Port 3 not tested.", PortTesting.LogText(testing.Summary(), testing.Differences()), StringComparison.Ordinal);

        var onTwo = Computer(2);
        var statuses = Green(onTwo);
        statuses["CAMERAP2"] = ConnectionStatus.Orange;
        testing.Update(onTwo, statuses);
        Assert.Equal("Tested on 2 of 3 ports: Port 1 stable, Port 2 potentially unstable (Camera on USB 2). Port 3 not tested.",
            PortTesting.LogText(testing.Summary(), testing.Differences()));

        testing.Reset();
        testing.Update(Computer(1), Green(Computer(1)));
        testing.Update(Computer(3), Green(Computer(3)));
        testing.Update(Computer(2), Green(Computer(2)));
        Assert.Equal("Tested on 3 of 3 ports, all stable.", PortTesting.LogText(testing.Summary(), []));
    }

    // A computer with two USB 3 ports, Port 1 and Port 2; the dock on the given one.
    private static IReadOnlyList<UsbNode> On(int port)
    {
        UsbNode Port(int number, UsbNode? child) => new($"ROOT|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child])
        {
            PortNumber = number,
            Bus = UsbBus.Usb3,
            UserConnectable = true,
        };
        return [Controller("C1", new UsbNode("ROOT", UsbNodeKind.Hub, "Root hub", [Port(1, port == 1 ? Dock("A") : null), Port(2, port == 2 ? Dock("B") : null)]))];
    }

    private static ResolvedProfile Mac(string model) =>
        HardwareDatabase.Load(Database.RealDatabaseTests.DatabaseFolder()).Computers[ComputerProfileId.From("Apple Inc.", model)];

    // The computer file's USB ports give the total: the MacBook Neo has two.
    [Fact]
    public void TheTotalComesFromTheComputerFile()
    {
        var testing = new PortTesting();
        testing.Update(On(1), Green(On(1)));

        var tested = testing.Summary(Mac("Mac17,5"));
        Assert.Equal(2, tested.Total);
        Assert.Equal("Tested on 1 of 2 ports, all stable. Port 2 not tested.", PortTesting.LogText(tested, []));
    }

    // The MacBook Pro file has one thunderbolt4 entry for its three ports: the three ports seen count, not the one entry.
    [Fact]
    public void AFileWithFewerEntriesThanPortsSeenGivesThePortsSeen()
    {
        var testing = new PortTesting();
        testing.Update(Computer(1), Green(Computer(1)));

        Assert.Equal(3, testing.Summary(Mac("MacBookPro18,2")).Total);
    }

    [Fact]
    public void TwoOfTwoPortsAllStable()
    {
        var testing = new PortTesting();
        testing.Update(On(1), Green(On(1)));
        testing.Update(On(2), Green(On(2)));

        Assert.Equal("Tested on 2 of 2 ports, all stable.", PortTesting.LogText(testing.Summary(), testing.Differences()));
        Assert.Empty(testing.Differences());
    }

    [Fact]
    public void ADeviceWithAnErrorOnOnePortIsADifference()
    {
        var testing = new PortTesting();
        testing.Update(Computer(1), Green(Computer(1)));
        var broken = Computer(3, Ids(Hub("DOCKP3", Ids(Device("CAMERAP3") with { Name = "Camera", Problem = "Code 43" }, 0x046D, 0x0893),
            Ids(Device("AUDIOP3") with { Name = "Speaker" }, 0x0B0E, 0x0420)), 0x2109, 0x2817));
        // The error also turns the port purple; that is the same cause, not also "over a limit".
        var statuses = Green(broken);
        statuses["DOCKP3"] = ConnectionStatus.Purple;
        statuses["CAMERAP3"] = ConnectionStatus.Purple;
        testing.Update(broken, statuses);

        var difference = Assert.Single(testing.Differences());
        Assert.Equal("On Port 3 the Camera has an error; on Port 1 it has none.", PortTesting.DifferenceText(difference));
    }

    [Fact]
    public void WithinTheLimitsOnOnePortAndOverOnAnotherIsADifference()
    {
        var testing = new PortTesting();
        testing.Update(Computer(1), Green(Computer(1)));
        var over = Green(Computer(3));
        over["DOCKP3"] = ConnectionStatus.Purple;
        testing.Update(Computer(3), over);

        var difference = Assert.Single(testing.Differences());
        Assert.Equal("On Port 3 the setup is over a limit (unstable); on Port 1 it is within.", PortTesting.DifferenceText(difference));
    }

    // Port 10 after Port 2, and two ports named "Port 1" on different hubs told apart.
    [Fact]
    public void PortsSortInTreeOrderAndSharedNamesGetTheirHub()
    {
        UsbNode Port(string hub, int number) => new($"{hub}|port{number}", UsbNodeKind.Port, $"Port {number}", []) { PortNumber = number, UserConnectable = true };
        IReadOnlyList<UsbNode> tree =
        [
            Controller("C1", new UsbNode("ROOT1", UsbNodeKind.Hub, "Root hub 1", [.. new[] { 1, 2, 10 }.Select(n => Port("ROOT1", n))])),
            Controller("C2", new UsbNode("ROOT2", UsbNodeKind.Hub, "Root hub 2", [Port("ROOT2", 1)])),
        ];

        Assert.Equal(["Port 1 on Root hub 1", "Port 2", "Port 10", "Port 1 on Root hub 2"], PortTesting.PortsOf(tree).Select(port => port.Name));
        var testing = new PortTesting();
        testing.Update(tree, new Dictionary<string, ConnectionStatus>());
        Assert.Equal(["Port 1 on Root hub 1", "Port 2", "Port 10", "Port 1 on Root hub 2"], testing.Summary().NotTried.Select(port => port.Name));
    }

    // The owner's Asus with the USB4 dock (#354): one USB-C connector, its USB 3 half on the Type-C controller (TXHC,
    // SS01) and its USB 2 half on the PCH controller (XHCI, HS02). The dock hubs on both have the same _PLD.
    private const string DockPld = "01000000080003000104000100000000FFFFFFFF";

    private static UsbNode RootPort(string hub, int number, UsbBus bus, UsbNode? child, bool? userConnectable = true, string? companion = null) =>
        new($"{hub}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child])
        {
            PortNumber = number,
            Bus = bus,
            UserConnectable = userConnectable,
            CompanionPortId = companion,
        };

    private static IReadOnlyList<UsbNode> Asus(bool dock)
    {
        var usb3 = dock ? Ids(Hub("DOCK3", Ids(Hub("GENESYS3", Ids(Device("LAN") with { Name = "USB 10/100/1000 LAN" }, 0x0BDA, 0x8153)), 0x05E3, 0x0626)), 0x1DA0, 0x901A)
            with
        { PhysicalLocation = DockPld } : null;
        var usb2 = dock ? Ids(Hub("DOCK2", Ids(Hub("GENESYS2", Ids(Device("WPP20") with { Name = "WPP20" }, 0x6993, 0xB022)), 0x05E3, 0x0610)), 0x1DA0, 0x901B)
            with
        { PhysicalLocation = DockPld } : null;
        var webcam = Ids(Device("WEBCAM"), 0x13D3, 0x56EB) with { PhysicalLocation = "01000000000000000000800300000000FFFFFFFF" };
        return
        [
            Controller("PCH", new UsbNode("XHCI", UsbNodeKind.Hub, "XHCI root hub",
                [RootPort("XHCI", 2, UsbBus.Usb2, usb2), RootPort("XHCI", 7, UsbBus.Usb2, webcam, userConnectable: false)])),
            Controller("TYPEC", new UsbNode("TXHC", UsbNodeKind.Hub, "TXHC root hub", [RootPort("TXHC", 1, UsbBus.Usb3, usb3)])),
        ];
    }

    [Fact]
    public void TheTwoHalvesOfAUsbCPortOnTwoControllersAreOnePort()
    {
        var port = Assert.Single(PortTesting.PortsOf(Asus(dock: true)));
        Assert.Equal("TXHC|port1", port.Id); // The USB 3 half names it.
        Assert.Equal(UsbBus.Usb3, port.Bus);

        var testing = new PortTesting();
        testing.Update(Asus(dock: true), Green(Asus(dock: true)));
        var tried = Assert.Single(testing.Tried);
        Assert.Equal(["USB 10/100/1000 LAN", "WPP20"], tried.Devices.Values.Select(device => device.Name).Order()); // Both halves' devices.
        Assert.Equal("Tested on 1 of 1 ports, all stable.", PortTesting.LogText(testing.Summary(), testing.Differences()));
    }

    // An empty port has no device and so no _PLD: the hub's companion pairs the halves instead.
    [Fact]
    public void EmptyCompanionPortsAreOnePort()
    {
        IReadOnlyList<UsbNode> tree = [Controller("C1", new UsbNode("ROOT", UsbNodeKind.Hub, "Root hub",
            [RootPort("ROOT", 1, UsbBus.Usb2, null, companion: "ROOT|port15"), RootPort("ROOT", 15, UsbBus.Usb3, null, companion: "ROOT|port1")]))];

        var port = Assert.Single(PortTesting.PortsOf(tree));
        Assert.Equal("ROOT|port15", port.Id);
        Assert.Equal("Port 15", port.Name);
    }

    // Without either, ports stay separate (the Mac, and the tests above).
    [Fact]
    public void PortsWithoutALocationOrACompanionStaySeparate() => Assert.Equal(2, PortTesting.PortsOf(Asus(dock: false)).Count);

    // The Asus with only the dock's USB 2 half enumerated yet (its USB 3 hub comes a moment later).
    private static IReadOnlyList<UsbNode> AsusUsb2Only()
    {
        var full = Asus(dock: true);
        return [full[0], full[1] with { Children = [full[1].Children[0] with { Children = [RootPort("TXHC", 1, UsbBus.Usb3, null)] }] }];
    }

    // The session order on the Asus (#342 review): no dock, then the dock. The empty halves looked like two ports;
    // once seen together they are one, and nothing is left under the USB 2 half's id.
    [Fact]
    public void AConnectorSeenTogetherOnceStaysOneForTheSession()
    {
        var testing = new PortTesting();
        testing.Update(Asus(dock: false), Green(Asus(dock: false)));
        Assert.Equal(2, testing.Summary().NotTried.Count);

        testing.Update(AsusUsb2Only(), Green(AsusUsb2Only())); // USB 2 first: still looks like a port of its own.
        testing.Update(Asus(dock: true), Green(Asus(dock: true)));
        testing.Update(Asus(dock: false), Green(Asus(dock: false))); // Unplugged again: still one connector.

        var tested = testing.Summary();
        Assert.Equal(["TXHC|port1"], tested.Tried.Select(result => result.Port.Id));
        Assert.Empty(tested.NotTried);
        Assert.Equal(1, tested.Total);
        Assert.Empty(testing.Differences());
        Assert.Single(PortTesting.PortsOf(Asus(dock: false)), port => port.Id == "XHCI|port2"); // Without the session: two.
    }

    private static IReadOnlyList<UsbNode> Root(params UsbNode[] ports) =>
        [Controller("C1", new UsbNode("ROOT", UsbNodeKind.Hub, "Root hub", ports))];

    private static UsbNode Located(string id, string pld) => Ids(Device(id), 0x046D, 0xC077) with { PhysicalLocation = pld };

    // Firmware that gives several ports one _PLD: two USB 2 ports are never one connector, and three ports with one
    // _PLD are not paired by it (the companion still can).
    [Fact]
    public void ASharedLocationPairsOnlyOneUsb3AndOneOtherPort()
    {
        Assert.Equal(2, PortTesting.PortsOf(Root(RootPort("ROOT", 1, UsbBus.Usb2, Located("A", "AA")), RootPort("ROOT", 2, UsbBus.Usb2, Located("B", "AA")))).Count);
        Assert.Equal(2, PortTesting.PortsOf(Root(RootPort("ROOT", 1, UsbBus.Usb2, Located("A", "AA"), companion: "ROOT|port15"),
            RootPort("ROOT", 2, UsbBus.Usb2, Located("B", "AA")), RootPort("ROOT", 15, UsbBus.Usb3, Located("C", "AA"), companion: "ROOT|port1"))).Count);
    }

    // The _PLD comes first: a companion that points elsewhere does not undo it.
    [Fact]
    public void ALocationPairComesBeforeAConflictingCompanion()
    {
        var ports = PortTesting.PortsOf(Root(
            RootPort("ROOT", 1, UsbBus.Usb2, Located("A", "AA"), companion: "ROOT|port16"),
            RootPort("ROOT", 15, UsbBus.Usb3, Located("B", "AA")),
            RootPort("ROOT", 16, UsbBus.Usb3, null, companion: "ROOT|port1")));

        Assert.Equal(["ROOT|port15", "ROOT|port16"], ports.Select(port => port.Id));
    }
}
