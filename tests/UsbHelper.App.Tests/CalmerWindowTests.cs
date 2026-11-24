using UsbHelper.App.ViewModels;
using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;

namespace UsbHelper.App.Tests;

/// <summary>A calmer window (#397): the result in one line per group, unknowns in a quiet line and the row's hover.</summary>
public class CalmerWindowTests
{
    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    // A hub with a keyboard behind it on the computer's own port.
    private static readonly IReadOnlyList<UsbNode> Desk = [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("HUB1", Device("KEYBOARD")))])];

    private static MainViewModel Window(params string[] ticked)
    {
        var window = new MainViewModel(DatabaseFormats.Read(4, EmbeddedDatabase.Files()[4]), [], new ComputerIdentity("", "", Platform: "windows-intel"));
        foreach (var option in window.OperatingSystems)
        {
            option.IsChecked = ticked.Contains(option.Profile.Id);
        }
        window.Load(Desk);
        return window;
    }

    private static IEnumerable<NodeViewModel> Flat(NodeViewModel item) => item.Children.SelectMany(Flat).Prepend(item);

    // Windows Intel's own hubs are not known; Apple silicon's and Windows AMD's are, or are not, by the database.
    [Fact]
    public void EachGroupIsOneLineAndTheSystemsNotFullyKnownShareAQuietLine()
    {
        var window = Window("windows-intel", "macos-apple-silicon", "windows-amd");

        var quiet = window.OutcomeRows.Where(row => row.IsQuiet).ToList();
        Assert.True(quiet.Count <= 1);
        Assert.All(window.OutcomeRows, row => Assert.False(row.IsExpanded));
        if (quiet is [var line])
        {
            Assert.EndsWith("not fully known", line.Title, StringComparison.Ordinal);
            Assert.Empty(line.Issues);
            var intel = window.OperatingSystems.Single(option => option.Profile.Id == "windows-intel").Label;
            Assert.Contains($"{intel}: ", line.Hint, StringComparison.Ordinal);
        }
        // Every system is named once, in a line or in the quiet line's hover.
        var named = window.OutcomeRows.Where(row => !row.IsQuiet).SelectMany(row => row.Systems.Split(", "))
            .Concat(quiet.SelectMany(row => row.Hint!.Split('\n').Select(reason => reason[..reason.IndexOf(':')]))).ToList();
        Assert.Equal(named.Distinct().Count(), named.Count);
        Assert.Equal(4, named.Count); // The three ticked systems and this computer, measured.
    }

    [Fact]
    public void ALineOpensOnClickAndAnIssueShowsWhatWasCountedOnlyOnceOpened()
    {
        var window = Window("windows-intel", "macos-apple-silicon", "windows-amd");
        var row = window.OutcomeRows.First(row => row.CanExpand);

        Assert.False(row.ShowsIssues || row.ShowsHint);
        row.Toggle.Execute(null);

        Assert.True(row.IsQuiet ? row.ShowsHint : row.ShowsIssues);
        Assert.Equal("⌄", row.Chevron);
        if (row.Issues.FirstOrDefault(issue => issue.Counted != null) is { } issue)
        {
            Assert.False(issue.ShowsCounted);
            issue.IsOpen = true;
            Assert.True(issue.ShowsCounted);
        }
    }

    // An unknown finding is never a panel line: it is in the row's hover (and the log).
    [Fact]
    public void UnknownFindingsAreInTheRowsHoverNotInThePanel()
    {
        var window = Window("windows-intel");

        Assert.DoesNotContain(window.OutcomeRows.SelectMany(row => row.Issues), issue => issue.Text.Contains("at least", StringComparison.Ordinal));
        // The system does not say whether HUB1's port is built in: that is in HUB1's hover.
        var hub = Flat(window.Roots[0]).First(row => row.Id == "HUB1");
        Assert.NotEmpty(hub.Unknowns);
        Assert.Contains("Not known: ", hub.RowTip, StringComparison.Ordinal);
        Assert.DoesNotContain(window.OutcomeRows.SelectMany(row => row.Issues), issue => hub.Unknowns.Contains(issue.Text));
    }
}
