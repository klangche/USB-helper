using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests;

// #134: the tree as text, as it is shown, for the log and the clipboard.
public class TreeTextTests
{
    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    // Controller > root hub (port 1, USB-C: hub with a keyboard and an empty port; port 2: DisplayLink adapter).
    private static IReadOnlyList<UsbNode> Desk()
    {
        var root = Hub("ROOT", Hub("HUB", Device("KEYBOARD") with { VendorId = 0x046D, ProductId = 0xC31C }, null),
            Device("DL") with { VendorId = UsbNode.DisplayLinkVendorId, ProductId = 0x6006 });
        root = root with { Children = [root.Children[0] with { UsbC = true }, root.Children[1]] };
        return [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [root])];
    }

    private static IReadOnlyList<DisplayMonitor> Monitors() =>
        [new("PANEL", null, MonitorOutput.Internal), new("DELL", "DELL U2723QE", MonitorOutput.Hdmi)];

    private static MainViewModel Loaded(EventLog log, FakeWindow? window = null)
    {
        var viewModel = new MainViewModel(null, [], new ComputerIdentity("Dell Inc.", "Latitude 7450"), Monitors, log) { Window = window };
        viewModel.Load(Desk());
        return viewModel;
    }

    private static readonly string Golden = string.Join(Environment.NewLine,
        "Dell Inc. Latitude 7450 (Hubs: 1, deepest tier: 5, deepest hop: 3)", // All controllers (#210).
        "|-- Controller (Hubs: 1, deepest tier: 5, deepest hop: 3)", // The marked line counts.
        "|   `-- ROOT (has a display)",
        "|       |-- Port 1 [USB-C]",
        "|       |   `-- [+2 hops] HUB - unknown", // No profile is checked, so every result is unknown.
        "|       |       `-- Port 1",
        "|       |           `-- KEYBOARD (046D:C31C) - unknown",
        "|       `-- Port 2",
        "|           `-- [Display] DL (17E9:6006) - unknown (Extended display: DisplayLink driver required)",
        "`-- Displays",
        "    |-- Monitor (built-in)",
        "    `-- DELL U2723QE (HDMI)");

    [Fact]
    public void TheTreeIsDrawnWithPlainTextLines()
    {
        var viewModel = Loaded(new EventLog());
        Find(viewModel.Roots[0], "HUB").ExtraHops = 2;

        Assert.Equal(Golden, TreeText.Render(viewModel.Roots[0]));
    }

    // #230: a header names the company, the room and the licensed email when any is set.
    [Fact]
    public void AHeaderNamesTheCompanyRoomAndLicensedEmail()
    {
        var viewModel = Loaded(new EventLog());
        Find(viewModel.Roots[0], "HUB").ExtraHops = 2;

        Assert.Equal(string.Join(Environment.NewLine, "Company: Acme AB", "Room: Room 4.12", "Licensed to: test@example.com", "", Golden),
            TreeText.Render(viewModel.Roots[0], "Acme AB", "Room 4.12", "test@example.com"));
        Assert.Equal(string.Join(Environment.NewLine, "Room: Room 4.12", "", Golden), TreeText.Render(viewModel.Roots[0], null, "Room 4.12", null));
    }

    [Fact]
    public void WithoutCompanyRoomOrEmailThereIsNoHeader()
    {
        var viewModel = Loaded(new EventLog());
        Find(viewModel.Roots[0], "HUB").ExtraHops = 2;

        Assert.Equal(Golden, TreeText.Render(viewModel.Roots[0], null, null, null));
    }

    [Fact]
    public void TheFirstReadAndEveryChangeLogTheTree()
    {
        var log = new EventLog();
        var viewModel = Loaded(log);

        Assert.Single(TreeEntries(log));

        viewModel.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);

        var trees = TreeEntries(log);
        Assert.Equal(2, trees.Count);
        Assert.Contains("`-- DL (17E9:6006) (disconnected) - disconnected", trees[1]);
        Assert.Contains(trees[1], log.Export());
    }

    [Fact]
    public void ADisplayChangeLogsTheTreeOnlyWhenItLooksDifferent()
    {
        var log = new EventLog();
        IReadOnlyList<DisplayMonitor> monitors = Monitors();
        var viewModel = new MainViewModel(null, [], new ComputerIdentity(null, null), () => monitors, log);
        viewModel.Load(Desk());

        viewModel.DisplaysChanged(); // For example the work area changed: nothing to see.

        Assert.Single(TreeEntries(log));

        monitors = [Monitors()[0]];
        viewModel.DisplaysChanged();

        Assert.Equal(2, TreeEntries(log).Count);
        Assert.DoesNotContain("DELL U2723QE", TreeEntries(log)[1]);
    }

    // A cable mark changes the tree but is no display change: the next display event logs no tree for it.
    [Fact]
    public void ADisplayEventLogsNoTreeForAnEarlierChange()
    {
        var log = new EventLog();
        var viewModel = Loaded(log);
        Find(viewModel.Roots[0], "HUB").ExtraHops = 2;

        viewModel.DisplaysChanged();

        Assert.Single(TreeEntries(log));
    }

    [Fact]
    public void CopyTreeAsTextGivesTheSameText()
    {
        var log = new EventLog();
        var window = new FakeWindow();
        var viewModel = Loaded(log, window);

        Assert.True(viewModel.CopyTreeCommand.CanExecute(null));
        viewModel.CopyTreeCommand.Execute(null);

        Assert.Equal(TreeText.Render(viewModel.Roots[0]), Assert.Single(window.Copied));
        Assert.EndsWith(window.Copied[0], TreeEntries(log)[^1]);
    }

    [Fact]
    public void ThereIsNothingToCopyBeforeTheTreeIsRead()
    {
        var viewModel = new MainViewModel(new ComputerIdentity(null, null));

        Assert.False(viewModel.CopyTreeCommand.CanExecute(null));
    }

    private static List<string> TreeEntries(EventLog log) =>
        [.. log.Entries.Select(entry => entry.Text).Where(text => text.StartsWith("Tree:", StringComparison.Ordinal))];

    private static NodeViewModel Find(NodeViewModel item, string id) => FindOrNull(item, id)!;

    private static NodeViewModel? FindOrNull(NodeViewModel item, string id) =>
        item.Id == id ? item : item.Children.Select(child => FindOrNull(child, id)).FirstOrDefault(found => found != null);

    // Records what the window was asked to copy.
    private sealed class FakeWindow : IMainWindowActions
    {
        public List<string> Copied { get; } = [];

        public void CopyText(string text) => Copied.Add(text);

        public void ExportSetup() { }

        public void ConfirmRefresh() { }

        public void ExportProfile() { }

        public void ExportLog() { }

        public void ShowSettings() { }

        public void ShowUnlock() { }

        public void ShowAbout() { }

        public void Exit() { }
    }
}
