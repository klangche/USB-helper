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

    // A computer with three ports: Port 1 and 3 on USB 3, Port 2 on USB 2; the dock on the given port, if any.
    private static IReadOnlyList<UsbNode> Computer(int? dockOn, UsbNode? dock = null)
    {
        UsbNode? On(int port) => dockOn == port ? (dock ?? Dock($"P{port}")).OnBus(port == 2 ? UsbBus.Usb2 : UsbBus.Usb3) : null;
        var root = Hub("ROOT", On(1), On(2), On(3));
        return [Controller("C1", root with
        {
            Children = [root.Children[0] with { Bus = UsbBus.Usb3, UserConnectable = true }, root.Children[1] with { Bus = UsbBus.Usb2, UserConnectable = true },
                root.Children[2] with { Bus = UsbBus.Usb3, UserConnectable = true }],
        })];
    }

    [Fact]
    public void TheSameSetupOnAnotherPortIsAMoveNotADisconnect()
    {
        var history = new SessionHistory();
        var before = Computer(1);
        var after = Computer(2);
        history.Record(before);

        var move = PortTesting.Detect(before, after);
        history.Record(after);
        history.ApplyMove(move!);

        Assert.NotNull(move);
        Assert.Equal("ROOT|port1", move.From.Id);
        Assert.Equal("ROOT|port2", move.To.Id);
        Assert.Equal("Port 1 (USB 3)", move.From.Label);
        Assert.Equal("Port 2 (USB 2)", move.To.Label);
        Assert.Empty(move.NotBack);
        Assert.Empty(history.Missing(after));
        Assert.Empty(history.Reconnected(after));
    }

    [Fact]
    public void AMoveWhereTheCameraDidNotComeBackListsItAsMissing()
    {
        var history = new SessionHistory();
        var before = Computer(1);
        var after = Computer(2, Dock("P2", withCamera: false));
        history.Record(before);

        var move = PortTesting.Detect(before, after)!;
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
        history.Record(Computer(1));
        history.Record(Computer(2));
        history.ApplyMove(PortTesting.Detect(Computer(1), Computer(2))!);

        history.Record(Computer(1));
        history.ApplyMove(PortTesting.Detect(Computer(2), Computer(1))!);

        Assert.Empty(history.Reconnected(Computer(1)));
        Assert.Empty(history.Missing(Computer(1)));
    }

    [Fact]
    public void ADifferentSetupIsNotAMove()
    {
        var other = Ids(Hub("OTHER", Ids(Device("MOUSE"), 0x046D, 0xC077)), 0x05E3, 0x0610);

        Assert.Null(PortTesting.Detect(Computer(1), Computer(2, other)));
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

        var difference = Assert.Single(testing.Differences(), each => each.Device == "Camera");
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

        var difference = Assert.Single(testing.Differences(), each => each.Missing);
        Assert.Equal("On Port 3 the Camera did not connect; on Port 1 it did.", PortTesting.DifferenceText(difference));
    }

    [Fact]
    public void ThePortsTestedSummary()
    {
        var testing = new PortTesting();
        testing.Update(Computer(1), Green(Computer(1)));
        Assert.StartsWith("Tested on 1 of 3 ports, all stable. Port 2 not tested. Port 3 not tested.", PortTesting.LogText(testing.Summary(null), testing.Differences()), StringComparison.Ordinal);

        var onTwo = Computer(2);
        var statuses = Green(onTwo);
        statuses["CAMERAP2"] = ConnectionStatus.Orange;
        testing.Update(onTwo, statuses);
        Assert.Equal("Tested on 2 of 3 ports: Port 1 stable, Port 2 potentially unstable (Camera on USB 2). Port 3 not tested.",
            PortTesting.LogText(testing.Summary(null), testing.Differences()));

        testing.Reset();
        testing.Update(Computer(1), Green(Computer(1)));
        testing.Update(Computer(3), Green(Computer(3)));
        testing.Update(Computer(2), Green(Computer(2)));
        Assert.Equal("Tested on 3 of 3 ports, all stable.", PortTesting.LogText(testing.Summary(null), []));
    }

    // The computer file's ports give the total: the MacBook Neo has two.
    [Fact]
    public void TheTotalComesFromTheComputerFile()
    {
        var neo = HardwareDatabase.Load(Database.RealDatabaseTests.DatabaseFolder()).Computers[ComputerProfileId.From("Apple Inc.", "Mac17,5")];
        var testing = new PortTesting();
        testing.Update(Computer(1), Green(Computer(1)));

        Assert.Equal(2, testing.Summary(neo).Total);
    }
}
