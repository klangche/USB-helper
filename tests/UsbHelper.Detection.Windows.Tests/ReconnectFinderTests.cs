using UsbHelper.Core.Detection;

namespace UsbHelper.Detection.Windows.Tests;

/// <summary>Drops shorter than the watcher's settle time: removed and added again within one batch of events.</summary>
public class ReconnectFinderTests
{
    private const string Keyboard = @"USB\VID_046D&PID_C31C\6&5E6F&0&1";
    private const string Mouse = @"USB\VID_046D&PID_C52B\5&7A8B&0&1";

    // Controller > root hub (port 1: mouse, port 2: external hub (port 1: keyboard)).
    private static readonly IReadOnlyList<UsbNode> Tree =
    [
        Node("C1", UsbNodeKind.Controller, Hub("ROOT", Node(Mouse, UsbNodeKind.Device), Hub("HUB", Node(Keyboard, UsbNodeKind.Device)))),
    ];

    private static UsbNode Node(string id, UsbNodeKind kind, params UsbNode[] children) => new(id, kind, id, children);

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) =>
            new UsbNode($"{id}|port{index + 1}", UsbNodeKind.Port, $"Port {index + 1}", child == null ? [] : [child])).ToList());

    private static DeviceEvent Arrived(string id, string? parent) => new(id, false, parent);

    private static DeviceEvent Removed(string id) => new(id, true, null);

    [Fact]
    public void DeviceRemovedAndAddedInOneBatchIsOneReconnect()
    {
        var events = new[]
        {
            Removed(Keyboard + "&MI_00"), Removed(Keyboard), Arrived(Keyboard, "HUB"), Arrived(Keyboard + "&MI_00", Keyboard),
            Arrived(Keyboard, "HUB"),
        };

        var change = Assert.Single(ReconnectFinder.Find(Tree, Tree, events));

        Assert.Equal(UsbChangeKind.Reconnected, change.Kind);
        Assert.Equal("HUB", change.ParentId);
        Assert.Equal("HUB|port1", change.Node.Id);
        Assert.Equal(Keyboard, Assert.Single(change.Node.Children).Id);
    }

    [Fact]
    public void DeviceThatStaysGoneIsNoReconnect()
    {
        IReadOnlyList<UsbNode> unplugged = [Node("C1", UsbNodeKind.Controller, Hub("ROOT", null, Hub("HUB", Node(Keyboard, UsbNodeKind.Device))))];

        Assert.Empty(ReconnectFinder.Find(Tree, unplugged, [Removed(Mouse)]));
    }

    [Fact]
    public void DeviceWithOnlyANewProblemIsNoReconnect()
    {
        Assert.Empty(ReconnectFinder.Find(Tree, Tree, [Arrived(Mouse, "ROOT")]));
    }

    [Fact]
    public void DeviceNotConnectedBeforeTheBatchIsNoReconnect()
    {
        // Some devices start again under the same id while they are first set up.
        IReadOnlyList<UsbNode> before = [Node("C1", UsbNodeKind.Controller, Hub("ROOT", null, Hub("HUB", Node(Keyboard, UsbNodeKind.Device))))];

        Assert.Empty(ReconnectFinder.Find(before, Tree, [Arrived(Mouse, "ROOT"), Removed(Mouse), Arrived(Mouse, "ROOT")]));
    }
}
