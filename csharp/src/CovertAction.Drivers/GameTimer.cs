using CovertAction.Platform;

namespace CovertAction.Drivers;

/// <summary>The game's time, from the host's clock: the time of day, and frames at the pace of the original's display.</summary>
/// <remarks>New code: the original reads the time from DOS and watches the display's retrace signal.</remarks>
/// <param name="link">The host.</param>
public sealed class GameTimer(HostLink link) : IGameTimer
{
    /// <inheritdoc/>
    public int Time() => (int)link.Host.Clock.UnixSeconds;

    /// <inheritdoc/>
    public void WaitForFrame() => link.WaitForFrame();

    /// <summary>The PC's clock ticks in a day: one every 54.9 ms.</summary>
    private const int TicksPerDay = 0x1800b0;

    private const double MillisecondsPerTick = 86_400_000.0 / TicksPerDay;

    /// <summary>The count as last given out: a smaller one next time means midnight has passed.</summary>
    private int _lastTicks;

    /// <inheritdoc/>
    /// <remarks>
    /// New code: the ticks are counted from the host's clock (from the moment the host started, not from the real
    /// midnight: the game only measures spans with them). Each call shows the screen (when the game changed it, else at the
    /// display's pace: showing it on every call handed the window a frame each millisecond, csharp/docs/PERFORMANCE.md)
    /// and lets a millisecond pass, so
    /// that a loop waiting for a tick neither freezes the window nor waits for ever on a host whose time only moves
    /// when the game sleeps.
    /// </remarks>
    public bool ClockTicks(out int ticks)
    {
        link.PresentIfChangedOrDue();
        link.Pump();
        link.Host.Clock.Sleep(1);
        ticks = (int)(link.Host.Clock.Milliseconds / MillisecondsPerTick % TicksPerDay);
        var midnight = ticks < _lastTicks;
        _lastTicks = ticks;
        return midnight;
    }
}
