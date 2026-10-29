using System.ComponentModel;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests;

public class MainViewModelTests
{
    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    // Controller > root hub (port 1: hub with keyboard and an empty port, port 2: mouse).
    private static readonly IReadOnlyList<UsbNode> Desk =
        [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("HUB", Device("KEYBOARD"), null), Device("MOUSE"))])];

    private static MainViewModel NewViewModel() => new(null, [], new ComputerIdentity(null, null));

    private static NodeViewModel Item(MainViewModel viewModel, string id) => Find(viewModel.Roots, id) ?? throw new KeyNotFoundException(id);

    private static NodeViewModel? Find(IEnumerable<NodeViewModel> items, string id) =>
        items.Select(item => item.Id == id ? item : Find(item.Children, id)).FirstOrDefault(found => found != null);

    private static IEnumerable<NodeViewModel> All(IEnumerable<NodeViewModel> items) => items.SelectMany(item => All(item.Children).Prepend(item));

    [Theory]
    [InlineData(ConnectionStatus.Green, "#2E7D32")]
    [InlineData(ConnectionStatus.Grey, "#9E9E9E")]
    [InlineData(ConnectionStatus.Yellow, "#F9A825")]
    [InlineData(ConnectionStatus.Purple, "#8E24AA")]
    [InlineData(ConnectionStatus.Red, "#C62828")]
    public void EachStatusHasItsOwnColour(ConnectionStatus status, string colour)
    {
        Assert.Equal(colour, StatusColors.Hex(status));
        Assert.False(string.IsNullOrWhiteSpace(StatusColors.Text(status)));
    }

    [Fact]
    public void TreeShowsControllersHubsPortsAndDevices()
    {
        var viewModel = NewViewModel();

        viewModel.Load(Desk);

        Assert.Equal(["C1", "ROOT", "ROOT|port1", "HUB", "HUB|port1", "KEYBOARD", "HUB|port2", "ROOT|port2", "MOUSE"],
            All(viewModel.Roots).Select(item => item.Id));
        Assert.Null(Item(viewModel, "ROOT").Status); // Root hubs and ports have no line of their own.
        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "KEYBOARD").Status); // No profile: limits unknown.
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Grey), Item(viewModel, "KEYBOARD").LineColor);
    }

    [Fact]
    public void OneChangeTouchesOnlyItsBranchAndKeepsViewState()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);
        var before = All(viewModel.Roots).ToDictionary(item => item.Id);
        Item(viewModel, "HUB").IsExpanded = false;
        viewModel.SelectedNode = Item(viewModel, "MOUSE");
        var notified = new HashSet<string>();
        foreach (var item in before.Values)
        {
            ((INotifyPropertyChanged)item).PropertyChanged += (_, _) => notified.Add(item.Id);
            item.Children.CollectionChanged += (_, _) => notified.Add(item.Id);
        }

        viewModel.Apply([new UsbChange(UsbChangeKind.Added, "HUB", Port("HUB", 2, Device("STICK")))]);

        Assert.Equal(["HUB|port2"], notified);
        Assert.All(before, entry => Assert.Same(entry.Value, Item(viewModel, entry.Key)));
        Assert.False(Item(viewModel, "HUB").IsExpanded);
        Assert.Same(before["MOUSE"], viewModel.SelectedNode);
        Assert.Equal("STICK", Item(viewModel, "STICK").Text);
    }

    [Fact]
    public void UnpluggedDeviceStaysRedAtItsPortUntilCleared()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        var mouse = Item(viewModel, "MOUSE");
        Assert.True(mouse.IsDisconnected);
        Assert.Equal(ConnectionStatus.Red, mouse.Status);
        Assert.Equal("MOUSE (disconnected)", mouse.Text);
        var disconnected = Assert.Single(viewModel.Disconnected);

        disconnected.Clear.Execute(null);

        Assert.Null(Find(viewModel.Roots, "MOUSE"));
        Assert.Empty(viewModel.Disconnected);
    }

    [Fact]
    public void DeviceThatReturnsIsNoLongerDisconnected()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        viewModel.Apply([new UsbChange(UsbChangeKind.Added, "ROOT", Port("ROOT", 2, Device("MOUSE")))]);

        Assert.False(Item(viewModel, "MOUSE").IsDisconnected);
        Assert.Empty(viewModel.Disconnected);
    }

    [Fact]
    public void ProblemIsShownInTheItemText()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        viewModel.Apply([new UsbChange(UsbChangeKind.Changed, "ROOT", Port("ROOT", 2, Device("MOUSE") with { Problem = "code 43" }))]);

        Assert.Equal("MOUSE: code 43", Item(viewModel, "MOUSE").Text);
        Assert.Equal(ConnectionStatus.Purple, Item(viewModel, "MOUSE").Status);
    }

    [Fact]
    public void ExactlyMatchingProfileIsSelectedAndChangingItReevaluates()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"));
        viewModel.Load(Desk);

        Assert.Equal("test.c", viewModel.SelectedProfile.Profile!.Id);
        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "KEYBOARD").Status);

        viewModel.SelectedProfile = viewModel.Profiles[0];

        Assert.Null(viewModel.SelectedProfile.Profile);
        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "KEYBOARD").Status);
    }

    [Fact]
    public void UnknownComputerGetsNoProfile()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified");

        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 9999"));

        Assert.Null(viewModel.SelectedProfile.Profile);
        Assert.Equal(2, viewModel.Profiles.Count);
    }

    [Fact]
    public void DatabaseErrorsAreListedWithFileAndLine()
    {
        var viewModel = new MainViewModel(null, [new DatabaseError("computers/c.yaml", 4, "Unknown field 'x'.")], new ComputerIdentity(null, null));

        Assert.True(viewModel.HasDatabaseErrors);
        Assert.Equal(["computers/c.yaml:4: Unknown field 'x'."], viewModel.DatabaseErrors);
        Assert.Single(viewModel.Profiles);
    }
}
