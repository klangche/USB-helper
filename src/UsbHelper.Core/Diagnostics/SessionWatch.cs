namespace UsbHelper.Core.Diagnostics;

/// <summary>Why the session may be stale (#341).</summary>
public enum SessionBreakKind
{
    /// <summary>The wall clock jumped between two ticks: the computer slept or was suspended.</summary>
    Sleep,

    /// <summary>The session started on an earlier calendar day than today.</summary>
    LongSession,
}

/// <summary>A break in the session (#341), for the "New session" popup.</summary>
/// <param name="Slept">For <see cref="SessionBreakKind.Sleep"/>, how long the clock jumped; otherwise how long the session has run.</param>
/// <param name="Started">When the session started, local time.</param>
public sealed record SessionBreak(SessionBreakKind Kind, TimeSpan Slept, DateTimeOffset Started)
{
    /// <summary>The duration for the log, in English: "12 h", or "45 min" under an hour, or "3 days" from two days.</summary>
    public string Duration => Slept.TotalDays >= 2 ? $"{(int)Slept.TotalDays} days"
        : Slept.TotalHours >= 1 ? $"{(int)Math.Round(Slept.TotalHours)} h"
        : $"{(int)Math.Round(Slept.TotalMinutes)} min";

    /// <summary>The "Keep this session" log line: "Kept the session after sleep (12 h)" or "Kept the session, open since 2026-11-20 09:30 (3 days)".</summary>
    public string KeptText => Kind == SessionBreakKind.Sleep
        ? $"Kept the session after sleep ({Duration})"
        : $"Kept the session, open since {Started.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} ({Duration})";
}

/// <summary>
/// Notices when the app's session has gone stale (#341): the computer slept (the wall clock moved more than
/// <see cref="Jump"/> between two ticks, which come every <see cref="Tick"/>), or the session started on an earlier day
/// when the window is activated. One break gives one popup: while it is open no other is asked for, and after "Keep"
/// the long session is not offered again that day. The clock is a <see cref="TimeProvider"/>, so tests control it.
/// </summary>
public sealed class SessionWatch
{
    /// <summary>How often the app calls <see cref="Ticked"/>.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    /// <summary>A wall-clock move between two ticks longer than this is a sleep.</summary>
    public static readonly TimeSpan Jump = TimeSpan.FromMinutes(5);

    private readonly TimeProvider clock;
    private DateTimeOffset lastTick;
    private DateOnly keptOn;
    private bool open;

    public SessionWatch(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        Started = this.clock.GetLocalNow();
        lastTick = Started;
        keptOn = DateOnly.FromDateTime(Started.DateTime);
    }

    /// <summary>When the session started: at launch, or at the last "New session".</summary>
    public DateTimeOffset Started { get; private set; }

    /// <summary>Call every <see cref="Tick"/>. A break when the clock jumped more than <see cref="Jump"/> and no popup is open.</summary>
    public SessionBreak? Ticked()
    {
        var now = clock.GetLocalNow();
        var moved = now - lastTick;
        lastTick = now;
        return moved > Jump ? Ask(new SessionBreak(SessionBreakKind.Sleep, moved, Started)) : null;
    }

    /// <summary>Call when the main window is activated. A break when the session started on an earlier day, once per day.</summary>
    public SessionBreak? Activated()
    {
        var now = clock.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        return keptOn < today ? Ask(new SessionBreak(SessionBreakKind.LongSession, now - Started, Started)) : null;
    }

    /// <summary>"New session": the session starts now.</summary>
    public void NewSession()
    {
        open = false;
        Started = clock.GetLocalNow();
        lastTick = Started;
        keptOn = DateOnly.FromDateTime(Started.DateTime);
    }

    /// <summary>"Keep this session": nothing changes, and the long session is not offered again today.</summary>
    public void Keep()
    {
        open = false;
        keptOn = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    }

    private SessionBreak? Ask(SessionBreak sessionBreak)
    {
        if (open)
        {
            return null;
        }
        open = true;
        return sessionBreak;
    }
}
