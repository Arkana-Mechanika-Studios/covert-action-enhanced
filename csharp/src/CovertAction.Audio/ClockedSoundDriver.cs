using CovertAction.Platform;

namespace CovertAction.Audio;

/// <summary>
/// A reconstructed sound driver as the game's <see cref="ISoundDriver"/>: the sound device's clock is the PC timer.
/// As the device asks for frames, the timer's cycles are counted along them and the driver's interrupt runs where
/// each falls, between the frames its synthesiser renders; so tunes go on while the game waits or works, at the
/// rate the driver set. A command of the game is carried out at once, at the point the frames have reached.
/// </summary>
/// <remarks>
/// New code: on the PC the timer interrupts the game whatever it does. Here the device's thread renders; the game's
/// commands and the rendering take turns under one lock. A command after which the original driver waits until all
/// is quiet makes the caller wait the same way. When the host's device does not ask for frames (a host without
/// sound), the waiting caller moves the clock on itself, so that the wait ends.
/// </remarks>
public sealed class ClockedSoundDriver : ISoundDriver, IPcmSource, ISoundTimer
{
    private const int TimerClock = 1193182;

    /// <summary>How long the device may leave the clock alone before a waiting caller moves it on itself.</summary>
    private const int DeviceSilenceMilliseconds = 100;

    private const int WaitStepMilliseconds = 5;

    private readonly object _lock = new();
    private readonly int _sampleRate;
    private readonly IClock _clock;
    private readonly ISoundProgram _program;
    private readonly ISoundSynth _synth;
    private readonly short[] _unheard;
    private long _frame;
    private long _now;
    private long _nextInterrupt = long.MaxValue;
    private int _divisor;
    private long _lastRender;
    private readonly Action<SoundDeviceEvent>? _watch;

    /// <summary>Makes the driver and starts it.</summary>
    /// <param name="sampleRate">The device's frames per second.</param>
    /// <param name="clock">Real time, for a caller that waits.</param>
    /// <param name="make">Makes the driver's logic and its synthesiser, given the timer it sets its rate on.</param>
    /// <param name="watch">Told of every rate the driver sets and every interrupt it gets (for tests and tools); null: nothing is told.</param>
    public ClockedSoundDriver(int sampleRate, IClock clock, Func<ISoundTimer, (ISoundProgram Program, ISoundSynth Synth)> make, Action<SoundDeviceEvent>? watch = null)
    {
        _sampleRate = sampleRate;
        _watch = watch;
        _clock = clock;
        _unheard = new short[2 * Math.Max(1, sampleRate * WaitStepMilliseconds / 1000)];
        (_program, _synth) = make(this);
        lock (_lock)
        {
            _program.Start();
        }
    }

    /// <inheritdoc/>
    public void SetInterruptDivisor(int divisor)
    {
        _watch?.Invoke(new(SoundDeviceEventKind.TimerRate, divisor == 0 ? 0x10000 : divisor));
        _divisor = divisor;
        _nextInterrupt = divisor == 0 ? long.MaxValue : _now + divisor;
    }

    /// <inheritdoc/>
    public void PlaySound(int effect) => PlaySound(effect, 0, 0);

    /// <inheritdoc/>
    public void PlaySound(int effect, int parameter) => PlaySound(effect, parameter, 0);

    /// <inheritdoc/>
    public void PlaySound(int effect, int mode, int variant)
    {
        bool waits;
        lock (_lock)
        {
            _now = Math.Max(_now, Cycle(_frame));
            waits = _program.Play(effect, mode, variant);
        }
        if (waits)
        {
            WaitUntilQuiet();
        }
    }
    /// <summary>The driver's end: the launcher's last call.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _program.Stop();
        }
    }

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        lock (_lock)
        {
            _lastRender = _clock.Milliseconds;
            RenderLocked(interleavedStereo);
        }
    }

    private void RenderLocked(Span<short> buffer)
    {
        // DIVERGENCE: D-19 the timer interrupts between the device's frames, never inside a command.
        var frames = buffer.Length / 2;
        var done = 0;
        while (done < frames)
        {
            // the first frame that starts at or after the next interrupt
            var due = _nextInterrupt == long.MaxValue ? long.MaxValue : (_nextInterrupt * _sampleRate + TimerClock - 1) / TimerClock;
            if (due <= _frame)
            {
                Interrupt();
                continue;
            }
            var chunk = (int)Math.Min(frames - done, due - _frame);
            _synth.Render(buffer.Slice(2 * done, 2 * chunk));
            _frame += chunk;
            done += chunk;
            _now = Math.Max(_now, Cycle(_frame));
        }
    }

    private long Cycle(long frame) => frame * TimerClock / _sampleRate;

    /// <summary>The next interrupt, at its cycle.</summary>
    private void Interrupt()
    {
        _now = _nextInterrupt;
        _nextInterrupt = _now + _divisor;
        _watch?.Invoke(new(SoundDeviceEventKind.TimerTick));
        _program.TimerInterrupt();
        _watch?.Invoke(new(SoundDeviceEventKind.TimerTickEnd));
    }

    /// <summary>
    /// Runs the next timer interrupts now, at the rate the driver set, as if the device had asked for the frames up to
    /// them (the frames are not rendered): a tool without a sound device gives the driver the interrupts the original
    /// got (the session proof, csharp/docs/SESSION-PROOF.md item 8; for tests and tools, new code).
    /// </summary>
    /// <param name="count">How many interrupts.</param>
    /// <returns>How many ran: fewer when the driver has given the interrupt back (no rate set).</returns>
    public int RunInterrupts(int count)
    {
        lock (_lock)
        {
            var run = 0;
            for (; run < count && _nextInterrupt != long.MaxValue; run++)
            {
                Interrupt();
                // the frames up to the interrupt count as played
                _frame = Math.Max(_frame, _now * _sampleRate / TimerClock);
            }
            return run;
        }
    }

    /// <summary>The caller waits until nothing plays; the interrupts go on meanwhile (on the device's thread, or here).</summary>
    // orig: ISOUND 0000:05a5 wait_until_quiet driver; tests: SpeakerTests, IbmSoundOracleTests
    private void WaitUntilQuiet()
    {
        while (true)
        {
            lock (_lock)
            {
                if (_program.IsQuiet)
                {
                    return;
                }
                if (_clock.Milliseconds - _lastRender > DeviceSilenceMilliseconds)
                {
                    RenderLocked(_unheard);
                    continue;
                }
            }
            _clock.Sleep(WaitStepMilliseconds);
        }
    }
}
