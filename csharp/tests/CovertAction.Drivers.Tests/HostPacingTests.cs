using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Msc;
using CovertAction.Platform;

namespace CovertAction.Drivers.Tests;

/// <summary>
/// How the host link shows frames and keeps the display's pace (csharp/docs/PERFORMANCE.md): frames on time on a host
/// with a precise clock, the headless host's time as before, and the screen shown when it changes, not on every poll.
/// </summary>
public sealed class HostPacingTests
{
    /// <summary>A host with a precise clock whose waits end exactly at their deadline (as SDL_DelayPrecise does).</summary>
    private sealed class PreciseHost : IHost, IVideo, IInput, IClock
    {
        public List<double> Shown { get; } = [];

        public double Now { get; set; }

        public IVideo Video => this;

        public IInput Input => this;

        public IPcmOutput Audio { get; } = new SilentPcmOutput();

        public IClock Clock => this;

        public bool QuitRequested => false;

        public long Milliseconds => (long)Now;

        public long UnixSeconds => 0;

        public double PreciseMilliseconds => Now;

        public void Sleep(int milliseconds) => Now += milliseconds;

        public void WaitUntil(double deadline) => Now = Math.Max(Now, deadline);

        public void Present(ReadOnlySpan<byte> pixels, ReadOnlySpan<uint> palette) => Shown.Add(Now);

        public void PresentText(TextScreen screen) => Shown.Add(Now);

        public void Pump()
        {
        }

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }
    }

    private static McgaGraphicsDriver Graphics() =>
        new(new FontSet(McgaGraphicsDriverTests.FontFile), new byte[3 * McgaDriverFile.Colours]);

    [Fact]
    public void OnAPreciseClockEveryFrameIsOneFrameOfTheDisplayAfterTheOneBefore()
    {
        var host = new PreciseHost();
        var link = new HostLink(Graphics(), host);
        for (var frame = 0; frame < 701; frame++)
        {
            link.WaitForFrame();
        }
        // 1000 / 70.086 ms apart, not 14 or 15: the frames are due on the display's own grid
        var gaps = host.Shown.Zip(host.Shown.Skip(1), (a, b) => b - a).Skip(1).ToList();
        Assert.All(gaps, gap => Assert.InRange(gap, 14.2681, 14.2683));
        Assert.InRange(host.Now, 10_001.9, 10_002.1);   // 701 waits of 1000/70.086 ms: 10002.0 ms
    }

    [Fact]
    public void AGameBusyForLongerThanAFrameDoesNotCatchUp()
    {
        var host = new PreciseHost();
        var link = new HostLink(Graphics(), host);
        link.WaitForFrame();
        link.WaitForFrame();
        host.Now += 50;                 // the game works for three and a half frames
        var busy = host.Now;
        link.WaitForFrame();            // no wait: the next frame is due one frame from now, not at once again
        Assert.Equal(busy, host.Now);
        link.WaitForFrame();
        Assert.InRange(host.Now - busy, 14.2681, 14.2683);
    }

    [Fact]
    public void TheHeadlessHostKeepsItsWholeMilliseconds()
    {
        // without a precise clock of its own the host's time moves by the whole milliseconds slept, as it always did
        // (the session proof's runs depend on it): the frames are 14 or 15 ms apart, 71 of them 1013 ms
        var host = new HeadlessHost([], unixSeconds: 0);
        var link = new HostLink(Graphics(), host);
        var times = new List<long>();
        for (var frame = 0; frame < 71; frame++)
        {
            times.Add(host.Milliseconds);
            link.WaitForFrame();
        }
        var gaps = times.Zip(times.Skip(1), (a, b) => b - a).Skip(1);
        Assert.All(gaps, gap => Assert.InRange(gap, 14, 15));
        Assert.InRange(host.Milliseconds, 1012, 1013);
    }

    [Fact]
    public void WaitingForAKeyShowsAnUnchangedScreenAtTheDisplaysPaceNotOnEveryPoll()
    {
        // 300 polls without a key, a millisecond each in the loop and one in the host: 600 ms of waiting
        var host = new HeadlessHost([.. Enumerable.Repeat(HeadlessHost.NoKeyYet, 300), HostKeys.Enter], unixSeconds: 0);
        var keyboard = new KeyboardDriver(new HostLink(Graphics(), host));
        Assert.Equal(HostKeys.Enter, keyboard.ReadKeyRaw());
        // shown about 70 times a second (42 in 600 ms), where it was shown on every one of the 300 turns
        Assert.InRange(host.Frames, 35, 45);
    }

    [Fact]
    public void AChangedScreenIsShownAtTheNextPoll()
    {
        var host = new HeadlessHost([HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HostKeys.Enter], unixSeconds: 0);
        var graphics = Graphics();
        var keyboard = new KeyboardDriver(new HostLink(graphics, host));
        Assert.False(keyboard.Kbhit());                 // the first poll shows the screen
        var frames = host.Frames;
        graphics.Pages[0][1234] = 7;                    // the game draws
        Assert.False(keyboard.Kbhit());                 // the next poll shows it, though a frame has not passed
        Assert.Equal(frames + 1, host.Frames);
        Assert.Equal(7, host.Pixels[1234]);
    }

    [Fact]
    public void TheClockTickLoopShowsAnUnchangedScreenAtTheDisplaysPace()
    {
        var host = new HeadlessHost([], unixSeconds: 0);
        var timer = new GameTimer(new HostLink(Graphics(), host));
        for (var call = 0; call < 1000; call++)
        {
            timer.ClockTicks(out _);                    // a millisecond each
        }
        Assert.InRange(host.Frames, 65, 75);            // a second: about 70 frames, not 1000
    }
}
