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
    [InlineData(ConnectionStatus.Orange, "#EF6C00")]
    [InlineData(ConnectionStatus.Purple, "#8E24AA")]
    [InlineData(ConnectionStatus.Red, "#C62828")]
    public void EachStatusHasItsOwnColour(ConnectionStatus status, string colour)
    {
        Assert.Equal(colour, StatusColors.Hex(status));
        Assert.False(string.IsNullOrWhiteSpace(StatusColors.Text(status)));
    }

    [Fact]
    public void ShowsLoadingUntilTheTreeArrives()
    {
        var viewModel = NewViewModel();

        Assert.True(viewModel.IsLoading);
        Assert.Empty(viewModel.Roots);

        viewModel.Load(Desk);

        Assert.False(viewModel.IsLoading);
        Assert.NotEmpty(viewModel.Roots);
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
    public void DeviceThatReturnsIsPurpleAndListedUntilCleared()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        viewModel.Apply([new UsbChange(UsbChangeKind.Added, "ROOT", Port("ROOT", 2, Device("MOUSE")))]);

        var mouse = Item(viewModel, "MOUSE");
        Assert.False(mouse.IsDisconnected);
        Assert.Equal(ConnectionStatus.Purple, mouse.Status);
        var reconnected = Assert.Single(viewModel.Disconnected);
        Assert.Equal("MOUSE, reconnected on Port 2 of ROOT", reconnected.Text);
        Assert.Contains(viewModel.Findings, finding => finding.Message == "Disconnected and reconnected during this session.");

        reconnected.Clear.Execute(null);

        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "MOUSE").Status); // No profile checked.
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

    private static readonly string Latitude = $"Dell Inc. Latitude 7450 ({ComputerProfileId.From("Dell Inc.", "Latitude 7450")})";

    // Platform "p" allows no hub (max_hops: 0), so the keyboard behind HUB is above its maximum; computer "c" allows it.
    private static MainViewModel TwoProfiles()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified", platformLimits: "max_hops: 0");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"));
        viewModel.Load(Desk);
        return viewModel;
    }

    [Fact]
    public void NothingIsCheckedAtStartEvenForAnExactlyMatchingComputer()
    {
        var viewModel = TwoProfiles();

        Assert.Equal(["p"], viewModel.OperatingSystems.Select(option => option.Profile.Id));
        Assert.Equal([ComputerProfileId.From("Dell Inc.", "Latitude 7450")], viewModel.Computers.Select(option => option.Profile.Id));
        Assert.All(viewModel.OperatingSystems.Concat(viewModel.Computers), option => Assert.False(option.IsChecked));
        Assert.Empty(viewModel.StatusRows);
        Assert.True(viewModel.NothingChecked);
        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "KEYBOARD").Status);
    }

    [Fact]
    public void ProfilesAreLabelledByIdOrManufacturerAndModel()
    {
        var viewModel = TwoProfiles();

        Assert.Equal("p", viewModel.OperatingSystems.Single().Label);
        Assert.Equal(Latitude, viewModel.Computers.Single().Label);
    }

    [Fact]
    public void CheckingTwoProfilesColoursLinesByTheWorstAndListsBoth()
    {
        var viewModel = TwoProfiles();

        viewModel.Computers.Single().IsChecked = true;

        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "KEYBOARD").Status);
        var computer = Assert.Single(viewModel.StatusRows);
        Assert.Equal((Latitude, "stable", StatusColors.Hex(ConnectionStatus.Green)), (computer.Label, computer.Word, computer.Color));
        Assert.False(viewModel.NothingChecked);

        viewModel.OperatingSystems.Single().IsChecked = true;

        Assert.Equal(ConnectionStatus.Purple, Item(viewModel, "KEYBOARD").Status);
        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "MOUSE").Status); // In the root hub: no hop.
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Purple), viewModel.OverallColor);
        Assert.Equal(
            [("p", "unstable", StatusColors.Hex(ConnectionStatus.Purple)), (Latitude, "stable", StatusColors.Hex(ConnectionStatus.Green))],
            viewModel.StatusRows.Select(row => (row.Label, row.Word, row.Color)));
        Assert.Contains("Most hops: 1, limit 0 (exceeded, verified)", viewModel.StatusRows[0].Details);
        Assert.Contains("Most hops: 1, limit 5 (within limit, verified)", viewModel.StatusRows[1].Details);
    }

    [Fact]
    public void UncheckingUpdatesLinesAndRows()
    {
        var viewModel = TwoProfiles();
        viewModel.OperatingSystems.Single().IsChecked = true;
        viewModel.Computers.Single().IsChecked = true;

        viewModel.OperatingSystems.Single().IsChecked = false;

        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "KEYBOARD").Status);
        Assert.Equal([Latitude], viewModel.StatusRows.Select(row => row.Label));

        viewModel.Computers.Single().IsChecked = false;

        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "KEYBOARD").Status);
        Assert.Empty(viewModel.StatusRows);
        Assert.True(viewModel.NothingChecked);
    }

    [Fact]
    public void FindingsOfOneProfileNameItAndSharedFindingsAreListedOnce()
    {
        var viewModel = TwoProfiles();
        viewModel.Apply([new UsbChange(UsbChangeKind.Changed, "ROOT", Port("ROOT", 2, Device("MOUSE") with { Problem = "code 43" }))]);
        viewModel.OperatingSystems.Single().IsChecked = true;
        viewModel.Computers.Single().IsChecked = true;

        Assert.Single(viewModel.Findings, finding => finding.Message == "code 43" && finding.Header == "Detected · MOUSE");
        Assert.Contains(viewModel.Findings, finding => finding.Header == "p · Risk · KEYBOARD");
    }

    [Theory]
    [InlineData(ConnectionStatus.Green, "stable")]
    [InlineData(ConnectionStatus.Orange, "potentially unstable")]
    [InlineData(ConnectionStatus.Purple, "unstable")]
    [InlineData(ConnectionStatus.Grey, "unknown")]
    [InlineData(ConnectionStatus.Red, "disconnected")]
    public void EachResultHasItsWord(ConnectionStatus status, string word) => Assert.Equal(word, StatusColors.Word(status));

    [Fact]
    public void DisplayCountIsReadOnEveryRefreshAndShown()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified");
        var displays = 1;
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), () => displays);
        viewModel.Load(Desk);
        viewModel.Computers.Single().IsChecked = true;

        Assert.Contains("External displays: 1, limit 2 (within limit, verified)", viewModel.StatusRows.Single().Details);

        displays = 3;
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        Assert.Contains("External displays: 3, limit 2 (exceeded, verified)", viewModel.StatusRows.Single().Details);
    }

    [Fact]
    public void UnknownDisplayCountIsShownAsUnknown()
    {
        var viewModel = TwoProfiles();
        viewModel.OperatingSystems.Single().IsChecked = true;

        Assert.Contains("External displays: unknown, limit unknown (unknown, not verified)", viewModel.StatusRows.Single().Details);
    }

    [Fact]
    public void ExportNeedsManufacturerAndModel()
    {
        Assert.False(NewViewModel().CanExport);

        var viewModel = new MainViewModel(null, [], new ComputerIdentity("Dell Inc.", "Latitude 7450", "CPU", "windows-intel"));
        Assert.False(viewModel.CanExport); // The USB tree is still being read.
        viewModel.Load(Desk);

        Assert.True(viewModel.CanExport);
        Assert.Equal("Dell-Inc.-Latitude-7450.yaml", viewModel.ExportFileName);
        Assert.Contains("manufacturer: \"Dell Inc.\"", viewModel.ExportProfile());
        Assert.Contains("Detected: USB controllers: 1, external hubs: 1", viewModel.ExportProfile());
    }

    [Fact]
    public void DatabaseErrorsAreListedWithFileAndLine()
    {
        var viewModel = new MainViewModel(null, [new DatabaseError("computers/c.yaml", 4, "Unknown field 'x'.")], new ComputerIdentity(null, null));

        Assert.True(viewModel.HasDatabaseErrors);
        Assert.Equal(["computers/c.yaml:4: Unknown field 'x'."], viewModel.DatabaseErrors);
        Assert.Empty(viewModel.OperatingSystems);
        Assert.Empty(viewModel.Computers);
    }
}
