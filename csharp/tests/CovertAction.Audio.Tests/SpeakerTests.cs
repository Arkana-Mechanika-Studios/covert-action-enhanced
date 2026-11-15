using CovertAction.Audio;
using CovertAction.Audio.Synthesis;
using CovertAction.Platform;

namespace CovertAction.Audio.Tests;

/// <summary>The speaker voice and the clock that runs a driver's interrupts along the rendered frames.</summary>
public sealed class SpeakerTests
{
    private const int Rate = 44100;

    private static short[] Render(SpeakerVoice voice, int frames)
    {
        var buffer = new short[2 * frames];
        voice.Render(buffer);
        return buffer;
    }

    /// <summary>Rising zero crossings of the left channel.</summary>
    private static int Cycles(short[] stereo)
    {
        var count = 0;
        for (var i = 2; i < stereo.Length; i += 2)
        {
            if (stereo[i - 2] < 0 && stereo[i] >= 0)
            {
                count++;
            }
        }
        return count;
    }

    [Fact]
    public void ADivisorSoundsAtTheTimersClockDividedByIt()
    {
        var voice = new SpeakerVoice(Rate);
        voice.StartTone();
        voice.SetDivisor(1193); // 1000.15 Hz
        voice.Connect();
        Render(voice, Rate / 10);
        var second = Render(voice, Rate);
        Assert.InRange(Cycles(second), 995, 1005);
    }

    [Fact]
    public void ADisconnectedSpeakerIsSilentAfterItsLastSwing()
    {
        var voice = new SpeakerVoice(Rate);
        voice.StartTone();
        voice.SetDivisor(2000);
        voice.Connect();
        Render(voice, 1000);
        voice.Disconnect();
        var after = Render(voice, Rate);
        Assert.All(after.Skip(after.Length - 2000), s => Assert.InRange((int)s, -2, 2));
    }

    [Fact]
    public void ANewDivisorTakesOverWhileTheToneSounds()
    {
        var voice = new SpeakerVoice(Rate);
        voice.StartTone();
        voice.SetDivisor(2386); // 500 Hz
        voice.Connect();
        Render(voice, Rate / 10);
        voice.SetDivisor(1193); // without a new mode: from the end of the half wave
        Render(voice, 100);
        Assert.InRange(Cycles(Render(voice, Rate)), 995, 1005);
    }

    /// <summary>A program that counts its interrupts and sets the rate it is told.</summary>
    private sealed class CountingProgram(ISoundTimer timer, int divisor, int quietAfter) : ISoundProgram
    {
        public int Interrupts { get; private set; }

        public bool IsQuiet => Interrupts >= quietAfter;

        public void Start() => timer.SetInterruptDivisor(divisor);

        public void Stop() => timer.SetInterruptDivisor(0);

        public bool Play(int command, int parameter, int variant) => command == 28;

        public void TimerInterrupt() => Interrupts++;
    }

    private sealed class Silence : ISoundSynth
    {
        public void Render(Span<short> interleavedStereo) => interleavedStereo.Clear();
    }

    private sealed class InstantClock : IClock
    {
        public long Milliseconds { get; private set; }

        public long UnixSeconds => 0;

        public void Sleep(int milliseconds) => Milliseconds += milliseconds;
    }

    [Fact]
    public void TheInterruptsFallAlongTheFramesAtTheDriversRate()
    {
        CountingProgram program = null!;
        var driver = new ClockedSoundDriver(Rate, new InstantClock(), timer => (program = new CountingProgram(timer, 0x4c90, int.MaxValue), new Silence()));
        var buffer = new short[2 * 1024];
        for (var i = 0; i < Rate / 1024 * 10; i++)
        {
            driver.Render(buffer);
        }
        // ten seconds less a little at 60.8766 Hz
        var seconds = Rate / 1024 * 10 * 1024 / (double)Rate;
        Assert.InRange(program.Interrupts, (int)(seconds * 60.8766) - 1, (int)(seconds * 60.8766) + 1);
        driver.Stop();
        driver.Render(buffer);
        Assert.InRange(program.Interrupts, (int)(seconds * 60.8766) - 1, (int)(seconds * 60.8766) + 1);
    }

    [Fact]
    public void AWaitingCommandMovesTheClockOnWhenNoDeviceAsks()
    {
        CountingProgram program = null!;
        var driver = new ClockedSoundDriver(Rate, new InstantClock(), timer => (program = new CountingProgram(timer, 0x0f88, 30), new Silence()));
        driver.PlaySound(28);
        Assert.True(program.IsQuiet);
        driver.PlaySound(3);
        // the clock moves on in steps of 5 ms (1.5 fast interrupts), as a device would go on asking
        Assert.InRange(program.Interrupts, 30, 32);
    }

    /// <summary>
    /// The session proof's hold on the timer (SESSION-PROOF.md item 8): interrupts given at once, each told to the watcher
    /// with its end, as are the rates the driver sets; none once the driver has given the interrupt back.
    /// </summary>
    [Fact]
    public void InterruptsGivenAtOnceAreToldToTheWatcherUntilTheDriverGivesTheTimerBack()
    {
        CountingProgram program = null!;
        var events = new List<SoundDeviceEvent>();
        var driver = new ClockedSoundDriver(Rate, new InstantClock(), timer => (program = new CountingProgram(timer, 0x4c90, int.MaxValue), new Silence()), events.Add);
        Assert.Equal(3, driver.RunInterrupts(3));
        Assert.Equal(3, program.Interrupts);
        Assert.Equal(
            [new(SoundDeviceEventKind.TimerRate, 0x4c90), .. Enumerable.Repeat<SoundDeviceEvent[]>([new(SoundDeviceEventKind.TimerTick), new(SoundDeviceEventKind.TimerTickEnd)], 3).SelectMany(e => e)],
            events);
        // the frames up to the interrupts count as played: the device's next frames bring the next interrupt in its time
        driver.Render(new short[2 * (Rate * 0x4c90 / 1193182 - 2)]);
        Assert.Equal(3, program.Interrupts);
        driver.Stop();
        Assert.Equal(0, driver.RunInterrupts(2));
        Assert.Equal(new SoundDeviceEvent(SoundDeviceEventKind.TimerRate, 0x10000), events[^1]);
    }
}
