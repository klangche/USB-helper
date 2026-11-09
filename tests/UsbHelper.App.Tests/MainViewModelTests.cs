using System.ComponentModel;
using System.Globalization;
using UsbHelper.App.Resources;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests;

public class MainViewModelTests
{
    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static IReadOnlyList<DisplayMonitor> External(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new DisplayMonitor($"M{i}", $"Monitor {i}", MonitorOutput.Hdmi))];

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

    // #86: the window opens before anything is read; each stage is shown before its data arrives.
    [Fact]
    public void ShowsEachLoadingStageBeforeItsData()
    {
        var log = new EventLog();
        var displaysRead = 0;
        var viewModel = new MainViewModel(new ComputerIdentity("Dell Inc.", "Latitude 7450", MachineName: "DESK-1"), () =>
        {
            displaysRead++;
            return External(2);
        }, log);

        Assert.True(viewModel.IsLoading);
        Assert.Equal(Strings.LoadingDatabase, viewModel.LoadingStage);
        Assert.Empty(viewModel.OperatingSystems);
        Assert.Empty(viewModel.Computers);
        Assert.Equal("Dell Inc. Latitude 7450", viewModel.HostName);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        viewModel.LoadDatabase(DatabaseFolder.Load("Dell Inc.", "Latitude 7450", "verified", name: "Dell Latitude 14 7450"), []);

        Assert.Equal(Strings.ReadingDevices, viewModel.LoadingStage);
        Assert.Single(viewModel.OperatingSystems);
        Assert.Single(viewModel.Computers);
        Assert.Equal("Dell Latitude 14 7450", viewModel.HostName);
        Assert.Contains(nameof(MainViewModel.HostName), changed);
        Assert.Empty(viewModel.Roots);

        viewModel.ReadingDisplays();

        Assert.Equal(Strings.ReadingDisplays, viewModel.LoadingStage);
        Assert.Empty(viewModel.Roots);
        Assert.Equal(0, displaysRead);

        viewModel.Load(Desk, monitors: External(2));

        Assert.False(viewModel.IsLoading);
        Assert.NotEmpty(viewModel.Roots);
        Assert.Equal(0, displaysRead); // The first refresh uses the count read off the UI thread.
        Assert.Contains("Database loaded: 1 platform profiles, 1 computer profiles", log.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void DatabaseErrorsArriveWithTheirStage()
    {
        var viewModel = new MainViewModel(new ComputerIdentity(null, null));

        Assert.False(viewModel.HasDatabaseErrors);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        viewModel.LoadDatabase(null, [new DatabaseError("base.yaml", 2, "bad")]);

        Assert.True(viewModel.HasDatabaseErrors);
        Assert.Equal(["base.yaml:2: bad"], viewModel.DatabaseErrors);
        Assert.Contains(nameof(MainViewModel.HasDatabaseErrors), changed);
        Assert.Equal(Strings.ReadingDevices, viewModel.LoadingStage);
    }

    // The tree as text: the guide and elbow columns the view draws, then the id.
    private static IEnumerable<string> Drawn(MainViewModel viewModel) =>
        All(viewModel.Roots).Select(item => (item.HasLine
            ? string.Concat(item.Guides.Select(trunk => trunk != null ? "| " : "  ")) + (item.IsLast ? "`-" : "|-")
            : "") + item.Id);

    [Fact]
    public void TreeShowsControllersHubsOccupiedPortsAndDevices()
    {
        var viewModel = NewViewModel();

        viewModel.Load(Desk);

        Assert.Equal(["host", "C1", "ROOT", "ROOT|port1", "HUB", "HUB|port1", "KEYBOARD", "ROOT|port2", "MOUSE"],
            All(viewModel.Roots).Select(item => item.Id)); // HUB|port2 is empty.
        Assert.Null(Item(viewModel, "ROOT").Status); // Root hubs and ports have no line of their own.
        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "KEYBOARD").Status); // No profile: limits unknown.
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Grey), Item(viewModel, "KEYBOARD").LineColor);
    }

    [Fact]
    public void TreeLinesShowTrunksAndLastChildren()
    {
        var viewModel = NewViewModel();

        viewModel.Load(Desk);

        Assert.Equal(
            [
                "host",
                "`-C1",
                "  `-ROOT",
                "    |-ROOT|port1",
                "    | `-HUB",
                "    |   `-HUB|port1",
                "    |     `-KEYBOARD",
                "    `-ROOT|port2",
                "      `-MOUSE",
            ],
            Drawn(viewModel));
    }

    // Each item's line colours, as status letters (G green, U grey, P purple, R red; "-" for a line with nothing below
    // it that has a status): guides, the parent's trunk above and below the elbow, the elbow, then the item's own trunk.
    private static string Painted(NodeViewModel item)
    {
        static string Letter(string? hex) => hex switch
        {
            null => " ",
            StatusColors.Trunk => "-",
            _ when hex == StatusColors.Hex(ConnectionStatus.Green) => "G",
            _ when hex == StatusColors.Hex(ConnectionStatus.Grey) => "U",
            _ when hex == StatusColors.Hex(ConnectionStatus.Purple) => "P",
            _ when hex == StatusColors.Hex(ConnectionStatus.Red) => "R",
            _ => "?",
        };
        var line = item.HasLine ? $"{Letter(item.TrunkAboveColor)}{Letter(item.TrunkBelowColor)}{Letter(item.ElbowColor)}" : "";
        return $"{string.Concat(item.Guides.Select(Letter))}|{line}|{(item.ShowsChildren ? Letter(item.TrunkColor) : " ")} {item.Id}";
    }

    [Fact]
    public void AllGreenTreeIsGreenUpToTheHost()
    {
        var viewModel = TwoProfiles();
        viewModel.Computers.Single().IsChecked = true;

        Assert.Equal(
            [
                "||G host",
                "|G G|G C1",
                " |G G|G ROOT",
                "  |GGG|G ROOT|port1",
                "  G|G G|G HUB",
                "  G |G G|G HUB|port1",
                "  G  |G G|  KEYBOARD",
                "  |G G|G ROOT|port2",
                "   |G G|  MOUSE",
            ],
            All(viewModel.Roots).Select(Painted));
    }

    [Fact]
    public void UnstableDeviceColoursItsPathToTheHostAndLeavesItsSiblingsGreen()
    {
        var viewModel = TwoProfiles();
        viewModel.Computers.Single().IsChecked = true;

        Item(viewModel, "HUB").ExtraHops = 5; // KEYBOARD: 6 hops, purple; HUB itself: 5 hops, green.

        Assert.Equal(
            [
                "||P host",
                "|P P|P C1",
                " |P P|P ROOT",
                "  |PGP|P ROOT|port1", // The trunk below goes on to the green mouse.
                "  G|P G|P HUB", // The elbow keeps the hub's own status.
                "  G |P P|P HUB|port1",
                "  G  |P P|  KEYBOARD",
                "  |G G|G ROOT|port2",
                "   |G G|  MOUSE",
            ],
            All(viewModel.Roots).Select(Painted));
    }

    [Fact]
    public void DisconnectedDeviceColoursItsPathRed()
    {
        var viewModel = TwoProfiles();
        viewModel.Computers.Single().IsChecked = true;

        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "HUB", Port("HUB", 1, null))]);

        Assert.Equal(
            [
                "||R host",
                "|R R|R C1",
                " |R R|R ROOT",
                "  |RGR|R ROOT|port1",
                "  G|R G|R HUB",
                "  G |R R|R HUB|port1",
                "  G  |R R|  KEYBOARD",
                "  |G G|G ROOT|port2",
                "   |G G|  MOUSE",
            ],
            All(viewModel.Roots).Select(Painted));
    }

    [Fact]
    public void LinesWithNoStatusBelowThemKeepTheTrunkColour()
    {
        var viewModel = NewViewModel();

        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("HUB", (UsbNode?)null))])]);

        Assert.Equal(StatusColors.Hex(ConnectionStatus.Grey), Item(viewModel, "HUB").ElbowColor); // No profile: unknown.
        Assert.Equal(StatusColors.Trunk, Item(viewModel, "HUB").TrunkColor); // HUB shows no children.
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Grey), Item(viewModel, "ROOT|port1").ElbowColor);
    }

    [Fact]
    public void HostIsTheOnlyRootAndEveryControllerIsItsChild()
    {
        var viewModel = NewViewModel();

        viewModel.Load([.. Desk, new UsbNode("C2", UsbNodeKind.Controller, "Controller 2", [Hub("ROOT2", Device("STICK"))])]);

        var host = Assert.Single(viewModel.Roots);
        Assert.Equal(NodeViewModel.HostId, host.Id);
        Assert.False(host.HasLine);
        Assert.Null(host.Status);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 1, 3, 1), host.Counts); // #210.
        Assert.Equal(["C1", "C2"], host.Children.Select(item => item.Id));
        Assert.All(host.Children, controller => Assert.True(controller.HasLine));
        // The lines start at the host: its trunk runs down to the last controller.
        Assert.Equal(["host", "|-C1", "| `-ROOT", "|   |-ROOT|port1"], Drawn(viewModel).Take(4));
        Assert.Equal(["`-C2", "  `-ROOT2", "    `-ROOT2|port1", "      `-STICK"], Drawn(viewModel).Skip(9));
    }

    [Fact]
    public void HostIsNamedAfterTheMatchingComputerProfile()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", "verified", name: "Dell Latitude 14 7450");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450", MachineName: "DESK-1"));

        viewModel.Load(Desk);

        Assert.Equal("Dell Latitude 14 7450", Assert.Single(viewModel.Roots).Text);
    }

    [Theory]
    [InlineData("Dell Inc.", "Latitude 7450", "DESK-1", "Dell Inc. Latitude 7450")] // No profile matches.
    [InlineData(null, null, "DESK-1", "DESK-1")]
    [InlineData(" ", "", "DESK-1", "DESK-1")]
    public void HostNameFallsBackToManufacturerAndModelThenTheMachineName(string? manufacturer, string? model, string machine, string expected)
    {
        var database = DatabaseFolder.Load("Apple Inc.", "Mac17,5", "verified", name: "MacBook Neo");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity(manufacturer, model, MachineName: machine));

        viewModel.Load(Desk);

        Assert.Equal(expected, Assert.Single(viewModel.Roots).Text);
    }

    [Fact]
    public void HostOfAMatchingProfileWithoutANameIsManufacturerAndModel()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", "verified");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450", MachineName: "DESK-1"));

        viewModel.Load(Desk);

        Assert.Equal("Dell Inc. Latitude 7450", Assert.Single(viewModel.Roots).Text);
    }

    [Fact]
    public void HostWithoutAnyNameIsThisComputer()
    {
        var viewModel = NewViewModel();

        viewModel.Load(Desk);

        Assert.Equal(Strings.ThisComputer, Assert.Single(viewModel.Roots).Text);
    }

    [Fact]
    public void HostItemSurvivesUpdates()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);
        var host = viewModel.Roots.Single();
        var controller = Item(viewModel, "C1");

        viewModel.Apply([new UsbChange(UsbChangeKind.Added, "HUB", Port("HUB", 2, Device("STICK")))]);

        Assert.Same(host, viewModel.Roots.Single());
        Assert.Same(controller, Item(viewModel, "C1"));
    }

    [Fact]
    public void TreeLinesFollowANewSibling()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        viewModel.Apply([new UsbChange(UsbChangeKind.Added, "HUB", Port("HUB", 2, Device("STICK")))]);

        Assert.Equal(
            [
                "host",
                "`-C1",
                "  `-ROOT",
                "    |-ROOT|port1",
                "    | `-HUB",
                "    |   |-HUB|port1",
                "    |   | `-KEYBOARD",
                "    |   `-HUB|port2",
                "    |     `-STICK",
                "    `-ROOT|port2",
                "      `-MOUSE",
            ],
            Drawn(viewModel));
    }

    [Fact]
    public void TreeLinesFollowARemovedLastChildAndADisconnectedDevice()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        // MOUSE is unplugged and stays at its port; then the dock hub's keyboard goes away and is cleared.
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "HUB", Port("HUB", 1, null))]);
        viewModel.Disconnected.First(item => item.Text.StartsWith("KEYBOARD", StringComparison.Ordinal)).Clear.Execute(null);

        Assert.Equal(
            [
                "host",
                "`-C1",
                "  `-ROOT",
                "    |-ROOT|port1",
                "    | `-HUB",
                "    `-ROOT|port2",
                "      `-MOUSE",
            ],
            Drawn(viewModel));
        Assert.True(Item(viewModel, "MOUSE").IsDisconnected);

        viewModel.Disconnected.Single().Clear.Execute(null);

        Assert.Equal(["host", "`-C1", "  `-ROOT", "    `-ROOT|port1", "      `-HUB"], Drawn(viewModel));
    }

    [Fact]
    public void PortWithAProblemOrADisconnectedDeviceIsShown()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        viewModel.Apply(
            [
                new UsbChange(UsbChangeKind.Changed, "HUB", Port("HUB", 2, null) with { Problem = "Could not read the port." }),
                new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null)),
            ]);

        Assert.NotNull(Find(viewModel.Roots, "HUB|port2"));
        Assert.NotNull(Find(viewModel.Roots, "ROOT|port2"));
    }

    [Fact]
    public void DisplayLinkDeviceIsShownAsADisplayAndItsHubsSayTheyHaveOne()
    {
        var viewModel = NewViewModel();
        var adapter = Device("DL") with { VendorId = 0x17E9, ProductId = 0x6006 };

        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("DOCK", adapter, Device("KEYBOARD")), Hub("HUB", Device("MOUSE")))])]);

        Assert.True(Item(viewModel, "DL").IsDisplay);
        Assert.Equal(Strings.DisplayLinkDevice, Item(viewModel, "DL").DisplayNote);
        Assert.Equal(Strings.HubHasDisplay, Item(viewModel, "DOCK").DisplayNote);
        Assert.Equal(Strings.HubHasDisplay, Item(viewModel, "ROOT").DisplayNote);
        // Nothing else: not a hub without one, not the ports, not the other devices.
        Assert.Equal(["ROOT", "DOCK", "DL"], All(viewModel.Roots).Where(item => item.DisplayNote != null).Select(item => item.Id));
        Assert.Equal(["DL"], All(viewModel.Roots).Where(item => item.IsDisplay).Select(item => item.Id));
    }

    [Fact]
    public void UnpluggedDisplayLinkDeviceIsNoLongerShownAsADisplay()
    {
        var viewModel = NewViewModel();
        var adapter = Device("DL") with { VendorId = 0x17E9 };
        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("DOCK", adapter))])]);

        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "DOCK", Port("DOCK", 1, null))]);

        Assert.True(Item(viewModel, "DL").IsDisconnected);
        Assert.False(Item(viewModel, "DL").IsDisplay);
        Assert.DoesNotContain(All(viewModel.Roots), item => item.DisplayNote != null);
    }

    [Fact]
    public void HubsAndDevicesShowTheirResultWordInTheirColour()
    {
        var viewModel = NewViewModel();

        viewModel.Load(Desk);

        var keyboard = Item(viewModel, "KEYBOARD");
        Assert.Equal(Strings.WordUnknown, keyboard.StatusWord);
        Assert.Equal(keyboard.LineColor, keyboard.ElbowColor);
        Assert.True(keyboard.HasStatus);
        var port = Item(viewModel, "ROOT|port1");
        Assert.Null(port.StatusWord);
        Assert.Equal(keyboard.LineColor, port.ElbowColor); // A port's elbow takes the worst status below it.
        Assert.False(port.HasStatus);
    }

    [Fact]
    public void EachControllerShowsItsHubCountAndDeepestTierAndHop()
    {
        var viewModel = NewViewModel();
        IReadOnlyList<UsbNode> tree =
        [
            .. Desk,
            new UsbNode("C2", UsbNodeKind.Controller, "Controller 2", [Hub("ROOT2", Device("CAMERA"))]),
        ];

        viewModel.Load(tree);

        // C1: HUB is tier 2, hop 0; KEYBOARD behind it is tier 3, hop 1.
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 1, 3, 1), Item(viewModel, "C1").Counts);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 0, 2, 0), Item(viewModel, "C2").Counts);
        Assert.Null(Item(viewModel, "ROOT").Counts);
    }

    // Owner decision in #208: the host shows the hubs of all controllers and the deepest tier and hop of any of them.
    [Fact]
    public void TheHostShowsTotalHubsAndTheDeepestTierAndHop()
    {
        var viewModel = NewViewModel();
        // C1: 1 hub, tier 3, hop 1. C2: DOCK tier 2, INNER tier 3, D tier 4 with 2 hops; 2 hubs.
        var dock = new UsbNode("C2", UsbNodeKind.Controller, "Controller 2", [Hub("ROOT2", Hub("DOCK", Hub("INNER", Device("D"))))]);

        viewModel.Load([.. Desk, dock]);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 3, 4, 2), Item(viewModel, NodeViewModel.HostId).Counts);

        Item(viewModel, "KEYBOARD").ExtraHops = 2; // KEYBOARD: tier 5, hop 3.

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 3, 5, 3), Item(viewModel, NodeViewModel.HostId).Counts);
    }

    [Fact]
    public void TheHostCountsAreMinimumsWhenAHubOnAnyControllerCannotBeRead()
    {
        var viewModel = NewViewModel();
        var broken = new UsbNode("C2", UsbNodeKind.Controller, "Controller 2", [Hub("ROOT2", new UsbNode("BAD", UsbNodeKind.Hub, "BAD", []))]);

        viewModel.Load([.. Desk, broken]);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCountsAtLeast, 2, 3, 1), Item(viewModel, NodeViewModel.HostId).Counts);
    }

    [Fact]
    public void OnlyAPortWindowsCallsUsbCIsLabelledUsbC()
    {
        var viewModel = NewViewModel();
        var root = Hub("ROOT", Device("PHONE"), Device("MOUSE"), Device("STICK"));
        root = root with { Children = [root.Children[0] with { UsbC = true }, root.Children[1] with { UsbC = false }, root.Children[2]] };

        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [root])]);

        Assert.Equal(Strings.UsbCPort, Item(viewModel, "ROOT|port1").PortType);
        Assert.Null(Item(viewModel, "ROOT|port2").PortType);
        Assert.Null(Item(viewModel, "ROOT|port3").PortType); // Unknown shows nothing.
        Assert.Null(Item(viewModel, "PHONE").PortType);
        Assert.Null(Item(viewModel, "ROOT").PortType);
    }

    [Fact]
    public void ControllerCountsLeaveOutTheBuiltInHub()
    {
        var viewModel = NewViewModel();
        // Root port 1 is wired to a built-in hub; its port 1 is the laptop's own socket, with a mouse.
        var builtIn = Hub("INTERNAL", Device("MOUSE"));
        builtIn = builtIn with { Children = [builtIn.Children[0] with { UserConnectable = true }] };
        var root = Hub("ROOT", builtIn);
        root = root with { Children = [root.Children[0] with { UserConnectable = false }] };

        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [root])]);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 0, 2, 0), Item(viewModel, "C1").Counts);
    }

    // #195: a USB 3 hub is one hub on each bus; the line shows both buses instead of adding the halves up.
    private static UsbNode RootWithBuses(string id, UsbNode usb2Hub, UsbNode usb3Hub)
    {
        static UsbNode OnBus(UsbNode hub, UsbBus bus) => hub with
        {
            Children = hub.Children.Select(port => port with
            {
                Bus = bus,
                Children = port.Children.Select(child => child.Kind == UsbNodeKind.Hub ? OnBus(child, bus) : child).ToList(),
            }).ToList(),
        };
        var root = Hub(id, OnBus(usb2Hub, UsbBus.Usb2), OnBus(usb3Hub, UsbBus.Usb3));
        return root with { Children = [root.Children[0] with { Bus = UsbBus.Usb2 }, root.Children[1] with { Bus = UsbBus.Usb3 }] };
    }

    [Fact]
    public void ControllerAndHostCountsArePerBusWhenTheBusIsKnown()
    {
        var viewModel = NewViewModel();
        var root = RootWithBuses("ROOT", Hub("HUB-USB2", Hub("INNER", Device("KEYBOARD"))), Hub("HUB-USB3", Device("DISK")));

        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [root])]);

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.ControllerCountsPerBus, 2, 4, 2, 1, 3, 1);
        Assert.Equal(expected, Item(viewModel, "C1").Counts);
        Assert.Equal(expected, Item(viewModel, NodeViewModel.HostId).Counts);
    }

    [Fact]
    public void PerBusCountsAreMinimumsWhenAPortsBusIsUnknown()
    {
        var viewModel = NewViewModel();
        var root = RootWithBuses("ROOT", Hub("HUB-USB2", Device("KEYBOARD")), Hub("HUB-USB3", Device("DISK")));
        root = root with { Children = [.. root.Children, Port("ROOT", 3, Hub("UNKNOWN", Device("MOUSE")))] };

        viewModel.Load([new UsbNode("C1", UsbNodeKind.Controller, "Controller", [root])]);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCountsPerBusAtLeast, 1, 3, 1, 1, 3, 1), Item(viewModel, "C1").Counts);
    }

    [Fact]
    public void EachHubAndDeviceSaysItsTierAndTheHubsInFront()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        Assert.Contains(string.Format(CultureInfo.CurrentCulture, Strings.NodeDepthTip, 3, 1), Item(viewModel, "KEYBOARD").RowTip);
        Assert.Contains(string.Format(CultureInfo.CurrentCulture, Strings.NodeDepthTip, 2, 0), Item(viewModel, "HUB").RowTip);
        Assert.Null(Item(viewModel, "C1").DepthText);
        Assert.Null(Item(viewModel, "ROOT|port1").DepthText);
    }

    [Fact]
    public void ControllerCountsAreMinimumsWhenAHubCannotBeRead()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        viewModel.Apply([new UsbChange(UsbChangeKind.Changed, "ROOT", Port("ROOT", 1, new UsbNode("HUB", UsbNodeKind.Hub, "HUB", []) { Problem = "Could not read the hub." }))]);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCountsAtLeast, 1, 2, 0), Item(viewModel, "C1").Counts);
    }

    [Fact]
    public void OneChangeTouchesOnlyItsBranchAndKeepsViewState()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);
        var before = All(viewModel.Roots).ToDictionary(item => item.Id);
        viewModel.SelectedNode = Item(viewModel, "MOUSE");
        var notified = new HashSet<string>();
        foreach (var item in before.Values)
        {
            ((INotifyPropertyChanged)item).PropertyChanged += (_, _) => notified.Add(item.Id);
            item.Children.CollectionChanged += (_, _) => notified.Add(item.Id);
        }

        viewModel.Apply([new UsbChange(UsbChangeKind.Added, "HUB", Port("HUB", 2, Device("STICK")))]);

        // The new port, and the lines of the sibling branch it now continues past.
        Assert.Equal(["HUB", "HUB|port1", "KEYBOARD"], notified.Order());
        Assert.All(before, entry => Assert.Same(entry.Value, Item(viewModel, entry.Key)));
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
    public void ShortDropReportedAsReconnectIsPurpleAndListedUntilCleared()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);

        // Removed and added again within one batch: the tree is unchanged.
        viewModel.Apply([new UsbChange(UsbChangeKind.Reconnected, "ROOT", Port("ROOT", 2, Device("MOUSE")))]);

        Assert.Equal(ConnectionStatus.Purple, Item(viewModel, "MOUSE").Status);
        var reconnected = Assert.Single(viewModel.Disconnected);
        Assert.Equal("MOUSE, reconnected on Port 2 of ROOT", reconnected.Text);

        reconnected.Clear.Execute(null);

        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "MOUSE").Status);
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

    [Fact]
    public void MarkingAConnectionAddsHopsBehindItAndReEvaluates()
    {
        var viewModel = TwoProfiles();
        viewModel.Computers.Single().IsChecked = true; // max_hops 5; the keyboard has 1 hop.
        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "KEYBOARD").Status);
        var hub = Item(viewModel, "HUB"); // The line from ROOT|port1 to HUB.
        Assert.True(hub.CanMark);
        Assert.False(hub.IsUserMarked);
        Assert.Null(hub.MarkBadge);

        hub.ExtraHops = 5;

        Assert.Equal(ConnectionStatus.Purple, Item(viewModel, "KEYBOARD").Status); // 6 hops.
        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "MOUSE").Status);
        Assert.Equal(5, Item(viewModel, "HUB").ExtraHops);
        Assert.True(Item(viewModel, "HUB").IsUserMarked);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.CableMark, 5), Item(viewModel, "HUB").MarkBadge);
        Assert.False(Item(viewModel, "MOUSE").IsUserMarked);
        Assert.Equal("Port 1", Item(viewModel, "ROOT|port1").Text); // The mark is on the line, not on the port.
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 1, 8, 6), Item(viewModel, "C1").Counts);

        viewModel.Apply([]); // The mark lasts while the app runs.
        Assert.Equal(ConnectionStatus.Purple, Item(viewModel, "KEYBOARD").Status);
        Assert.True(Item(viewModel, "HUB").IsUserMarked);

        Item(viewModel, "HUB").ExtraHops = 0;
        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "KEYBOARD").Status);
        Assert.False(Item(viewModel, "HUB").IsUserMarked);
        Assert.Null(Item(viewModel, "HUB").MarkBadge);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.ControllerCounts, 1, 3, 1), Item(viewModel, "C1").Counts);
    }

    [Fact]
    public void OnlyConnectionsFromAPortCanBeMarked()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Desk);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]); // MOUSE is disconnected.

        Assert.Equal(["HUB", "KEYBOARD"], All(viewModel.Roots).Where(item => item.CanMark).Select(item => item.Id));
        var mouse = Item(viewModel, "MOUSE");
        mouse.ExtraHops = 2; // Ignored: nothing is behind a disconnected device's port now.
        Assert.Equal(0, mouse.ExtraHops);
        Assert.False(mouse.IsUserMarked);
    }

    // Platform "p" allows no hub (max_hops: 0), so the keyboard behind HUB is above its maximum; computer "c" allows it.
    private static MainViewModel TwoProfiles()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified", platformLimits: "max_hops: 0");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"));
        viewModel.Load(Desk);
        return viewModel;
    }

    [Fact]
    public void NoComputerIsCheckedAtStartEvenForAnExactMatch()
    {
        var viewModel = TwoProfiles();

        Assert.Equal(["p"], viewModel.OperatingSystems.Select(option => option.Profile.Id));
        Assert.Equal([ComputerProfileId.From("Dell Inc.", "Latitude 7450")], viewModel.Computers.Select(option => option.Profile.Id));
        Assert.All(viewModel.OperatingSystems.Concat(viewModel.Computers), option => Assert.False(option.IsChecked));
        Assert.Empty(viewModel.StatusRows);
        Assert.True(viewModel.NothingChecked);
        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "KEYBOARD").Status);
    }

    // An AMD Windows computer whose platform profile allows no hub (max_hops: 0): the keyboard behind HUB is unstable.
    private static MainViewModel OnWindowsAmd(string? detectedPlatform)
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified", platformLimits: "max_hops: 0", platform: "windows-amd");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450", Platform: detectedPlatform));
        viewModel.Load(Desk);
        return viewModel;
    }

    [Fact]
    public void TheDetectedOperatingSystemIsCheckedAtStartAndJudgesTheLines()
    {
        var viewModel = OnWindowsAmd("windows-amd");

        Assert.True(viewModel.OperatingSystems.Single().IsChecked);
        Assert.False(viewModel.Computers.Single().IsChecked); // Computer profiles are never checked automatically.
        Assert.Equal(["windows-amd"], viewModel.StatusRows.Select(row => row.Label));
        Assert.Equal(ConnectionStatus.Purple, Item(viewModel, "KEYBOARD").Status);
    }

    [Fact]
    public void UncheckingTheDetectedOperatingSystemTurnsTheLinesGrey()
    {
        var viewModel = OnWindowsAmd("windows-amd");

        viewModel.OperatingSystems.Single().IsChecked = false;

        Assert.Equal(ConnectionStatus.Grey, Item(viewModel, "KEYBOARD").Status);
        Assert.True(viewModel.NothingChecked);
    }

    [Theory]
    [InlineData("windows-intel")]
    [InlineData(null)]
    public void NothingIsCheckedAtStartWhenNoPlatformProfileMatches(string? detectedPlatform)
    {
        var viewModel = OnWindowsAmd(detectedPlatform);

        Assert.All(viewModel.OperatingSystems.Concat(viewModel.Computers), option => Assert.False(option.IsChecked));
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
    public void ComputerWithNameIsLabelledByNameThenManufacturerAndModel()
    {
        var database = DatabaseFolder.Load("Apple Inc.", "Mac17,5", status: "verified", name: "MacBook Neo");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Apple Inc.", "Mac17,5"));

        Assert.Equal("MacBook Neo (Apple Inc. Mac17,5)", viewModel.Computers.Single().Label);
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
        Assert.Contains("Most hops: 1, limit 0 (exceeded)", viewModel.StatusRows[0].Details);
        Assert.Contains("Most hops: 1, limit 5 (within limit)", viewModel.StatusRows[1].Details);
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
    public void WithSeveralProfilesCheckedOnlyTheStrictestListsFindingsAndDetectedFactsAreListedOnce()
    {
        var viewModel = TwoProfiles(); // "p" (max_hops 0) is stricter than the computer (max_hops 5).
        viewModel.Apply([new UsbChange(UsbChangeKind.Changed, "ROOT", Port("ROOT", 2, Device("MOUSE") with { Problem = "code 43" }))]);
        viewModel.OperatingSystems.Single().IsChecked = true;
        viewModel.Computers.Single().IsChecked = true;

        Assert.Single(viewModel.Findings, finding => finding.Message == "code 43" && finding.Header == "Detected · MOUSE");
        Assert.Contains(viewModel.Findings, finding => finding.Header == "p · Risk · KEYBOARD");
        Assert.DoesNotContain(viewModel.Findings, finding => finding.Header.StartsWith(Latitude, StringComparison.Ordinal));
        Assert.Equal(["p", Latitude], viewModel.StatusRows.Select(row => row.Label)); // The status field still lists both.
        Assert.Equal(ConnectionStatus.Purple, Item(viewModel, "KEYBOARD").Status);
    }

    [Fact]
    public void WithOneProfileCheckedItsFindingsDoNotNameIt()
    {
        var viewModel = TwoProfiles();

        viewModel.OperatingSystems.Single().IsChecked = true;

        Assert.Contains(viewModel.Findings, finding => finding.Header == "Risk · KEYBOARD");
        Assert.DoesNotContain(viewModel.Findings, finding => finding.Header.StartsWith("p ·", StringComparison.Ordinal));
    }

    // Owner decision in #85 (#88): the findings list shows only real findings; the rest stay in the log.
    private static bool IsReal(FindingItem finding) =>
        !finding.Header.StartsWith(Strings.KindUnknown, StringComparison.Ordinal)
        && !finding.Header.StartsWith(Strings.KindVerifiedDatabaseFact, StringComparison.Ordinal);

    [Fact]
    public void UnknownLimitsShowNoFindingsWhileEverythingIsWithinTheKnownLimits()
    {
        var log = new EventLog();
        // Only max_hops is known; the hub and tier limits and the display count are unknown.
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified", platformLimits: "max_hops: 5");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), log: log);
        viewModel.Load(Desk);

        viewModel.OperatingSystems.Single().IsChecked = true;

        Assert.Empty(viewModel.Findings);
        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "KEYBOARD").Status);
        Assert.Contains(log.Entries, entry => entry.Text.StartsWith("Finding (p, unknown): This computer: limits.max_hubs is unknown", StringComparison.Ordinal));
        Assert.Contains(log.Entries, entry => entry.Text == "Finding (p, unknown): This computer: The number of external displays is not known.");
        Assert.Contains(log.Entries, entry => entry.Text.StartsWith("Finding (p, database): This computer: limits.max_hops = 5", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAProfileOnlyRealFindingsAreShown()
    {
        var viewModel = NewViewModel(); // Nothing checked: every limit is unknown.

        viewModel.Load(Desk);

        Assert.Empty(viewModel.Findings);
    }

    [Fact]
    public void AnExceededLimitAnErrorAndADisconnectAreStillShown()
    {
        var log = new EventLog();
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified", platformLimits: "max_hops: 0");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), () => External(3), log);
        viewModel.Load(Desk);
        viewModel.OperatingSystems.Single().IsChecked = true;

        viewModel.Apply([
            new UsbChange(UsbChangeKind.Changed, "ROOT", Port("ROOT", 1, Hub("HUB", Device("KEYBOARD") with { Problem = "code 43" }, null))),
            new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null)),
        ]);

        Assert.Contains(viewModel.Findings, finding => finding.Header == "Risk · KEYBOARD");
        Assert.Contains(viewModel.Findings, finding => finding.Header == "Detected · KEYBOARD" && finding.Message == "code 43");
        Assert.Contains(viewModel.Findings, finding => finding.Header == "Detected · MOUSE" && finding.Message.StartsWith("Disconnected", StringComparison.Ordinal));
        Assert.All(viewModel.Findings, finding => Assert.True(IsReal(finding), finding.Header));
        // An active display count is a detected fact but not a problem.
        Assert.DoesNotContain(viewModel.Findings, finding => finding.Message.Contains("external displays are active", StringComparison.Ordinal));
        Assert.Contains(log.Entries, entry => entry.Text.StartsWith("Finding (p, unknown): This computer: limits.max_hubs is unknown", StringComparison.Ordinal));
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
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), () => External(displays));
        viewModel.Load(Desk);
        viewModel.Computers.Single().IsChecked = true;

        Assert.Contains("External displays: 1, limit 2 (within limit)", viewModel.StatusRows.Single().Details);

        displays = 3;
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        Assert.Contains("External displays: 3, limit 2 (exceeded)", viewModel.StatusRows.Single().Details);
    }

    [Fact]
    public void UnknownDisplayCountIsShownAsUnknown()
    {
        var viewModel = TwoProfiles();
        viewModel.OperatingSystems.Single().IsChecked = true;

        Assert.Contains("External displays: unknown, limit unknown (unknown)", viewModel.StatusRows.Single().Details);
    }

    // Laptop: root port 1 is fixed, with a built-in hub (webcam on a fixed port, a dock on a laptop socket); Windows
    // says nothing about root port 2, which has a hub.
    private static IReadOnlyList<UsbNode> Laptop()
    {
        var builtIn = Hub("INTERNAL", Device("WEBCAM"), Hub("DOCK", Device("KEYBOARD")));
        builtIn = builtIn with { Children = [builtIn.Children[0] with { UserConnectable = false }, builtIn.Children[1] with { UserConnectable = true }] };
        var root = Hub("ROOT", builtIn, Hub("UNSURE", Device("MOUSE")));
        root = root with { Children = [root.Children[0] with { UserConnectable = false }, root.Children[1]] };
        return [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [root])];
    }

    // Owner decision in #142: only the controller rows collapse; the host and everything below a controller, built-in
    // hubs too, are always open.
    [Fact]
    public void OnlyControllersCanCollapse()
    {
        var viewModel = NewViewModel();

        viewModel.Load(Laptop());

        Assert.True(Item(viewModel, "C1").CanExpand);
        Assert.All(All(viewModel.Roots).Where(item => item.Id != "C1"), item =>
        {
            Assert.False(item.CanExpand);
            Assert.True(item.IsExpanded);
            Assert.False(item.IsCollapsed);
        });
        Assert.True(Item(viewModel, "INTERNAL").ShowsChildren); // The built-in hub is open.
    }

    [Fact]
    public void TheHostAndEverythingBelowAControllerCannotCollapse()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Laptop());

        foreach (var id in new[] { NodeViewModel.HostId, "ROOT", "INTERNAL", "ROOT|port2", "UNSURE" })
        {
            var item = Item(viewModel, id);

            item.IsExpanded = false; // As the tree view does for a double-click or an arrow key.

            Assert.True(item.IsExpanded);
            Assert.False(item.IsCollapsed);
            Assert.True(item.ShowsChildren);
        }
    }

    // A collapsed controller hides its tree and its name is bold (#100); expanding it shows the tree as before.
    [Fact]
    public void CollapsingAControllerHidesItsTreeAndExpandingShowsItAgain()
    {
        var viewModel = NewViewModel();
        viewModel.Load(Laptop());
        var controller = Item(viewModel, "C1");

        controller.IsExpanded = false;

        Assert.False(controller.ShowsChildren);
        Assert.True(controller.IsCollapsed);

        controller.IsExpanded = true;

        Assert.True(controller.ShowsChildren);
        Assert.False(controller.IsCollapsed);
        Assert.All(All(viewModel.Roots), item => Assert.True(item.IsExpanded));
    }

    private static readonly DisplayMonitor Panel = new("PANEL", null, MonitorOutput.Internal);
    private static readonly DisplayMonitor Dell = new("DELL", "DELL U2723QE", MonitorOutput.Hdmi);
    private static readonly DisplayMonitor Lg = new("LG", "LG HDR 4K", MonitorOutput.DisplayPort);
    private static readonly DisplayMonitor OnDisplayLink = new("DL-MONITOR", "HP E24", MonitorOutput.Indirect);

    private static NodeViewModel? DisplaysGroup(MainViewModel viewModel) =>
        Item(viewModel, NodeViewModel.HostId).Children.SingleOrDefault(item => item.Id == NodeViewModel.DisplaysId);

    // Owner decision in #133: a "Displays" group under the host lists each monitor with its name and output type.
    [Fact]
    public void TheDisplaysGroupListsEachMonitorWithItsTypeUnderTheHost()
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => [Panel, Dell, Lg]);

        viewModel.Load(Desk);

        var host = Item(viewModel, NodeViewModel.HostId);
        Assert.Equal(["C1", NodeViewModel.DisplaysId], host.Children.Select(item => item.Id));
        var group = DisplaysGroup(viewModel)!;
        Assert.Equal(Strings.DisplaysGroup, group.Text);
        Assert.Equal(["Monitor (built-in)", "DELL U2723QE (HDMI)", "LG HDR 4K (DisplayPort)"], group.Children.Select(item => item.Text));
        Assert.True(group.IsLast);
        Assert.False(Item(viewModel, "C1").IsLast);
        Assert.True(group.Children[^1].IsLast);
        Assert.All(group.Children.Prepend(group), item =>
        {
            Assert.False(item.CanMark);
            Assert.False(item.CanExpand);
            Assert.Null(item.Status);
            Assert.False(item.IsDisplay); // The DisplayLink note is for DisplayLink devices only.
        });
    }

    // DisplayLink monitors stay under their USB device (#92) and are not listed twice.
    [Fact]
    public void ADisplayLinkMonitorIsNotListedTwice()
    {
        var adapter = Device("DL") with { VendorId = UsbNode.DisplayLinkVendorId };
        var tree = new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("DOCK", adapter))]);
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => [Dell, OnDisplayLink]);

        viewModel.Load([tree]);

        Assert.Equal(["DELL U2723QE (HDMI)"], DisplaysGroup(viewModel)!.Children.Select(item => item.Text));
        Assert.Equal(Strings.DisplayLinkDevice, Item(viewModel, "DL").DisplayNote);
    }

    [Fact]
    public void AnIndirectMonitorIsListedWhenNoDisplayLinkDeviceIsConnected()
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => [OnDisplayLink]);

        viewModel.Load(Desk);

        Assert.Equal([string.Format(CultureInfo.CurrentCulture, Strings.MonitorItem, "HP E24", Strings.OutputIndirect)],
            DisplaysGroup(viewModel)!.Children.Select(item => item.Text));
    }

    [Fact]
    public void PluggingAndUnpluggingAMonitorUpdatesTheGroupAndTheLog()
    {
        var log = new EventLog();
        IReadOnlyList<DisplayMonitor>? monitors = [Panel, Dell];
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => monitors, log);
        viewModel.Load(Desk);
        var panel = DisplaysGroup(viewModel)!.Children[0];

        monitors = [Panel, Dell, Lg];
        viewModel.DisplaysChanged();

        Assert.Equal(["Monitor (built-in)", "DELL U2723QE (HDMI)", "LG HDR 4K (DisplayPort)"],
            DisplaysGroup(viewModel)!.Children.Select(item => item.Text));
        Assert.Same(panel, DisplaysGroup(viewModel)!.Children[0]); // Kept, so a selection survives.

        monitors = [Panel];
        viewModel.DisplaysChanged();

        Assert.Equal(["Monitor (built-in)"], DisplaysGroup(viewModel)!.Children.Select(item => item.Text));
        Assert.True(DisplaysGroup(viewModel)!.Children.Single().IsLast);
        Assert.Contains("Display connected: LG HDR 4K (DisplayPort)", log.Entries.Select(entry => entry.Text));
        Assert.Contains("Display disconnected: DELL U2723QE (HDMI)", log.Entries.Select(entry => entry.Text));
        Assert.Contains("Display disconnected: LG HDR 4K (DisplayPort)", log.Entries.Select(entry => entry.Text));

        monitors = [];
        viewModel.DisplaysChanged();

        Assert.Null(DisplaysGroup(viewModel));
        Assert.True(Item(viewModel, "C1").IsLast);
    }

    [Fact]
    public void ADisplayChangeUpdatesTheDisplayCount()
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified");
        IReadOnlyList<DisplayMonitor> monitors = [Panel, Dell];
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), () => monitors);
        viewModel.Load(Desk);
        viewModel.Computers.Single().IsChecked = true;

        monitors = [Panel, Dell, Lg, Lg with { Id = "LG2" }];
        viewModel.DisplaysChanged();

        Assert.Contains("External displays: 3, limit 2 (exceeded)", viewModel.StatusRows.Single().Details);
    }

    // A monitor belongs to no controller: selecting it, or the group, keeps what the status covers.
    [Fact]
    public void SelectingAMonitorKeepsTheStatusScope()
    {
        var other = new UsbNode("C2", UsbNodeKind.Controller, "Second controller", [Hub("ROOT2", Device("A"))]);
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => [Panel, Dell]);
        viewModel.Load([.. Desk, other]);

        viewModel.SelectedNode = Item(viewModel, "C2");
        viewModel.SelectedNode = DisplaysGroup(viewModel)!.Children[1];

        Assert.Equal("C2", viewModel.SelectedControllerId);

        viewModel.SelectedNode = Item(viewModel, NodeViewModel.HostId);
        viewModel.SelectedNode = DisplaysGroup(viewModel);

        Assert.Null(viewModel.SelectedControllerId);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.StatusFor, Strings.AllControllers), viewModel.StatusScope);
    }

    // #143: a monitor in the same Windows container as a USB node is shown under that node, not in the Displays group.
    [Fact]
    public void ALinkedMonitorIsShownUnderItsUsbNode()
    {
        var container = new Guid("6f1d2a6e-0000-4000-8000-000000000001");
        var monitorHub = Hub("MONITOR-HUB", Device("CAM")) with { ContainerId = container };
        var tree = new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", monitorHub, Device("MOUSE"))]);
        var samsung = new DisplayMonitor("SAMSUNG", "LS27A600U", MonitorOutput.DisplayPort) { ContainerId = container };
        var log = new EventLog();
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => [Panel, samsung], log);

        viewModel.Load([tree]);

        var hub = Item(viewModel, "MONITOR-HUB");
        Assert.Equal(["MONITOR-HUB|port1", "display|SAMSUNG"], hub.Children.Select(item => item.Id));
        Assert.Equal("LS27A600U (DisplayPort)", hub.Children[1].Text);
        Assert.True(hub.Children[1].IsLast);
        Assert.False(hub.Children[0].IsLast);
        Assert.Equal(["Monitor (built-in)"], DisplaysGroup(viewModel)!.Children.Select(item => item.Text));
        Assert.Contains("Display under a USB device: LS27A600U (DisplayPort) under MONITOR-HUB (same Windows container)",
            log.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void WhenEveryMonitorIsLinkedThereIsNoDisplaysGroup()
    {
        var container = new Guid("6f1d2a6e-0000-4000-8000-000000000001");
        var tree = new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("MONITOR-HUB", (UsbNode?)null) with { ContainerId = container })]);
        IReadOnlyList<DisplayMonitor> monitors = [new DisplayMonitor("SAMSUNG", "LS27A600U", MonitorOutput.DisplayPort) { ContainerId = container }];
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => monitors);

        viewModel.Load([tree]);

        Assert.Null(DisplaysGroup(viewModel));
        Assert.True(Item(viewModel, "display|SAMSUNG").IsLast);

        monitors = [];
        viewModel.DisplaysChanged();

        Assert.Empty(Item(viewModel, "MONITOR-HUB").Children);
    }

    // Owner decision in #144: with a Mac-like profile (no MST) checked, the second DisplayPort monitor is orange with
    // the No MST message; the first stays green.
    [Fact]
    public void WithoutMstTheSecondDisplayPortMonitorIsOrange()
    {
        var database = DatabaseFolder.Load("Apple", "Mac17,5", status: "verified", name: "MacBook Neo", mst: "false");
        DisplayMonitor first = new("A", "LS27A600U", MonitorOutput.DisplayPort);
        DisplayMonitor second = new("B", "LS27A600U", MonitorOutput.DisplayPort);
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("LENOVO", "21JK"), () => [Panel, first, second]);
        viewModel.Load(Desk);

        viewModel.Computers.Single().IsChecked = true;

        Assert.Equal(ConnectionStatus.Green, Item(viewModel, "display|A").Status);
        Assert.Equal(ConnectionStatus.Orange, Item(viewModel, "display|B").Status);
        Assert.Equal("potentially unstable", Item(viewModel, "display|B").StatusWord);
        Assert.Null(Item(viewModel, "display|PANEL").Status);
        Assert.Contains(viewModel.Findings, finding =>
            finding.Message == "No MST on MacBook Neo: this display is duplicated; use a second USB-C port or DisplayLink.");
    }

    [Fact]
    public void ThereIsNoDisplaysGroupWhenTheMonitorsAreUnknown()
    {
        var viewModel = NewViewModel();

        viewModel.Load(Desk);

        Assert.Null(DisplaysGroup(viewModel));
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

    // C1 is the desk (hub, keyboard, mouse: 3 connected); C2 has a phone in its root hub (1). Platform "p" allows no hub
    // (max_hops 0), so C1 is unstable and C2 stable. "p" is the detected platform, so it is checked at start.
    private static readonly UsbNode Phone =
        new("C2", UsbNodeKind.Controller, "Second controller", [Hub("ROOT2", Device("PHONE"))]);

    private static MainViewModel TwoControllers(params UsbNode[] controllers)
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified", platformLimits: "max_hops: 0");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450", Platform: "p"));
        viewModel.Load(controllers.Length == 0 ? [.. Desk, Phone] : controllers);
        return viewModel;
    }

    private static string Word(MainViewModel viewModel) => Assert.Single(viewModel.StatusRows).Word;

    [Fact]
    public void AtStartTheControllerWithMostDevicesIsSelectedAndTheStatusFollowsIt()
    {
        var viewModel = TwoControllers(Phone, Desk[0]);

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.Equal("unstable", Word(viewModel));
        Assert.Contains(viewModel.Findings, finding => finding.Header == "Risk · KEYBOARD");
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Purple), viewModel.OverallColor);
    }

    [Fact]
    public void ClickingAnotherControllerOrADeviceUnderItSwitches()
    {
        var viewModel = TwoControllers();

        viewModel.SelectedNode = Item(viewModel, "C2");

        Assert.Equal("C2", viewModel.SelectedControllerId);
        Assert.Equal("stable", Word(viewModel));
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Green), viewModel.OverallColor); // The overall result follows it.
        Assert.DoesNotContain(viewModel.Findings, finding => finding.Header.EndsWith("KEYBOARD", StringComparison.Ordinal));
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Purple), Item(viewModel, "KEYBOARD").LineColor); // The tree is unchanged.

        viewModel.SelectedNode = Item(viewModel, "KEYBOARD");

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.Equal("unstable", Word(viewModel));

        viewModel.SelectedNode = Item(viewModel, "PHONE");

        Assert.Equal("C2", viewModel.SelectedControllerId);
    }

    [Fact]
    public void ClickingTheHostCoversAllControllers()
    {
        var viewModel = TwoControllers();
        viewModel.SelectedNode = Item(viewModel, "C2");

        viewModel.SelectedNode = Item(viewModel, NodeViewModel.HostId);

        Assert.Null(viewModel.SelectedControllerId);
        Assert.Equal("unstable", Word(viewModel));
        Assert.Contains(viewModel.Findings, finding => finding.Header == "Risk · KEYBOARD");
    }

    [Fact]
    public void WhenTheSelectedControllerIsRemovedTheOneWithMostDevicesIsSelected()
    {
        var other = new UsbNode("C3", UsbNodeKind.Controller, "Third controller", [Hub("ROOT3", Device("A"), Device("B"))]);
        var viewModel = TwoControllers([.. Desk, Phone, other]);
        viewModel.SelectedNode = Item(viewModel, "C1");

        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, null, Desk[0])]);

        Assert.Equal("C3", viewModel.SelectedControllerId);
        Assert.Equal("stable", Word(viewModel));
    }

    [Fact]
    public void OnATieTheFirstControllerInTheTreeIsSelected()
    {
        var other = new UsbNode("C3", UsbNodeKind.Controller, "Third controller", [Hub("ROOT3", Device("A"))]);

        Assert.Equal("C3", TwoControllers(other, Phone).SelectedControllerId);
    }

    [Fact]
    public void BuiltInHubsDoNotCountAsDevicesWhenSelecting()
    {
        // C4: a built-in hub (on a port that is not user-connectable) with a camera: 1 device. C5: a dock with a device: 2.
        var root = Hub("ROOT4", Hub("BUILTIN", Device("CAMERA")));
        root = root with { Children = [root.Children[0] with { UserConnectable = false }] };
        var builtIn = new UsbNode("C4", UsbNodeKind.Controller, "Built-in controller", [root]);
        var external = new UsbNode("C5", UsbNodeKind.Controller, "Fifth controller", [Hub("ROOT5", Hub("DOCK", Device("D")))]);

        Assert.Equal("C5", TwoControllers(builtIn, external).SelectedControllerId);
    }

    [Fact]
    public void TheStatusFieldNamesWhatItCovers()
    {
        var viewModel = TwoControllers();

        Assert.Equal("For: Controller", viewModel.StatusScope);

        viewModel.SelectedNode = Item(viewModel, NodeViewModel.HostId);

        Assert.Equal("For: all controllers", viewModel.StatusScope);
    }

    [Fact]
    public void TheSelectedControllersStatusUsesTheWholeComputersHubCount()
    {
        // One hub on each controller; platform "p" allows one hub, so each hub is above the computer's maximum.
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified", platformLimits: "max_hubs: 1");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450", Platform: "p"));
        var dock = new UsbNode("C6", UsbNodeKind.Controller, "Dock controller", [Hub("ROOT6", Hub("DOCK", Device("D")))]);
        viewModel.Load([.. Desk, dock]);

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.Equal("unstable", Word(viewModel));
        Assert.Contains("External hubs: 2, limit 1 (exceeded)", viewModel.StatusRows[0].Details);
        Assert.Equal(StatusColors.Hex(ConnectionStatus.Purple), Item(viewModel, "HUB").LineColor);
    }

    // Owner decision in #142: at start only the busiest controller, the selected one (#73), is open.
    [Fact]
    public void AtStartOnlyTheBusiestControllerIsOpen()
    {
        var other = new UsbNode("C3", UsbNodeKind.Controller, "Third controller", [Hub("ROOT3", Device("A"))]);

        var viewModel = TwoControllers(Phone, Desk[0], other);

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.True(Item(viewModel, "C1").IsExpanded);
        Assert.True(Item(viewModel, "C2").IsCollapsed);
        Assert.True(Item(viewModel, "C3").IsCollapsed);
        Assert.True(Item(viewModel, NodeViewModel.HostId).IsExpanded);
        Assert.True(Item(viewModel, "ROOT2").IsExpanded); // Only the controller row is collapsed.
    }

    [Fact]
    public void AControllerWithNoDevicesStartsCollapsed()
    {
        var empty = new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", null, null)]);
        var viewModel = NewViewModel();

        viewModel.Load([empty]);

        Assert.True(Item(viewModel, "C1").IsCollapsed);
    }

    [Fact]
    public void LiveUpdatesKeepTheUsersControllerChoice()
    {
        var viewModel = TwoControllers(Phone, Desk[0]);
        Item(viewModel, "C1").IsExpanded = false;
        Item(viewModel, "C2").IsExpanded = true;

        viewModel.Apply([new UsbChange(UsbChangeKind.Added, "ROOT2", Port("ROOT2", 2, Device("STICK")))]);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        Assert.True(Item(viewModel, "C1").IsCollapsed);
        Assert.True(Item(viewModel, "C2").IsExpanded);
    }

    // Owner decision in #208: until the user acts, a controller that grows past the selected one is selected and opened.
    private static readonly UsbNode EmptyFirst = new("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", null, null)]);

    private static readonly UsbNode EmptySecond = new("C2", UsbNodeKind.Controller, "Second controller", [Hub("ROOT2", null, null)]);

    private static UsbChange Plug(string hubId, int port, UsbNode node) => new(UsbChangeKind.Added, hubId, Port(hubId, port, node));

    [Fact]
    public void ADockPluggedInAfterStartIsSelectedAndOpened()
    {
        var viewModel = TwoControllers(EmptyFirst, EmptySecond);
        Assert.True(Item(viewModel, "C1").IsCollapsed);
        Assert.True(Item(viewModel, "C2").IsCollapsed);

        viewModel.Apply([Plug("ROOT2", 1, Hub("DOCK", Device("D1"), Device("D2")))]);

        Assert.Equal("C2", viewModel.SelectedControllerId);
        Assert.True(Item(viewModel, "C2").IsExpanded);
        Assert.True(Item(viewModel, "C1").IsCollapsed);
    }

    [Fact]
    public void TheSelectedControllerStaysSelectedWhenItShrinks()
    {
        var viewModel = TwoControllers(Phone, Desk[0]);
        Assert.Equal("C1", viewModel.SelectedControllerId);

        // The hub with the keyboard, then the mouse drop out: C1 has fewer devices than C2, but nothing grew.
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 1, null))]);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.True(Item(viewModel, "C1").IsExpanded); // It still shows the disconnected devices.
    }

    // #212: disconnected devices count, so a dropped dock keeps its place against a small new device elsewhere.
    [Fact]
    public void ADroppedTreeKeepsItsPlaceUntilABiggerTreeArrives()
    {
        var viewModel = TwoControllers(EmptySecond, Desk[0]);
        Assert.Equal("C1", viewModel.SelectedControllerId);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 1, null))]);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        viewModel.Apply([Plug("ROOT2", 1, Device("STICK"))]);

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.True(Item(viewModel, "C1").IsExpanded);
        Assert.True(Item(viewModel, "C2").IsCollapsed);

        viewModel.Apply([Plug("ROOT2", 2, Hub("DOCK", Device("D1"), Device("D2"), Device("D3")))]);

        Assert.Equal("C2", viewModel.SelectedControllerId);
    }

    // #68 stays: the busiest pick at start and on Refresh counts connected hubs and devices only (#212 review).
    [Fact]
    public void RefreshPicksTheControllerWithConnectedDevicesOverADroppedTree()
    {
        var viewModel = TwoControllers(Phone, Desk[0]);
        viewModel.RefreshRequested += () => { };
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 1, null))]);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        viewModel.RequestRefresh(clearLog: false);
        viewModel.Load([Phone, new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", null, null)])], monitors: []);

        Assert.Equal("C2", viewModel.SelectedControllerId);
    }

    [Fact]
    public void AControllerThatGrowsSwitchesOnlyWhenItHasMoreThanTheSelectedOne()
    {
        // C1: hub, keyboard and mouse (3). C2: phone (1) and three free ports.
        var phone = new UsbNode("C2", UsbNodeKind.Controller, "Second controller", [Hub("ROOT2", Device("PHONE"), null, null, null)]);
        var viewModel = TwoControllers(phone, Desk[0]);

        viewModel.Apply([Plug("ROOT2", 2, Device("STICK"))]);
        viewModel.Apply([Plug("ROOT2", 3, Device("CARD"))]);

        Assert.Equal("C1", viewModel.SelectedControllerId); // 3 against 3: a tie does not switch.

        viewModel.Apply([Plug("ROOT2", 4, Device("DISK"))]);

        Assert.Equal("C2", viewModel.SelectedControllerId);
        Assert.True(Item(viewModel, "C2").IsExpanded);
        Assert.True(Item(viewModel, "C1").IsCollapsed);
    }

    [Fact]
    public void AfterTheUserSelectsAnItemTheControllerIsNotSwitched()
    {
        var viewModel = TwoControllers(EmptyFirst, EmptySecond);
        viewModel.SelectedNode = Item(viewModel, "C1");

        viewModel.Apply([Plug("ROOT2", 1, Hub("DOCK", Device("D1")))]);

        Assert.Equal("C1", viewModel.SelectedControllerId);
    }

    [Fact]
    public void AfterTheUserSelectsAMonitorTheControllerIsNotSwitched()
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => [Panel, Dell]);
        viewModel.Load([EmptyFirst, EmptySecond]);
        viewModel.SelectedNode = DisplaysGroup(viewModel)!.Children[1];

        viewModel.Apply([Plug("ROOT2", 1, Hub("DOCK", Device("D1")))]);

        Assert.Equal("C1", viewModel.SelectedControllerId);
    }

    [Fact]
    public void AfterTheUserOpensOrClosesAControllerNothingIsSwitchedOrOpened()
    {
        var viewModel = TwoControllers(Phone, Desk[0]);
        Item(viewModel, "C2").IsExpanded = true;

        viewModel.Apply([Plug("ROOT2", 1, Hub("DOCK", Device("D1"), Device("D2"), Device("D3")))]);

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.True(Item(viewModel, "C1").IsExpanded);
        Assert.True(Item(viewModel, "C2").IsExpanded);
    }

    [Fact]
    public void RefreshFollowsAgainFromTheBusiestController()
    {
        var viewModel = TwoControllers(Phone, Desk[0]);
        viewModel.RefreshRequested += () => { };
        viewModel.SelectedNode = Item(viewModel, "C2");

        viewModel.RequestRefresh(clearLog: false);
        viewModel.Load([Phone, Desk[0]], monitors: []);

        Assert.Equal("C1", viewModel.SelectedControllerId);
        Assert.Null(viewModel.SelectedNode);
        Assert.True(Item(viewModel, "C1").IsExpanded);
        Assert.True(Item(viewModel, "C2").IsCollapsed);

        viewModel.Apply([Plug("ROOT2", 1, Hub("DOCK", Device("D1"), Device("D2"), Device("D3")))]);

        Assert.Equal("C2", viewModel.SelectedControllerId);
    }

    // The Displays group (#133) stays open and has no control.
    [Fact]
    public void TheDisplaysGroupCannotCollapse()
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => [Panel]);
        viewModel.Load(Desk);
        var group = Item(viewModel, NodeViewModel.DisplaysId);

        group.IsExpanded = false;

        Assert.False(group.CanExpand);
        Assert.True(group.IsExpanded);
    }

    // Owner decision in #100: the window never says a limit is unverified or not verified; the log keeps it.
    [Fact]
    public void TheWindowNeverSaysUnverifiedButTheLogDoes()
    {
        var log = new EventLog();
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "unverified");
        var viewModel = new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), () => External(3), log);
        viewModel.Load(Desk);

        viewModel.Computers.Single().IsChecked = true;

        var finding = Assert.Single(viewModel.Findings);
        Assert.Equal("3 external displays are active; displays.max_external is 2.", finding.Message);
        Assert.Contains("External displays: 3, limit 2 (exceeded)", viewModel.StatusRows.Single().Details);
        var shown = viewModel.Findings.SelectMany(item => new[] { item.Header, item.Message })
            .Concat(viewModel.StatusRows.SelectMany(row => row.Details.Prepend(row.Word).Prepend(row.Label)))
            .Concat(All(viewModel.Roots).Select(item => item.Text));
        Assert.DoesNotContain(shown, text => text.Contains("verified", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(log.Entries, entry => entry.Text.EndsWith("displays.max_external is 2 (unverified limit).", StringComparison.Ordinal));
        Assert.Contains(log.Entries, entry => entry.Text.Contains("comes from an unverified profile", StringComparison.Ordinal));
    }
}

public class LogViewModelTests
{
    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    private static readonly IReadOnlyList<UsbNode> Desk =
        [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("HUB", Device("KEYBOARD"), null), Device("MOUSE"))])];

    // Platform "p" has no limits, so every limit is unknown while it is checked.
    private static MainViewModel NewViewModel(EventLog log)
    {
        var database = DatabaseFolder.Load("Dell Inc.", "Latitude 7450", status: "verified");
        return new MainViewModel(database, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), log: log);
    }

    [Fact]
    public void TheLogWindowListsEveryEntryNewestAtTheBottom()
    {
        var log = new EventLog();
        log.Add("Started");
        var viewModel = NewViewModel(log);

        viewModel.Load(Desk);
        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);
        viewModel.OperatingSystems.Single().IsChecked = true;

        Assert.Equal(log.Entries.Select(entry => entry.ToString()), viewModel.Log.Lines);
        Assert.Equal("Started", log.Entries[0].Text);
        Assert.Contains(log.Entries, entry => entry.Text.StartsWith("Database loaded: 1 platform profiles, 1 computer profiles", StringComparison.Ordinal));
        Assert.Contains(log.Entries, entry => entry.Text == "USB devices read: 1 controllers");
        Assert.Contains(log.Entries, entry => entry.Text == "Disconnected: MOUSE from Port 2 of ROOT");
        Assert.Contains(log.Entries, entry => entry.Text == "Profile checked: p");
        Assert.Equal("Profile checked: p", log.Entries.Last(entry => !entry.Text.StartsWith("Finding", StringComparison.Ordinal)).Text);
    }

    [Fact]
    public void ACableMarkIsLogged()
    {
        var log = new EventLog();
        var viewModel = NewViewModel(log);
        viewModel.Load(Desk);
        var hub = viewModel.Roots[0].Children[0].Children[0].Children[0].Children[0]; // host > C1 > ROOT > ROOT|port1 > HUB.

        hub.ExtraHops = 2;

        Assert.Equal("HUB", hub.Id);
        Assert.Contains(log.Entries, entry => entry.Text == "Cable mark: +2 hops on the line to HUB");
    }

    [Fact]
    public void ExportWritesEveryEntryUnknownFindingsIncluded()
    {
        var log = new EventLog();
        var viewModel = NewViewModel(log);
        viewModel.Load(Desk);

        viewModel.OperatingSystems.Single().IsChecked = true;

        var export = viewModel.Log.Export();
        Assert.Equal(log.Export(), export);
        Assert.Contains("Finding (no profile, unknown): ", export, StringComparison.Ordinal);
        Assert.Contains("Finding (p, unknown): ", export, StringComparison.Ordinal);
        Assert.Contains("USB devices read: 1 controllers", export, StringComparison.Ordinal);
        Assert.EndsWith(".txt", viewModel.Log.ExportFileName, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutADatabaseItsErrorsAreLogged()
    {
        var log = new EventLog();

        _ = new MainViewModel(null, [new DatabaseError("base.yaml", 2, "bad")], new ComputerIdentity(null, null), log: log);

        Assert.Equal(["Database not loaded: 1 errors", "Database error: base.yaml:2: bad"], log.Entries.Select(entry => entry.Text));
    }

    // #206: Refresh reads the tree again and keeps the log and the disconnected devices.
    [Fact]
    public void RefreshKeepsTheLogAndTheDisconnectedDevices()
    {
        var log = new EventLog();
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), log: log);
        var asked = 0;
        viewModel.RefreshRequested += () => asked++;
        viewModel.Load(Desk, monitors: []);
        IReadOnlyList<UsbNode> mouseGone = [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("HUB", Device("KEYBOARD"), null), null)])];
        viewModel.Load(mouseGone, monitors: []);
        var entries = log.Entries.Count;

        viewModel.RequestRefresh(clearLog: false);

        Assert.Equal(1, asked);
        Assert.True(viewModel.IsLoading);
        Assert.Equal(entries + 1, log.Entries.Count);
        Assert.Equal("Refresh", log.Entries[^1].Text);
        viewModel.Load(mouseGone, monitors: []);
        Assert.Contains(viewModel.Disconnected, item => item.Text.Contains("MOUSE", StringComparison.Ordinal));
    }

    // #206: Refresh and clear log is a fresh start: an empty log and no disconnected devices.
    [Fact]
    public void RefreshAndClearLogStartsAFreshSession()
    {
        var log = new EventLog();
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), log: log);
        var asked = 0;
        viewModel.RefreshRequested += () => asked++;
        viewModel.Load(Desk, monitors: []);
        IReadOnlyList<UsbNode> mouseGone = [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("HUB", Device("KEYBOARD"), null), null)])];
        viewModel.Load(mouseGone, monitors: []);
        Assert.NotEmpty(viewModel.Disconnected);

        viewModel.RequestRefresh(clearLog: true);

        Assert.Equal(1, asked);
        Assert.Equal(["Refresh: log and disconnected devices cleared"], log.Entries.Select(entry => entry.Text));
        viewModel.Load(mouseGone, monitors: []);
        Assert.Empty(viewModel.Disconnected);
    }

    // #206: the log window follows a cleared log.
    [Fact]
    public void TheLogWindowEmptiesWhenTheLogIsCleared()
    {
        var log = new EventLog();
        log.Add("Started");
        var window = new LogViewModel(log);

        log.Clear();
        log.Add("Refresh");

        Assert.Equal([log.Entries[0].ToString()], window.Lines);
    }

    // #206: a second refresh while the first one is still reading does nothing, so two reads never overlap.
    [Fact]
    public void ASecondRefreshWhileReadingIsIgnored()
    {
        var log = new EventLog();
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), log: log);
        var asked = 0;
        viewModel.RefreshRequested += () => asked++;
        viewModel.Load(Desk, monitors: []);

        viewModel.RequestRefresh(clearLog: false);
        viewModel.RequestRefresh(clearLog: true);

        Assert.Equal(1, asked);
        Assert.Equal("Refresh", log.Entries[^1].Text);
    }
}
