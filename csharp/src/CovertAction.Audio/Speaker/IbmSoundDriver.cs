using CovertAction.Content;

namespace CovertAction.Audio.Speaker;

/// <summary>
/// The original's PC-speaker sound driver (ISOUND.CA, "CovertActIBM10-12-90") as C#: its 47 commands and what it does
/// on each timer interrupt. Its sounds are read from the player's driver file (<see cref="SpeakerDriverFile"/>);
/// which command plays which sound, and how a note sounds, is the driver's code, here.
/// </summary>
/// <remarks>
/// <para>
/// The speaker has one tone, and the driver two kinds of sound for it. A <b>noise sound</b> is a list of notes whose
/// divisor gets random bits added (a noise mask of 0x77 makes a rough tone, of 1 a clean one) and glides by a step
/// each tick; it runs the timer fast (300 interrupts a second) and changes the random bits on the four interrupts
/// between two ticks. A <b>sweep sound</b> is a list of notes that trill between two divisors on those same
/// interrupts and move both a step each tick. A tick is 60 a second either way: every interrupt of the slow timer
/// (60.9 Hz), every fifth of the fast one (60.0 Hz).
/// </para>
/// <para>
/// Each note carries a priority. A new sound starts only if its first note's priority is not below the priority of
/// the note playing; the two kinds share the speaker, so a sound of either kind ends the other. When a sound has
/// played through it is repeated as often as its command says, from where the command says; then the driver goes
/// quiet and back to the slow timer, unless the teleprinter's ticking (command 31) is on, which plays its click
/// again, one of two chosen at random, until another command comes.
/// </para>
/// <para>
/// The two kinds share some of the driver's variables (the step, the noise mask that is also the trill's swing);
/// the C# keeps them shared, because a sound that starts while the other kind's notes are under way finds what the
/// other left there, as in the original. Words wrap as the original's 16-bit words do.
/// </para>
/// </remarks>
public sealed class IbmSoundDriver : ISoundProgram
{
    /// <summary>The slow timer's divisor: 60.88 interrupts a second, one tick each.</summary>
    public const int SlowDivisor = 0x4c90;

    /// <summary>The fast timer's divisor: 300.10 interrupts a second, a tick on every fifth.</summary>
    public const int FastDivisor = 0x0f88;

    private const int InterruptsPerFastTick = 5;
    private const int SlowInterruptsPerBiosTick = 3;
    private const int Commands = 47;

    // where the driver's own sounds and tables are in its data segment
    private const int NoSound = 0x450;
    private const int TeleprinterClick = 0x432;
    private const int TeleprinterVariants = 0x466;
    private const int TakeOffRepeat = 0x072;

    private enum TimerMode
    {
        /// <summary>The PC's own timer handler: the driver gets no interrupts (before its start, after its end).</summary>
        None,
        Slow,
        Fast,
    }

    /// <summary>Where a voice is in a sound: the sound and the number of its next note (the count: at its end).</summary>
    private readonly record struct Place<TNote>(SpeakerSound<TNote> Sound, int Index)
    {
        public bool AtEnd => Index >= Sound.Notes.Count;

        public TNote Note => Sound.Notes[Index];

        public Place<TNote> Next => this with { Index = Index + 1 };
    }

    private readonly SpeakerDriverFile _file;
    private readonly ISpeaker _speaker;
    private readonly ISoundTimer _timer;
    private readonly (ushort Mask, ushort Pause)[] _teleprinterVariants;
    private readonly Place<NoiseNote> _noNoise;
    private readonly Place<SweepNote> _noSweep;

    // orig: ISOUND DS:0fc3 (the timer mode), DS:0f92 (whether the PC's handler was saved), DS:0f91 (the interrupt countdown)
    private TimerMode _timerMode;
    private bool _handlerSaved;
    private byte _countdown;

    // orig: ISOUND DS:0fc9 (ticks on: off while paused), DS:0fca (the fast interrupts' work on: 0ffh in the driver file)
    private bool _ticking;
    private bool _fastStepsOn = true;

    // orig: ISOUND DS:0f93 (the priority of the note playing)
    private ushort _priority;

    // the noise voice. orig: ISOUND DS:0f9b (next note), DS:0f99 (where a repeat starts), DS:0f95 (repeats left), DS:0fa7 (ticks left of the note)
    private Place<NoiseNote> _noiseNext;
    private Place<NoiseNote> _noiseRestart;
    private ushort _noiseRepeats;
    private ushort _noiseTicks;

    // the sweep voice. orig: ISOUND DS:0fa3 (next note), DS:0fa1 (where a repeat starts), DS:0f9d (repeats left), DS:0fa5 (ticks left), DS:0fa9 (its divisor)
    private Place<SweepNote> _sweepNext;
    private Place<SweepNote> _sweepRestart;
    private ushort _sweepRepeats;
    private ushort _sweepTicks;
    private ushort _sweepDivisor;

    // shared. orig: ISOUND DS:0fab (the noise note's divisor), DS:0fad (noise mask, or the trill's swing), DS:0faf (the step per tick),
    // DS:0fb1 (fast interrupts per trill change; 0: no trill), DS:0fb3 (its countdown), DS:0fc6 (the random number)
    private ushort _noiseDivisor;
    private ushort _spread;
    private ushort _glide;
    private ushort _trillPeriod;
    private short _trillCountdown;
    private ushort _random;

    // orig: ISOUND DS:0fbd (teleprinter ticking on), CS:01a6 (ticks counted), DS:0fbb (its random number), DS:0fb9 (the click chosen), DS:0fb5 and DS:0fb7 (the gears of the two engines)
    private int _tickCount;
    private bool _teleprinterTicking;
    private ushort _teleprinterRandom;
    private int _gearCar1, _gearCar2;

    /// <summary>Makes the driver from the player's ISOUND.CA, playing on <paramref name="speaker"/>.</summary>
    /// <param name="file">The driver file's sounds.</param>
    /// <param name="speaker">The speaker.</param>
    /// <param name="timer">The timer interrupt's rate.</param>
    /// <exception cref="InvalidDataException">The file lacks a sound the commands play.</exception>
    public IbmSoundDriver(SpeakerDriverFile file, ISpeaker speaker, ISoundTimer timer)
    {
        _file = file;
        _speaker = speaker;
        _timer = timer;
        _teleprinterVariants = [(file.Word(TeleprinterVariants), file.Word(TeleprinterVariants + 2)),
            (file.Word(TeleprinterVariants + 4), file.Word(TeleprinterVariants + 6))];
        _noNoise = new(file.NoiseSound(NoSound), 0);
        _noSweep = new(file.SweepSound(NoSound), 0);
        _noiseNext = _noiseRestart = _noNoise;
        _sweepNext = _sweepRestart = _noSweep;
        // every sound a command plays, read once: a missing one fails here, not in the middle of the game
        for (var command = 0; command < Commands; command++)
        {
            _ = SoundOf(command);
        }
    }

    /// <summary>The driver on a speaker voice of its own, as the game's sound driver on a device of <paramref name="sampleRate"/>, started.</summary>
    /// <param name="file">The driver file's sounds.</param>
    /// <param name="sampleRate">The device's frames per second.</param>
    /// <param name="clock">Real time, for the command that waits for its sound to end.</param>
    /// <param name="watch">Told of everything done to the speaker and every rate and interrupt (for tests and tools); null: nothing is told.</param>
    public static ClockedSoundDriver OnSpeaker(SpeakerDriverFile file, int sampleRate, CovertAction.Platform.IClock clock, Action<SoundDeviceEvent>? watch = null) =>
        new(sampleRate, clock, timer =>
        {
            var voice = new Synthesis.SpeakerVoice(sampleRate);
            return (new IbmSoundDriver(file, WatchedDevice.Speaker(voice, watch), timer), voice);
        }, watch);

    /// <summary>Ticks since the driver started (the driver's slot 105 tells the game; no part of it asks).</summary>
    // orig: ISOUND 0000:0126 read_tick_count driver; tests: IbmSoundOracleTests
    public int ReadTickCount() => _tickCount;

    /// <inheritdoc/>
    public bool IsQuiet => _noiseRepeats == 0 && _sweepRepeats == 0 && _noiseTicks == 0 && _sweepTicks == 0;

    /// <inheritdoc/>
    // orig: ISOUND 0000:00a6 init_driver driver; also ISOUND 0000:0202, ISOUND 0000:0687; tests: IbmSoundOracleTests
    public void Start()
    {
        Silence();
        _handlerSaved = true;
        UseSlowTimer();
        _ticking = true;
    }

    /// <inheritdoc/>
    // orig: ISOUND 0000:00bc shutdown_driver driver; also ISOUND 0000:06ae; tests: IbmSoundOracleTests
    public void Stop()
    {
        Silence();
        if (!_handlerSaved)
        {
            return;
        }
        // the PC's own handler back, and its rate (divisor 0: 65,536)
        _handlerSaved = false;
        _timerMode = TimerMode.None;
        _timer.SetInterruptDivisor(0);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">A negative command: the original calls through a word in front of its table.</exception>
    // orig: ISOUND 0000:00ca play_sound driver; tests: IbmSoundOracleTests
    public bool Play(int command, int parameter, int variant)
    {
        // any command but the teleprinter's ends its ticking (after the click under way)
        _teleprinterTicking = command == 31;
        if (command == 26)
        {
            SetEngineGear(ref _gearCar1, ref _gearCar2, parameter);
        }
        else if (command == 27)
        {
            SetEngineGear(ref _gearCar2, ref _gearCar1, parameter);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(command);
        if (command >= Commands)
        {
            return false;
        }
        return Carry(command);
    }

    /// <summary>
    /// The command table (driver words 0048h-00a5h): the routine each command jumps to, given as the sound it starts.
    /// The numbers after each case are the routine's address in the driver.
    /// </summary>
    private bool Carry(int command)
    {
        switch (command)
        {
            case 0 or 1: Silence(); break;                                    // 020b: stop (1: the menus' and scenes' end)
            case 2: StartNoiseSound(Noise(0x46e), repeats: 4); break;         // 0478: the theme, five times through
            case 6: StartNoiseSound(Noise(0x242), repeats: 7); break;         // 04d6: the Uzi, a burst of eight
            case 22: StartNoiseSound(Noise(0x330), repeats: 2); break;        // 055b: first gear
            case 35:                                                          // 0493: take-off, its drone held 255 times more
                StartNoiseSound(Noise(0x068), repeats: 0xff);
                _noiseRestart = new(Noise(0x068), Noise(0x068).NoteAt(TakeOffRepeat));
                break;
            case 28:                                                          // 057c: the workbench's swap, waited for
                StartNoiseSound(Noise(0x3be), repeats: 0);
                return true;
            case 26 or 27: break;                                             // 057a, 057b: the engines (see SetEngineGear)
            case 37: Pause(); break;                                          // 045f
            case 38: Resume(); break;                                         // 046c
            case >= 39 and <= 44: break;                                      // 025f: nothing on the speaker
            default:
                switch (SoundOf(command))
                {
                    case SpeakerSound<NoiseNote> noise: StartNoiseSound(noise, repeats: 0); break;
                    case SpeakerSound<SweepNote> sweep: StartSweepSound(sweep); break;
                }
                break;
        }
        return false;
    }

    /// <summary>The sound a command starts (null for those that start none); the others are noise sounds played once.</summary>
    private object? SoundOf(int command) => command switch
    {
        2 => Noise(0x46e), 6 => Noise(0x242), 22 => Noise(0x330), 35 => Noise(0x068), 28 => Noise(0x3be),
        3 => Noise(0x08a), 4 => Noise(0x0a6), 7 => Noise(0x24e), 8 => Noise(0x25a), 9 => Noise(0x266),
        10 => Noise(0x282), 11 => Noise(0x2ac), 12 => Noise(0x2b8), 16 => Noise(0x2e8), 17 => Noise(0x2f4),
        21 => Noise(0x324), 23 => Noise(0x33c), 24 => Noise(0x348), 25 => Noise(0x39a), 29 => Noise(0x3ca),
        31 => Noise(TeleprinterClick), 32 => Noise(0x444), 33 => Noise(0x05c), 34 => Noise(0x050), 36 => Noise(0x07e),
        46 => Noise(0x1aa),
        // the sweeps: 04bb, 050a, 0514, 051e, 0536, 0540, 054a, 058d, 04c5
        5 => Sweep(0x0c2), 13 => Sweep(0x2c4), 14 => Sweep(0x2d0), 15 => Sweep(0x2dc), 18 => Sweep(0x300),
        19 => Sweep(0x30c), 20 => Sweep(0x318), 30 => Sweep(0x426), 45 => Sweep(0x0ce),
        _ => null,
    };

    private SpeakerSound<NoiseNote> Noise(int offset) => _file.NoiseSound(offset);

    private SpeakerSound<SweepNote> Sweep(int offset) => _file.SweepSound(offset);

    /// <summary>
    /// The engines' commands only take a gear: they put the gear's two values into two sounds of the driver's data
    /// (DS:03a6, DS:03b2) that no command of this driver plays, and play nothing. The car chase is silent on the
    /// speaker but for its gear changes and turns.
    /// </summary>
    // orig: ISOUND 0000:0167 set_engine_gear_car1 driver; also ISOUND 0000:0186; tests: IbmSoundOracleTests
    private static void SetEngineGear(ref int gear, ref int otherGear, int parameter)
    {
        gear = parameter;
        otherGear = 0;
    }

    /// <summary>Everything stops: both voices, their repeats, the trill, and the speaker is disconnected. The timer stays as it is.</summary>
    // orig: ISOUND 0000:020b silence driver; also ISOUND 0000:0259; tests: IbmSoundOracleTests
    private void Silence()
    {
        _gearCar1 = _gearCar2 = 0;
        _sweepDivisor = _glide = 0;
        _sweepTicks = _noiseTicks = 0;
        _priority = 0;
        _noiseRepeats = _sweepRepeats = 0;
        _trillPeriod = 0;
        _trillCountdown = 0;
        _noiseNext = _noNoise;
        _sweepNext = _noSweep;
        _speaker.Disconnect();
    }

    // orig: ISOUND 0000:045f pause_sound driver; tests: IbmSoundOracleTests
    private void Pause()
    {
        _ticking = false;
        _fastStepsOn = false;
        _speaker.Disconnect();
    }

    /// <summary>Ticks again, and the speaker is connected again, whatever plays (the next quiet tick disconnects it).</summary>
    // orig: ISOUND 0000:046c resume_sound driver; tests: IbmSoundOracleTests
    private void Resume()
    {
        _ticking = true;
        _fastStepsOn = true;
        _speaker.Connect();
    }

    /// <summary>Starts a noise sound, unless the note playing has a higher priority than its first.</summary>
    // orig: ISOUND 0000:03ed start_noise_sound driver; also ISOUND 0000:03ef; tests: IbmSoundOracleTests
    private void StartNoiseSound(SpeakerSound<NoiseNote> sound, int repeats)
    {
        // the first note's priority: the word the sound starts with (0 for an empty sound)
        if (_priority > _file.Word(sound.Offset))
        {
            return;
        }
        _trillPeriod = 0;
        _sweepTicks = 0;
        _sweepRepeats = 0;
        _noiseRepeats = (ushort)repeats;
        _sweepRestart = _noSweep;
        _noiseRestart = new(sound, 0);
        StartNoiseNote(new(sound, 0));
    }

    /// <summary>Starts a sweep sound with a trill on every fast interrupt, unless the note playing has a higher priority.</summary>
    // orig: ISOUND 0000:0374 start_sweep_sound driver; tests: IbmSoundOracleTests
    private void StartSweepSound(SpeakerSound<SweepNote> sound)
    {
        if (_priority > _file.Word(sound.Offset))
        {
            return;
        }
        _trillPeriod = 1;
        _noiseTicks = 0;
        _noiseRepeats = 0;
        _sweepRepeats = 0;
        _noiseRestart = _noNoise;
        _sweepRestart = new(sound, 0);
        StartSweepNote(new(sound, 0));
    }

    /// <summary>
    /// The noise voice's next note: a rest disconnects the speaker; a note connects it with the tone channel waiting
    /// for its divisor, which the next fast interrupt or tick gives, and switches to the fast timer.
    /// </summary>
    // orig: ISOUND 0000:0418 start_noise_note driver; tests: IbmSoundOracleTests
    private void StartNoiseNote(Place<NoiseNote> place)
    {
        var note = place.Note;
        _random = 0;
        _priority = note.Priority;
        _noiseTicks = note.Duration;
        _spread = note.NoiseMask;
        _noiseNext = place.Next;
        if (note.IsRest)
        {
            _speaker.Disconnect();
            return;
        }
        _noiseDivisor = note.Divisor;
        _glide = note.Glide;
        UseFastTimer();
        SpeakerOn();
    }

    /// <summary>The sweep voice's next note: a rest disconnects the speaker; a note sounds at once at its start divisor.</summary>
    // orig: ISOUND 0000:03a6 start_sweep_note driver; tests: IbmSoundOracleTests
    private void StartSweepNote(Place<SweepNote> place)
    {
        var note = place.Note;
        _priority = note.Priority;
        _sweepTicks = note.Duration;
        _sweepDivisor = note.Start;
        _sweepNext = place.Next;
        if (note.IsRest)
        {
            _speaker.Disconnect();
            return;
        }
        _spread = (ushort)(note.Start - note.OtherEnd);
        _glide = note.Glide;
        SpeakerOn();
        _speaker.SetDivisor(_sweepDivisor);
        if (_trillPeriod != 0)
        {
            UseFastTimer();
        }
    }

    // orig: ISOUND 0000:0454 speaker_on driver; tests: IbmSoundOracleTests
    private void SpeakerOn()
    {
        _speaker.StartTone();
        _speaker.Connect();
    }

    /// <summary>
    /// The timer interrupt: the driver's handler for the rate it set. The original also passes every third slow or
    /// every sixteenth fast interrupt on to the PC's own handler, which keeps DOS's clock; the C# has no such clock.
    /// </summary>
    /// <inheritdoc/>
    // orig: ISOUND 0000:05fc slow_timer_interrupt driver; also ISOUND 0000:05c2; tests: IbmSoundOracleTests
    public void TimerInterrupt()
    {
        // DIVERGENCE: D-19 nothing is passed on to a BIOS clock.
        switch (_timerMode)
        {
            case TimerMode.Slow:
                Tick();
                if (--_countdown == 0)
                {
                    _countdown = SlowInterruptsPerBiosTick;
                }
                break;
            case TimerMode.Fast:
                // the countdown is shared: the first tick after a switch from the slow timer comes after what was left of it
                if (--_countdown == 0)
                {
                    _countdown = InterruptsPerFastTick;
                    Tick();
                }
                else
                {
                    FastStep();
                }
                break;
        }
    }

    // orig: ISOUND 0000:0623 use_fast_timer driver; tests: IbmSoundOracleTests
    private void UseFastTimer()
    {
        if (_timerMode == TimerMode.Fast)
        {
            return;
        }
        _timerMode = TimerMode.Fast;
        _timer.SetInterruptDivisor(FastDivisor);
    }

    // orig: ISOUND 0000:0655 use_slow_timer driver; tests: IbmSoundOracleTests
    private void UseSlowTimer()
    {
        if (_timerMode == TimerMode.Slow)
        {
            return;
        }
        _timerMode = TimerMode.Slow;
        _countdown = SlowInterruptsPerBiosTick;
        _timer.SetInterruptDivisor(SlowDivisor);
    }

    /// <summary>
    /// The work of a fast interrupt between two ticks: a sweep note's trill swaps to its other tone; otherwise the
    /// noise note gets new random bits. This runs while the fast timer is on, whatever plays.
    /// </summary>
    // orig: ISOUND 0000:01d5 fast_step driver; also ISOUND 0000:01a8; tests: IbmSoundOracleTests
    private void FastStep()
    {
        if (!_fastStepsOn)
        {
            return;
        }
        if (_trillPeriod == 0)
        {
            Noise();
            return;
        }
        if (_sweepDivisor == 0 || --_trillCountdown > 0)
        {
            return;
        }
        _trillCountdown = (short)_trillPeriod;
        _spread = (ushort)-_spread;
        _sweepDivisor += _spread;
        _speaker.SetDivisor(_sweepDivisor);
    }

    /// <summary>The noise note's tone with new random bits: the random number steps on and is masked onto the divisor.</summary>
    private void Noise()
    {
        _random = RotateRight3((ushort)(_random + 0x9248));
        _speaker.SetDivisor((ushort)((_random & _spread) + _noiseDivisor));
    }

    private static ushort RotateRight3(ushort value) => (ushort)((value >> 3) | (value << 13));

    /// <summary>A tick (60 a second): the voice that plays counts down its note and moves its tone on.</summary>
    // orig: ISOUND 0000:0263 timer_tick driver; tests: IbmSoundOracleTests
    private void Tick()
    {
        _tickCount++;
        if (!_ticking)
        {
            return;
        }
        if (_sweepTicks != 0 || _sweepRepeats != 0)
        {
            SweepTick();
            return;
        }
        if (_noiseTicks == 0 && _noiseRepeats == 0)
        {
            EndOfSounds();
            return;
        }
        if (--_noiseTicks == 0)
        {
            if (!_noiseNext.AtEnd)
            {
                StartNoiseNote(_noiseNext);
            }
            else if (_noiseRepeats != 0)
            {
                _noiseRepeats--;
                StartNoiseNote(_noiseRestart);
            }
            else
            {
                EndOfSounds();
            }
            return;
        }
        if (_spread == 0)
        {
            return;
        }
        // the last tick of a sound's last note: the tone drops to the lowest divisor less the random bits
        if (_noiseTicks == 1 && _noiseNext.AtEnd && _noiseRepeats == 0)
        {
            _glide = 0;
            _random = 0;
            _noiseDivisor = 0xffff;
            _spread = 0xffff;
        }
        _noiseDivisor += _glide;
        Noise();
    }

    /// <summary>The sweep voice's tick: its note counts down; a sounding note moves by its step and its tone starts again.</summary>
    // orig: ISOUND 0000:0321 sweep_tick driver; tests: IbmSoundOracleTests
    private void SweepTick()
    {
        if (--_sweepTicks == 0)
        {
            if (!_sweepNext.AtEnd)
            {
                StartSweepNote(_sweepNext);
            }
            else if (_sweepRepeats != 0)
            {
                _sweepRepeats--;
                StartSweepNote(_sweepRestart);
            }
            else
            {
                _trillPeriod = 0;
                EndOfSounds();
            }
            return;
        }
        if (_sweepDivisor == 0)
        {
            return;
        }
        _sweepDivisor += _glide;
        SpeakerOn();
        _speaker.SetDivisor(_sweepDivisor);
    }

    /// <summary>Nothing more to play: the teleprinter clicks again if it ticks, else all stops and the timer goes slow.</summary>
    // orig: ISOUND 0000:030a end_of_sounds driver; tests: IbmSoundOracleTests
    private void EndOfSounds()
    {
        if (_teleprinterTicking)
        {
            ChooseTeleprinterClick();
            StartNoiseNote(new(Noise(TeleprinterClick), 0));
            return;
        }
        Silence();
        UseSlowTimer();
    }

    /// <summary>One of the teleprinter's two clicks at random: its noise mask and the length of the pause after it.</summary>
    // orig: ISOUND 0000:012e choose_teleprinter_click driver; tests: IbmSoundOracleTests
    private void ChooseTeleprinterClick()
    {
        _teleprinterRandom = RotateRight3((ushort)(_teleprinterRandom + 0x4567));
        var (mask, pause) = _teleprinterVariants[(_teleprinterRandom & 4) >> 2];
        var click = Noise(TeleprinterClick);
        click.Notes[0].NoiseMask = mask;
        click.Notes[1].Duration = pause;
    }
}
