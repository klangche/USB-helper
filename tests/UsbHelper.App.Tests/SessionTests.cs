using System.Globalization;
using UsbHelper.App.ViewModels;
using UsbHelper.Core.Detection;
using UsbHelper.Core.Diagnostics;
using UsbHelper.Core.Topology;

namespace UsbHelper.App.Tests;

/// <summary>A new session after sleep or a long time, and a restart into a newer installed version (#341).</summary>
public class SessionTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static UsbNode Device(string id) => new(id, UsbNodeKind.Device, id, []);

    private static UsbNode Port(string hubId, int number, UsbNode? child) =>
        new($"{hubId}|port{number}", UsbNodeKind.Port, $"Port {number}", child == null ? [] : [child]) { PortNumber = number };

    private static UsbNode Hub(string id, params UsbNode?[] ports) =>
        new(id, UsbNodeKind.Hub, id, ports.Select((child, index) => Port(id, index + 1, child)).ToList());

    private static readonly IReadOnlyList<UsbNode> Desk =
        [new UsbNode("C1", UsbNodeKind.Controller, "Controller", [Hub("ROOT", Hub("HUB", Device("KEYBOARD"), null), Device("MOUSE"))])];

    private static NodeViewModel? Find(IEnumerable<NodeViewModel> items, string id) =>
        items.Select(item => item.Id == id ? item : Find(item.Children, id)).FirstOrDefault(found => found != null);

    // The app at 09:30 with the desk read, the mouse unplugged (so it is missing) and the hub marked.
    private static (MainViewModel Window, EventLog Log, Clock Clock, Func<int> Reads) Running(string installed = "0.11.0")
    {
        var clock = new Clock();
        var log = new EventLog(clock);
        log.SessionStarted("0.11.0");
        var window = new MainViewModel(null, [], new ComputerIdentity(null, null), log: log)
        {
            Session = new SessionWatch(clock),
            InstalledVersion = () => installed,
            RunningVersion = "0.11.0",
        };
        var reads = 0;
        window.RefreshRequested += () => reads++;
        window.Load(Desk, []);
        window.Apply([new UsbChange(UsbChangeKind.Removed, "ROOT", Port("ROOT", 2, null))]);
        Find(window.Roots, "HUB")!.MarkIndex = 1;
        return (window, log, clock, () => reads);
    }

    [Fact]
    public void NewSessionClearsTheLogAndHistoryKeepsTheMarksAndReadsTheTreeAgain()
    {
        var (window, log, clock, reads) = Running();
        Assert.NotEmpty(window.Disconnected);
        // Both ports were tried: the mouse's port keeps its result after the unplug (#342).
        Assert.StartsWith("Tested on 2 of 2 ports", window.Report(clock.Now.DateTime, null, null, "0.11.0").PortsTested, StringComparison.Ordinal);
        clock.Now += TimeSpan.FromHours(12);

        window.SessionTick();
        Assert.Equal("The computer slept. Start a new session?", window.SessionOffer!.Text);
        Assert.False(window.SessionOffer.Restart);

        window.StartNewSession();

        Assert.Null(window.SessionOffer);
        Assert.Equal("New session 2026-11-22 21:30 (USB-helper 0.11.0)", log.Entries[0].Text);
        Assert.DoesNotContain(log.Entries, entry => entry.Text.StartsWith("USB-helper 0.11.0 started", StringComparison.Ordinal));
        Assert.Empty(window.Disconnected);
        // The ports tested start over: only the port in use now.
        Assert.StartsWith("Tested on 1 of 2 ports", window.Report(clock.Now.DateTime, null, null, "0.11.0").PortsTested, StringComparison.Ordinal);
        Assert.Equal(1, reads());
        Assert.DoesNotContain(log.Entries, entry => entry.Text == "Refresh");
        window.Load(Desk, []);
        Assert.Equal(new CableMark(1, 1), Find(window.Roots, "HUB")!.Mark); // The marks stay.
        Assert.Equal(clock.Now, window.Session.Started);
    }

    [Fact]
    public void KeepAddsTheKeptLineAndClearsNothing()
    {
        var (window, log, clock, reads) = Running();
        var entries = log.Entries.Count;
        clock.Now += TimeSpan.FromHours(12);
        window.SessionTick();

        window.KeepSession();

        Assert.Null(window.SessionOffer);
        Assert.Equal("Kept the session after sleep (12 h)", log.Entries[^1].Text);
        Assert.Equal(entries + 2, log.Entries.Count); // "The computer slept (12 h)" and the kept line.
        Assert.NotEmpty(window.Disconnected);
        Assert.Equal(0, reads());
    }

    [Fact]
    public void ALongSessionAsksWhenTheWindowIsActivated()
    {
        var (window, _, clock, _) = Running();
        clock.Now += TimeSpan.FromDays(2);
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // English day and month names on every machine.
        try
        {
            window.SessionActivated();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        Assert.Equal("USB-helper has been open since Sunday 22 November. Start a new session?", window.SessionOffer!.Text);
    }

    [Fact]
    public void ANewerVersionOnDiskGivesTheRestartPopup()
    {
        var (window, log, clock, _) = Running(installed: "0.12.0");
        var restarted = 0;
        window.RestartRequested = () => restarted++;
        clock.Now += TimeSpan.FromHours(12);

        window.SessionTick();

        Assert.True(window.SessionOffer!.Restart);
        Assert.Equal("USB-helper 0.12.0 is installed; this window is 0.11.0. Restart now?", window.SessionOffer.Text);
        window.Restart();
        Assert.Equal(1, restarted);
        Assert.Equal("Restarting into the installed version", log.Entries[^1].Text);
    }

    [Fact]
    public void LaterKeepsEverythingAndAsksAgainOnlyAtTheNextBreak()
    {
        var (window, log, clock, reads) = Running(installed: "0.12.0");
        clock.Now += TimeSpan.FromHours(12);
        window.SessionTick();

        window.KeepSession();

        Assert.Null(window.SessionOffer);
        Assert.Equal("Restart later", log.Entries[^1].Text);
        Assert.NotEmpty(window.Disconnected);
        Assert.Equal(0, reads());
        clock.Now += TimeSpan.FromSeconds(30);
        window.SessionTick();
        window.SessionActivated();
        Assert.Null(window.SessionOffer);
        clock.Now += TimeSpan.FromHours(1);
        window.SessionTick();
        Assert.True(window.SessionOffer!.Restart);
    }

    // A restart that does not happen leaves this window open: it is asked again at the next break.
    [Fact]
    public void AFailedRestartIsAskedAgainAtTheNextBreak()
    {
        var (window, _, clock, _) = Running(installed: "0.12.0");
        clock.Now += TimeSpan.FromHours(12);
        window.SessionTick();

        window.Restart(); // No RestartRequested: nothing starts.

        clock.Now += TimeSpan.FromHours(1);
        window.SessionTick();
        Assert.NotNull(window.SessionOffer);
    }

    // During a read, the read already running brings the fresh tree: no second read, and no "Refresh" line.
    [Fact]
    public void ANewSessionDuringAReadWaitsForThatRead()
    {
        var (window, log, clock, reads) = Running();
        clock.Now += TimeSpan.FromHours(12);
        window.SessionTick();
        window.IsLoading = true;

        window.StartNewSession();

        Assert.Equal(0, reads());
        Assert.Equal("New session 2026-11-22 21:30 (USB-helper 0.11.0)", log.Entries[0].Text);
        Assert.DoesNotContain(log.Entries, entry => entry.Text == "Refresh");
        window.Load(Desk, []);
        Assert.False(window.IsLoading);
    }

    [Fact]
    public void TheSameOrAnOlderVersionOnDiskAsksForANewSession()
    {
        var (window, _, clock, _) = Running(installed: "0.10.0");
        clock.Now += TimeSpan.FromHours(12);

        window.SessionTick();

        Assert.False(window.SessionOffer!.Restart);
    }

    [Fact]
    public void TheInstalledVersionIsReadFromTheAppOnDisk()
    {
        Assert.Null(UsbHelper.App.Update.InstalledApp.Version(Path.Combine(Path.GetTempPath(), "no-such-usbhelper.dll")));
        Assert.Equal(AppVersion.Current, UsbHelper.App.Update.InstalledApp.Version(typeof(MainViewModel).Assembly.Location));
    }
}
