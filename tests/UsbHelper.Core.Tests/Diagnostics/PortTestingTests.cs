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

    // Plugged back into the same port: a reconnect, and nothing is left to match later.
    [Fact]
    public void BackOnTheSamePortIsNoMoveAndIsForgotten()
    {
        var testing = new PortTesting();
        testing.Note(Computer(1), Empty);
        Assert.Null(testing.Note(Empty, Computer(1)));
        testing.Note(Computer(1), Empty);
        testing.Note(Empty, Computer(1));

        Assert.Null(testing.Note(Empty, Computer(2))); // Port 1 is not empty any more: nothing moved from it.
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

    // From a poor port (the camera missing) to a good one: the finding says it works on the good port.
    [Fact]
    public void FromAPoorPortToAGoodOneSaysWhereItWorks()
    {
        var testing = new PortTesting();
        var poor = Computer(3, Dock("P3", withCamera: false));
        testing.Update(poor, Green(poor));
        testing.Update(Computer(1), Green(Computer(1)));

        var difference = Assert.Single(testing.Differences(), each => each.Kind == PortDifferenceKind.Missing);
        Assert.Equal("On Port 3 the Camera did not connect; on Port 1 it did.", PortTesting.DifferenceText(difference));
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
}
