using UsbHelper.Core.Database;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using static UsbHelper.Core.Tests.Detection.TestTree;

namespace UsbHelper.Core.Tests.Diagnostics;

public class EventLogTests
{
    // A clock that stands still until moved, in UTC so the texts do not depend on the machine's time zone.
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly Clock clock = new();

    private EventLog NewLog() => new(clock);

    // Controller > root hub (port 1: hub with keyboard and an empty port, port 2: mouse).
    private static readonly IReadOnlyList<UsbNode> Desk = [Controller("C1", Hub("ROOT", Hub("HUB", Device("KEYBOARD"), null), Device("MOUSE")))];

    private static IEnumerable<string> Texts(EventLog log) => log.Entries.Select(entry => entry.Text);

    [Fact]
    public void EachEntryHasItsTimeAndText()
    {
        var log = NewLog();

        log.Add("Started");
        clock.Now = clock.Now.AddSeconds(5);
        log.Add("Reading USB devices");

        Assert.Equal(
            [new LogEntry(new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero), "Started"),
             new LogEntry(new DateTimeOffset(2026, 11, 2, 9, 30, 5, TimeSpan.Zero), "Reading USB devices")],
            log.Entries);
    }

    [Fact]
    public void EveryNewEntryIsAnnounced()
    {
        var log = NewLog();
        var announced = new List<LogEntry>();
        log.Added += announced.Add;

        log.Add("Started");

        Assert.Equal(log.Entries, announced);
    }

    [Fact]
    public void DatabaseErrorsAreLoggedOneByOne()
    {
        var log = NewLog();

        log.DatabaseFailed([new DatabaseError("computers/a.yaml", 3, "bad value"), new DatabaseError("base.yaml", 0, "missing")]);

        Assert.Equal(
            ["Database not loaded: 2 errors", $"Database error: {new DatabaseError("computers/a.yaml", 3, "bad value")}",
             $"Database error: {new DatabaseError("base.yaml", 0, "missing")}"],
            Texts(log));
    }

    [Fact]
    public void ReadingTheTreeLogsTheControllersAndEveryOperatingSystemError()
    {
        var log = NewLog();

        log.TreeRead([Controller("C1", Hub("ROOT", Device("MOUSE", problem: "code 43"), null)),
            Controller("C2", Hub("ROOT2", Device("STICK")))]);

        Assert.Equal(["USB devices read: 2 controllers", "Error on MOUSE: code 43"], Texts(log));
    }

    [Fact]
    public void ReadingTheTreeLogsPerHubWhichPortsWindowsCallsUsbC()
    {
        var log = NewLog();
        var root = Hub("ROOT", Hub("DOCK", null, null), null, null);
        var dock = root.Children[0].Children[0];
        dock = dock with { Children = [dock.Children[0] with { UsbC = true }, dock.Children[1]] };
        root = root with { Children = [root.Children[0] with { UsbC = false, Children = [dock] }, root.Children[1] with { UsbC = true }, root.Children[2]] };

        log.TreeRead([Controller("C1", root), Controller("C2", Hub("ROOT2", null, null))]);

        // Ports Windows says nothing about are left out; a hub with none it says anything about has no line.
        Assert.Equal(
            ["USB devices read: 2 controllers", "USB-C ports of ROOT: Port 1 no, Port 2 yes", "USB-C ports of DOCK: Port 1 yes"],
            Texts(log));
    }

    // #133: the monitors at start, then each one connected or removed.
    [Fact]
    public void DisplaysAreLoggedAtStartAndOnEveryChange()
    {
        var log = NewLog();
        DisplayMonitor panel = new("1", null, MonitorOutput.Internal);
        DisplayMonitor dell = new("2", "DELL U2723QE", MonitorOutput.Hdmi);
        DisplayMonitor lg = new("3", "LG HDR 4K", MonitorOutput.DisplayPort);

        log.Displays([panel, dell]);
        log.Displays([panel, dell]);
        log.Displays([panel, lg]);
        log.Displays(null);
        log.Displays(null);

        Assert.Equal(
            [
                "Displays read: 2",
                "Display: unnamed (built-in)",
                "Display: DELL U2723QE (HDMI)",
                "Display connected: LG HDR 4K (DisplayPort)",
                "Display disconnected: DELL U2723QE (HDMI)",
                "Displays could not be read",
            ],
            Texts(log));
    }

    // #143: a monitor shown under a USB node is logged when the link starts and when it ends.
    [Fact]
    public void DisplayLinksAreLoggedWhenTheyStartAndEnd()
    {
        var log = NewLog();
        var container = Guid.NewGuid();
        IReadOnlyList<UsbNode> tree = [Controller("C1", Hub("ROOT", Hub("MONITOR-HUB") with { ContainerId = container, Name = "Realtek hub" }))];
        DisplayMonitor samsung = new("M1", "LS27A600U", MonitorOutput.DisplayPort) { ContainerId = container };

        log.DisplayLinks(MonitorPlacement.Place(tree, [samsung]), tree);
        log.DisplayLinks(MonitorPlacement.Place(tree, [samsung]), tree);
        log.DisplayLinks(MonitorPlacement.Place([], [samsung]), []);

        Assert.Equal(
            [
                "Display under a USB device: LS27A600U (DisplayPort) under Realtek hub (same Windows container)",
                "Display no longer under a USB device: LS27A600U (DisplayPort)",
            ],
            Texts(log));
    }

    [Fact]
    public void AConnectIsOneEntry()
    {
        var log = NewLog();

        log.Changes(Desk, [new UsbChange(UsbChangeKind.Added, "HUB", Port("HUB", 2, Device("STICK")))]);

        var entry = Assert.Single(log.Entries);
        Assert.Equal("Connected: STICK on Port 2 of HUB", entry.Text);
        Assert.Equal(clock.Now, entry.Time);
    }

    [Fact]
    public void ADisconnectNamesEverythingThatWasBehindThePort()
    {
        var log = NewLog();

        log.Changes(Desk, [new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 1, null))]);

        Assert.Equal(["Disconnected: HUB from Port 1 of ROOT", "Disconnected: KEYBOARD from Port 1 of HUB"], Texts(log));
    }

    [Fact]
    public void AReconnectIsOneEntry()
    {
        var log = NewLog();

        log.Changes(Desk, [new UsbChange(UsbChangeKind.Reconnected, "ROOT", Port("ROOT", 2, Device("MOUSE")))]);

        Assert.Equal(["Reconnected: MOUSE on Port 2 of ROOT"], Texts(log));
    }

    [Fact]
    public void ANewOperatingSystemErrorIsOneEntryAndAnOldOneIsNotRepeated()
    {
        var log = NewLog();
        var broken = Port("ROOT", 2, Device("MOUSE", problem: "code 43"));

        log.Changes(Desk, [new UsbChange(UsbChangeKind.Changed, "ROOT", broken)]);
        var after = UsbTree.Apply(Desk, [new UsbChange(UsbChangeKind.Changed, "ROOT", broken)]);
        log.Changes(after, [new UsbChange(UsbChangeKind.Changed, "ROOT", broken)]);

        Assert.Equal(["Error on MOUSE: code 43"], Texts(log));
    }

    [Fact]
    public void AnErrorOnAPortNamesItsHub()
    {
        var log = NewLog();

        log.Changes(Desk, [new UsbChange(UsbChangeKind.Changed, "ROOT", Port("ROOT", 2, null) with { Problem = "port failed" })]);

        Assert.Equal(["Disconnected: MOUSE from Port 2 of ROOT", "Error on Port 2 of ROOT: port failed"], Texts(log));
    }

    [Fact]
    public void ControllersThatComeAndGoAreLogged()
    {
        var log = NewLog();
        var second = Controller("C2", Hub("ROOT2", Device("STICK")));

        log.Changes(Desk, [new UsbChange(UsbChangeKind.Added, null, second)]);
        log.Changes([.. Desk, second], [new UsbChange(UsbChangeKind.Removed, null, second)]);

        Assert.Equal(["Connected: controller C2", "Connected: STICK on Port 1 of ROOT2",
            "Disconnected: controller C2", "Disconnected: STICK from Port 1 of ROOT2"], Texts(log));
    }

    [Theory]
    [InlineData(true, "Profile checked: windows-amd")]
    [InlineData(false, "Profile unchecked: windows-amd")]
    public void AProfileCheckChangeIsOneEntry(bool isChecked, string text)
    {
        var log = NewLog();

        log.ProfileChecked("windows-amd", isChecked);

        Assert.Equal([text], Texts(log));
    }

    [Theory]
    [InlineData(2, "Cable mark: +2 hops on the line to HUB")]
    [InlineData(0, "Cable mark removed from the line to HUB")]
    public void ACableMarkIsOneEntry(int hops, string text)
    {
        var log = NewLog();

        log.CableMark("HUB", hops);

        Assert.Equal([text], Texts(log));
    }

    private static readonly Finding UnknownLimit = new(FactKind.Unknown, DiagnosticEvaluator.ComputerId, "Latitude 7450", "limits.max_hubs is unknown.");
    private static readonly Finding Risk = new(FactKind.PredictedRisk, "KEYBOARD", "KEYBOARD", "Hop 1 is above the maximum of 0.");

    [Fact]
    public void EveryFindingIsLoggedUnknownLimitsIncluded()
    {
        var log = NewLog();

        log.Findings([("windows-amd", UnknownLimit), ("windows-amd", Risk), (null, UnknownLimit)]);

        Assert.Equal(
            ["Finding (windows-amd, unknown): Latitude 7450: limits.max_hubs is unknown.",
             "Finding (windows-amd, risk): KEYBOARD: Hop 1 is above the maximum of 0.",
             "Finding (no profile, unknown): Latitude 7450: limits.max_hubs is unknown."],
            Texts(log));
    }

    [Fact]
    public void AFindingIsLoggedWhenItAppearsNotOnEveryRefresh()
    {
        var log = NewLog();

        log.Findings([("p", Risk)]);
        log.Findings([("p", Risk), ("p", UnknownLimit)]);
        log.Findings([("p", UnknownLimit)]);
        log.Findings([("p", Risk), ("p", UnknownLimit)]); // The risk came back.

        Assert.Equal(
            ["Finding (p, risk): KEYBOARD: Hop 1 is above the maximum of 0.",
             "Finding (p, unknown): Latitude 7450: limits.max_hubs is unknown.",
             "Finding (p, risk): KEYBOARD: Hop 1 is above the maximum of 0."],
            Texts(log));
    }

    [Fact]
    public void ExportHasOneLinePerEntryWithDateAndTime()
    {
        var log = NewLog();
        log.Add("Started");
        clock.Now = clock.Now.AddMinutes(1);
        log.Findings([("p", UnknownLimit)]);

        Assert.Equal(
            "2026-11-02 09:30:00  Started" + Environment.NewLine
            + "2026-11-02 09:31:00  Finding (p, unknown): Latitude 7450: limits.max_hubs is unknown." + Environment.NewLine,
            log.Export());
    }
}
