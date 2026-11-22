using UsbHelper.Core.Diagnostics;

namespace UsbHelper.Core.Tests.Diagnostics;

/// <summary>Sleep and long sessions (#341), with a clock the test moves.</summary>
public class SessionWatchTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void ASixMinuteJumpBetweenTicksIsABreakOneMinuteIsNot()
    {
        var clock = new Clock();
        var watch = new SessionWatch(clock);

        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Null(watch.Ticked());

        clock.Now += TimeSpan.FromMinutes(6);
        var found = watch.Ticked();
        Assert.Equal(SessionBreakKind.Sleep, found!.Kind);
        Assert.Equal(TimeSpan.FromMinutes(6), found.Slept);
        Assert.Equal("Kept the session after sleep (6 min)", found.KeptText);
    }

    [Fact]
    public void ASessionStartedYesterdayIsABreakWhenActivatedTheSameDayIsNot()
    {
        var clock = new Clock();
        var watch = new SessionWatch(clock);

        clock.Now += TimeSpan.FromHours(8);
        Assert.Null(watch.Activated());

        clock.Now += TimeSpan.FromHours(10); // 2026-11-23 03:30
        var found = watch.Activated();
        Assert.Equal(SessionBreakKind.LongSession, found!.Kind);
        Assert.Equal("Kept the session, open since 2026-11-22 09:30 (18 h)", found.KeptText);
    }

    [Fact]
    public void OneBreakGivesOnePopupRequest()
    {
        var clock = new Clock();
        var watch = new SessionWatch(clock);
        clock.Now += TimeSpan.FromDays(1);

        Assert.NotNull(watch.Ticked());
        Assert.Null(watch.Activated()); // The popup is open.
        clock.Now += TimeSpan.FromHours(1);
        Assert.Null(watch.Ticked());

        watch.Keep();
        Assert.Null(watch.Activated()); // Kept today: not offered again today.
        clock.Now += TimeSpan.FromSeconds(30);
        Assert.Null(watch.Ticked());
        clock.Now += TimeSpan.FromHours(7);
        Assert.NotNull(watch.Ticked()); // The next break.
    }

    [Fact]
    public void ANewSessionStartsNow()
    {
        var clock = new Clock();
        var watch = new SessionWatch(clock);
        clock.Now += TimeSpan.FromDays(2);
        Assert.NotNull(watch.Activated());

        watch.NewSession();

        Assert.Equal(clock.Now, watch.Started);
        Assert.Null(watch.Activated());
    }

    [Fact]
    public void TheLogNamesItsInstanceFirstAndInTheExportHeader()
    {
        var clock = new Clock();
        var log = new EventLog(clock);

        log.SessionStarted("0.11.0");
        Assert.Equal("USB-helper 0.11.0 started 2026-11-22 09:30", log.Entries[0].Text);
        Assert.StartsWith("USB-helper 0.11.0 started 2026-11-22 09:30" + Environment.NewLine, log.Export(), StringComparison.Ordinal);

        log.Clear();
        log.SessionStarted("0.11.0", newSession: true);
        Assert.Equal("New session 2026-11-22 09:30 (USB-helper 0.11.0)", Assert.Single(log.Entries).Text);
    }
}
