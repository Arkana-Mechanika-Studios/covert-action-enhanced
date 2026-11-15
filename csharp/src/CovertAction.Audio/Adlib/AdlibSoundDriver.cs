using CovertAction.Content;

namespace CovertAction.Audio.Adlib;

/// <summary>
/// The original's AdLib sound driver (ASOUND.CA, "Covert Adlib10-18-90") as C#: its 47 commands and what it does on
/// each timer interrupt, writing the card's OPL2 registers. Its instruments, note table and tunes are read from the
/// player's driver file (<see cref="AdlibDriverFile"/>); which command plays which tune, and how a tune is played, is
/// the driver's code, here.
/// </summary>
/// <remarks>
/// <para>
/// Seven <b>voices</b> play tunes, voice n on the card's channel n. A tune is a byte stream in the driver's data: a
/// note is two bytes (its number, 12 a octave, and its length in ticks; note 0 is a rest, length 0 ends the tune and
/// the voice), and the bytes F7h-FFh are events (<see cref="TuneEvent"/>): the instrument, the volume and its slide,
/// a pitch bend, a detune, an early release, two levels of repeats and the tune's end, which plays it again (or
/// another). A tick is 60 a second: every interrupt of the slow timer (60.9 Hz), every fifth of the fast one.
/// </para>
/// <para>
/// An instrument may have a <b>noise sweep</b>: its note then also starts one of two noise voices, which give the
/// note's channel a frequency with new random bits on every interrupt and move it a step each tick; the driver runs
/// the fast timer (300 interrupts a second) while one of them plays. The <b>engines</b> of the car chase and the
/// aeroplane glide channel 8 (and 7) from gear to gear; the raid's <b>gunfire</b> (command 44) rewrites the length of
/// a shot and the instrument in four rhythms of the driver's data as it plays, on voices 0-2.
/// </para>
/// <para>
/// The driver keeps a copy of every register it writes and builds the next value on it, so the C# does the same.
/// Bytes and words wrap as the original's do. Three routines of the driver are never called and are left out
/// (0138, 0214, 0d8e), and so is its eighth voice (DS:23d8), which is cleared but never played.
/// </para>
/// </remarks>
public sealed class AdlibSoundDriver : ISoundProgram
{
    /// <summary>The slow timer's divisor: 60.88 interrupts a second, one tick each.</summary>
    public const int SlowDivisor = 0x4c90;

    /// <summary>The fast timer's divisor: 300.10 interrupts a second, a tick on every fifth.</summary>
    public const int FastDivisor = 0x0f88;

    private const int Commands = 47;
    private const int Voices = 7;
    private const int InterruptsPerFastTick = 5;
    private const int SlowInterruptsPerBiosTick = 3;
    private const int DetectionReads = 200;

    // the tune that ends the others: a silent slide, no bend, and the end (DS:0788)
    private const ushort Ending = 0x0788;

    // instruments the driver's code chooses (the tunes choose the others)
    private const int Car1Engine = 0x12, Car2Engine = 0x0b, PlaneEngine = 0x19, PlaneWhine = 0x29;
    private const byte SingleShots = 0x21, Barrage = 0x2f;

    // the engines: their channels and frequencies (block and F-number)
    private const int EngineChannel = 8, WhineChannel = 7;
    private const ushort TakeOffFrequency = 0x1ff0, LandedFrequency = 0x1da0, WhineFrequency = 0x1a85;
    private const ushort StopBelowFrequency = 0x1da1;
    private const short GlideStep = 4;
    private const int GearFrequencies = 0x2295;

    // the gunfire: its tunes and the bytes of them it rewrites, and its tables of rhythms
    private const ushort Shot = 0x0f18, ShotLength = 0x0f19, ShotFollowUp = 0x12c6, Rhythm = 0x1332;
    private static readonly int[] RhythmInstrumentBytes = [0x1335, 0x134d, 0x1365, 0x137d];
    private const int RhythmVariants = 0x2305, SparsePatterns = 0x22a5, HeavyPatterns = 0x22b5;

    // DS:2026 the noise's random number, DS:2283 the gunfire's (their first values are the file's)
    private const int NoiseRandomSeed = 0x2026, GunfireRandomSeed = 0x2283;

    // DS:2042-2044 the chip's deep tremolo, deep vibrato and note-select bits, as the file sets them
    private const int DeepTremoloFlag = 0x2042, DeepVibratoFlag = 0x2043, NoteSelectFlag = 0x2044;

    /// <summary>The bytes F7h-FFh of a tune: events, each with its operands; any other byte starts a note.</summary>
    private enum TuneEvent : byte
    {
        /// <summary>F7h d: the notes are detuned by d (signed) F-number steps.</summary>
        Detune = 0xf7,

        /// <summary>F8h p s: every p ticks the volume moves by s (0: it stays).</summary>
        VolumeSlide = 0xf8,

        /// <summary>F9h v: the volume (63 the loudest).</summary>
        Volume = 0xf9,

        /// <summary>FAh b: every tick the frequency moves by b (signed).</summary>
        Bend = 0xfa,

        /// <summary>FBh t: a note is released t ticks before its end.</summary>
        EarlyRelease = 0xfb,

        /// <summary>FCh i: the instrument.</summary>
        Instrument = 0xfc,

        /// <summary>FDh: the end; the tune plays again from its start (or from the tune that follows it).</summary>
        End = 0xfd,

        /// <summary>FEh 0 marks where an outer repeat starts; FEh n goes back there n times.</summary>
        OuterRepeat = 0xfe,

        /// <summary>FFh 0 marks where an inner repeat starts; FFh n goes back there n times.</summary>
        InnerRepeat = 0xff,
    }

    private enum TimerMode
    {
        /// <summary>The PC's own timer handler: the driver gets no interrupts (before its start, after its end).</summary>
        None,
        Slow,
        Fast,
    }

    private enum Engine
    {
        None,
        Car1,
        Car2,
    }

    private readonly AdlibDriverFile _file;
    private readonly IOplChip _chip;
    private readonly ISoundTimer _timer;
    private readonly Voice[] _voices = [.. Enumerable.Range(0, Voices).Select(_ => new Voice())];
    private readonly NoiseVoice _noiseA = new(), _noiseB = new();
    private readonly bool _deepTremolo, _deepVibrato, _noteSelect;

    // orig: ASOUND DS:2400 (the copy of the chip's registers), DS:2500 (each channel's noise sweep), DS:23fc (the channel the code works on)
    private readonly byte[] _registers = new byte[256];
    private readonly NoiseSweep[] _channelNoise = [.. Enumerable.Range(0, 9).Select(_ => new NoiseSweep(0, 0, 0, 0))];
    private int _channel;

    // orig: ASOUND DS:2320 (the driver runs: ticks play), CS:0bb8 (the timer mode), CS:0bbb (the PC's handler saved), CS:0bba (the interrupt countdown), CS:0bbc (ticks)
    private bool _running;
    private TimerMode _timerMode;
    private bool _handlerSaved;
    private byte _countdown;
    private ushort _tickCount;
    private ushort _noiseRandom;

    // the engines. orig: ASOUND DS:2286 (which plays), DS:2285 (the command's car), DS:2287 (the gear playing), DS:2288, DS:2289 (each car's gear),
    // DS:228a (the glide's step), DS:228c (the engine's frequency), DS:228e (the whine's), DS:2290 (the frequency glided to), DS:2292 (the aeroplane)
    private Engine _engine;
    private bool _car1Command;
    private byte _gear, _gearCar1, _gearCar2;
    private short _glideStep;
    private ushort _engineFrequency, _whineFrequency, _targetFrequency;
    private bool _plane;

    // orig: ASOUND DS:2293, DS:2294 (the noise voices' ticks while paused)
    private byte _pausedNoiseA, _pausedNoiseB;

    // the gunfire. orig: ASOUND DS:2282 (on), DS:227e (its level), DS:2280 (its intensity), DS:227c (the level of the last tick),
    // DS:2278 (the beat), DS:227a (the beat's length), DS:2283 (its random number)
    private bool _gunfireOn;
    private ushort _gunfireLevel, _gunfireIntensity, _previousLevel;
    private ushort _beat, _beatLength;
    private ushort _gunfireRandom;

    /// <summary>Makes the driver from the player's ASOUND.CA, playing on <paramref name="chip"/>.</summary>
    /// <param name="file">The driver file's data; the driver changes some of its tune bytes as it plays.</param>
    /// <param name="chip">The card.</param>
    /// <param name="timer">The timer interrupt's rate.</param>
    public AdlibSoundDriver(AdlibDriverFile file, IOplChip chip, ISoundTimer timer)
    {
        _file = file;
        _chip = chip;
        _timer = timer;
        _deepTremolo = file.Byte(DeepTremoloFlag) != 0;
        _deepVibrato = file.Byte(DeepVibratoFlag) != 0;
        _noteSelect = file.Byte(NoteSelectFlag) != 0;
        _noiseRandom = file.Word(NoiseRandomSeed);
        _gunfireRandom = file.Word(GunfireRandomSeed);
    }

    /// <summary>The driver on an OPL chip of its own, as the game's sound driver on a device of <paramref name="sampleRate"/>, started.</summary>
    /// <param name="file">The driver file's data.</param>
    /// <param name="sampleRate">The device's frames per second.</param>
    /// <param name="clock">Real time (the AdLib driver has no command that waits).</param>
    /// <param name="watch">Told of every register written and every rate and interrupt (for tests and tools); null: nothing is told.</param>
    public static ClockedSoundDriver OnOpl(AdlibDriverFile file, int sampleRate, CovertAction.Platform.IClock clock, Action<SoundDeviceEvent>? watch = null) =>
        new(sampleRate, clock, timer =>
        {
            var chip = new Synthesis.OplSynth(sampleRate);
            return (new AdlibSoundDriver(file, WatchedDevice.Opl(chip, watch), timer), chip);
        }, watch);

    /// <summary>Whether the start found the card (the original's start returns 0 then, 41h otherwise; the game does not look).</summary>
    public bool CardDetected { get; private set; }

    /// <summary>Ticks since the driver started (the driver's slot 105 tells the game; no part of it asks).</summary>
    // orig: ASOUND 0000:18fc read_tick_count driver; tests: AdlibSoundOracleTests
    public int ReadTickCount() => _tickCount;

    /// <inheritdoc/>
    public bool IsQuiet => _voices.All(v => v.Ticks == 0) && _noiseA.Ticks == 0 && _noiseB.Ticks == 0;

    /// <inheritdoc/>
    /// <remarks>The caller's word the original keeps (DS:2270) is not read by the driver.</remarks>
    // orig: ASOUND 0000:18a7 init_driver driver; also ASOUND 0000:0bc6, ASOUND 0000:0c93, ASOUND 0000:0c2d, ASOUND 0000:0c51; tests: AdlibSoundOracleTests
    public void Start()
    {
        if (!_handlerSaved)
        {
            _handlerSaved = true;
            _timerMode = TimerMode.None;
            UseSlowTimer();
        }
        // DIVERGENCE: D-19 the PIT calibration of the delays between port writes (0c51) is not done: no bus to wait for.
        // The card's detection: both timers masked, then timer 1 started at reload FFh (80 us); its overflow must
        // show in the status. The flags' reset (register 4 <- 80h) is meant after the mask, but the original's writer
        // leaves the value in AL, so the 80h goes to register 60h (60h's own value) and the flags are never reset: a
        // second start finds them set and fails. Kept as it is.
        // BUG: CA-49
        Write(0x04, 0x60);
        Write(0x60, 0x80);
        var before = _chip.ReadStatus();
        Write(0x02, 0xff);
        Write(0x04, 0x21);
        byte after = 0;
        for (var i = 0; i < DetectionReads; i++)
        {
            after = _chip.ReadStatus();
        }
        Write(0x04, 0x60);
        Write(0x60, 0x80);
        CardDetected = (after & 0xe0) == 0xc0 && (before & 0xe0) == 0;
        Reset();
    }

    /// <inheritdoc/>
    // orig: ASOUND 0000:18df shutdown_driver driver; also ASOUND 0000:0bf6; tests: AdlibSoundOracleTests
    public void Stop()
    {
        StopAll();
        // the PC's own handler back (whatever was saved), and its rate (divisor 0: 65,536)
        _timer.SetInterruptDivisor(0);
        _timerMode = TimerMode.None;
        _handlerSaved = false;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">A negative command: the original calls through a word in front of its table.</exception>
    // orig: ASOUND 0000:18c4 play_sound driver; tests: AdlibSoundOracleTests
    public bool Play(int command, int parameter, int variant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(command);
        if (command < Commands)
        {
            Carry(command, (ushort)parameter, (ushort)variant);
        }
        return false;
    }

    /// <summary>The tune each voice starts, of the commands that start tunes (driver words 1849h-18a6h give the routines).</summary>
    private static (int Voice, ushort Tune)[]? TunesOf(int command) => command switch
    {
        2 => Song(0x079a, 0x0818, 0x08ce, 0x0a08, 0x0a96, 0x0b36, 0x0bd2),   // 10dc: the theme
        39 => Song(0x1a7e, 0x1af0, 0x1b62, 0x1c22, 0x1c9c, 0x1d20, 0x1da4),  // 16df
        40 => Song(0x1e28, 0x1e4c, 0x1e70, 0x1e8a, 0x0794, 0x0796, 0x0798),  // 168f
        41 => Song(0x0c2a, 0x0cfe, 0x0d4c, 0x0d8c, 0x0db8, 0x0e4e, 0x0790),  // 1741
        42 => Song(0x1392, 0x13f8, 0x1454, 0x14a4, 0x14de, 0x150c, 0x0792),  // 1791
        43 => Song(0x153a, 0x1582, 0x1634, 0x1734, 0x17b0, 0x18ec, 0x19ce),  // 17e1
        3 => [(3, 0x1ea2)], 4 => [(4, 0x1eb0)], 5 => [(3, 0x1ecc)], 6 => [(3, 0x1ebe)], 7 => [(5, 0x1f1a)],
        8 => [(6, 0x1f22)], 9 => [(3, 0x1ed4)], 10 => [(3, 0x2016)], 11 => [(3, 0x1f50)], 12 => [(3, 0x1f58)],
        13 => [(4, 0x1ede)], 14 => [(4, 0x1ee6)], 15 => [(4, 0x1f64)], 16 => [(3, 0x1f6c)], 17 => [(3, 0x1f78)],
        18 => [(4, 0x1eee)], 19 => [(5, 0x1f2a)], 20 => [(5, 0x1f32)], 21 => [(5, 0x1f84)], 23 => [(4, 0x1f02)],
        28 => [(3, 0x1f8c)], 29 => [(3, 0x1f3c)], 30 => [(4, 0x1f96)], 31 => [(3, 0x1fa2)],
        32 => [(4, 0x200e), (3, 0x200e)],                                    // 1529: both voices, the same tune
        33 => [(3, 0x1fae)], 34 => [(4, 0x1fb6)], 45 => [(4, 0x1fbe)], 46 => [(4, 0x1fea)],
        _ => null,
    };

    private static (int, ushort)[] Song(params ushort[] tunes) => [.. tunes.Select((tune, voice) => (voice, tune))];

    /// <summary>The command table: what each command does (the numbers are its routine's address in the driver).</summary>
    // orig: ASOUND 0000:10dc play_theme driver; also ASOUND 0000:112c, ASOUND 0000:1139, ASOUND 0000:1146, ASOUND 0000:1153, ASOUND 0000:1160, ASOUND 0000:116d, ASOUND 0000:117a, ASOUND 0000:1187, ASOUND 0000:1194, ASOUND 0000:11a1, ASOUND 0000:11ae, ASOUND 0000:11bb, ASOUND 0000:11bc, ASOUND 0000:11c9, ASOUND 0000:11ca, ASOUND 0000:11d7, ASOUND 0000:11e4, ASOUND 0000:11f1, ASOUND 0000:11fe, ASOUND 0000:120b, ASOUND 0000:1218, ASOUND 0000:1225, ASOUND 0000:0f48, ASOUND 0000:14f5, ASOUND 0000:1502, ASOUND 0000:150f, ASOUND 0000:151c, ASOUND 0000:1529, ASOUND 0000:1543, ASOUND 0000:1550, ASOUND 0000:155d, ASOUND 0000:156a, ASOUND 0000:168f, ASOUND 0000:16df, ASOUND 0000:1741, ASOUND 0000:1791, ASOUND 0000:17e1; tests: AdlibSoundOracleTests
    private void Carry(int command, ushort parameter, ushort variant)
    {
        switch (command)
        {
            case 0: StopAll(); return;                                  // 1832
            case 1: EndTunes(); return;                                 // 10a6: the menus' and scenes' end
            case 26: SetEngineGear(car1: true, parameter); return;      // 1232
            case 27: SetEngineGear(car1: false, parameter); return;     // 123a
            case 35: TakeOff(); return;                                 // 1577
            case 36: Land(); return;                                    // 158e
            case 37: Pause(); return;                                   // 15c2
            case 38: Resume(); return;                                  // 1610
            case 44: StartGunfire(parameter, variant); return;          // 172f
        }
        // 22, 24 and 25 do nothing (11bb, 11c9, 0f48)
        foreach (var (voice, tune) in TunesOf(command) ?? [])
        {
            StartVoice(_voices[voice], tune, following: 0);
        }
    }

    /// <summary>A voice starts a tune at its next tick, with no bend, slide, detune or early release.</summary>
    // orig: ASOUND 0000:1066 start_voice driver; tests: AdlibSoundOracleTests
    private static void StartVoice(Voice voice, ushort tune, ushort following)
    {
        voice.Bend = 0;
        voice.SlidePeriod = 0xff;
        voice.VolumeStep = 0;
        voice.Detune = 0;
        voice.Start = voice.Position = voice.InnerStart = voice.OuterStart = tune;
        voice.InnerRepeats = voice.OuterRepeats = 0;
        voice.EarlyRelease = 0;
        voice.FollowingTune = following;
        voice.Ticks = 1;
    }

    /// <summary>Every voice goes on to the ending tune at the end of its note (a silent voice stays silent).</summary>
    // orig: ASOUND 0000:10a6 end_tunes driver; tests: AdlibSoundOracleTests
    private void EndTunes()
    {
        foreach (var voice in _voices)
        {
            voice.Position = Ending;
        }
    }

    /// <summary>Command 0: the engines forgotten and the card reset.</summary>
    // orig: ASOUND 0000:1832 stop_all driver; tests: AdlibSoundOracleTests
    private void StopAll()
    {
        _engine = Engine.None;
        _gear = _gearCar1 = _gearCar2 = 0;
        Reset();
    }

    /// <summary>All voices and noise voices silent, the gunfire's level gone, every register of the card 0, waveforms enabled.</summary>
    // orig: ASOUND 0000:0ced reset_card driver; tests: AdlibSoundOracleTests
    private void Reset()
    {
        _gunfireLevel = _previousLevel = _gunfireIntensity = 0;
        foreach (var voice in _voices)
        {
            voice.Ticks = voice.Bend = voice.VolumeStep = 0;
        }
        foreach (var noise in (NoiseVoice[])[_noiseA, _noiseB])
        {
            noise.Ticks = 0;
            noise.Mask = noise.Frequency = noise.Step = 0;
        }
        _running = true;
        for (var register = 0xff; register > 0; register--)
        {
            Write(register, 0);
        }
        Write(0x01, 0x20);
    }

    /// <summary>A register written, and its copy kept.</summary>
    // orig: ASOUND 0000:0e53 write_register driver; also ASOUND 0000:0e49; tests: AdlibSoundOracleTests
    private void Write(int register, int value)
    {
        // DIVERGENCE: D-19 no delays between the index and the data write (the original's calibrated loops).
        _registers[register] = (byte)value;
        _chip.Write(register, (byte)value);
    }

    // ---- the timer

    /// <inheritdoc/>
    /// <remarks>The original also passes every third slow or every sixteenth fast interrupt on to the PC's own handler.</remarks>
    // orig: ASOUND 0000:1036 slow_timer_interrupt driver; also ASOUND 0000:0f80; tests: AdlibSoundOracleTests
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
                NoiseStep();
                // the countdown is shared: the first tick after a switch from the slow timer comes after what was left of it
                if (--_countdown == 0)
                {
                    _countdown = InterruptsPerFastTick;
                    Tick();
                }
                break;
        }
    }

    // orig: ASOUND 0000:0f49 use_slow_timer driver; tests: AdlibSoundOracleTests
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

    // orig: ASOUND 0000:0fff use_fast_timer driver; tests: AdlibSoundOracleTests
    private void UseFastTimer()
    {
        if (_timerMode == TimerMode.Fast)
        {
            return;
        }
        _timerMode = TimerMode.Fast;
        _timer.SetInterruptDivisor(FastDivisor);
    }

    /// <summary>The fast interrupt's work: each sounding noise voice's channel gets its frequency with new random bits.</summary>
    private void NoiseStep()
    {
        _noiseRandom = (ushort)(_noiseRandom + 0x9248);
        _noiseRandom = (ushort)((_noiseRandom >> 3) | (_noiseRandom << 13));
        if (_noiseA.Ticks != 0)
        {
            SetNoiseFrequency(_noiseA.Channel, (ushort)((~_noiseRandom & _noiseA.Mask) + _noiseA.Frequency));
        }
        if (_noiseB.Ticks != 0)
        {
            SetNoiseFrequency(_noiseB.Channel, (ushort)((_noiseRandom & _noiseB.Mask) + _noiseB.Frequency));
        }
    }

    /// <summary>A channel sounds at a frequency word (block and F-number), key on.</summary>
    // orig: ASOUND 0000:0e74 set_noise_frequency driver; tests: AdlibSoundOracleTests
    private void SetNoiseFrequency(int channel, ushort frequency)
    {
        Write(0xa0 + channel, frequency & 0xff);
        Write(0xb0 + channel, (frequency >> 8) | 0x20);
    }

    /// <summary>A tick (60 a second): the gunfire, the engine's glide, the voices, then the noise voices' sweep.</summary>
    // orig: ASOUND 0000:0e86 timer_tick driver; tests: AdlibSoundOracleTests
    private void Tick()
    {
        if (!_running)
        {
            return;
        }
        _tickCount++;
        if (_gunfireOn)
        {
            GunfireTick();
        }
        if (_glideStep != 0)
        {
            EngineGlide();
        }
        VoicesTick();
        if (_noiseA.Ticks == 0 && _noiseB.Ticks == 0)
        {
            UseSlowTimer();
            return;
        }
        // At its end a noise voice's channel is to go quiet, unless the other noise voice goes on on it. The original
        // loads the channel only when it compares it with the other's: when the other noise voice is silent (the first
        // voice) or still sounds (the second), the "channel" is what the register AL still holds, the low byte of the
        // frequency just swept, and that register pair (A0h + it, B0h + it, as bytes) is cleared instead. Kept as it is.
        // BUG: CA-50
        if (_noiseA.Ticks != 0)
        {
            _noiseA.Frequency += _noiseA.Step;
            if (--_noiseA.Ticks == 0)
            {
                if (_noiseB.Ticks == 0)
                {
                    SilenceChannel(_noiseA.Frequency);
                }
                else if (_noiseA.Channel != _noiseB.Channel)
                {
                    SilenceChannel(_noiseA.Channel);
                }
            }
        }
        if (_noiseB.Ticks != 0)
        {
            _noiseB.Frequency += _noiseB.Step;
            if (--_noiseB.Ticks == 0)
            {
                if (_noiseA.Ticks != 0)
                {
                    SilenceChannel(_noiseB.Frequency);
                }
                else if (_noiseB.Channel != _noiseA.Channel)
                {
                    SilenceChannel(_noiseB.Channel);
                }
            }
        }
    }

    /// <summary>A channel's frequency and key cleared (the registers wrap as the original's byte additions do).</summary>
    private void SilenceChannel(int channel)
    {
        Write((byte)(0xa0 + channel), 0);
        Write((byte)(0xb0 + channel), 0);
    }

    // ---- the voices

    /// <summary>Each voice's tick, voice n on channel n.</summary>
    // orig: ASOUND 0000:0b64 voices_tick driver; tests: AdlibSoundOracleTests
    private void VoicesTick()
    {
        _channel = 0;
        foreach (var voice in _voices)
        {
            VoiceTick(voice);
        }
    }

    /// <summary>
    /// A voice's tick: its note's early release and end; at the end the tune's events up to the next note, which
    /// starts; then the bend and the volume slide, whether the voice sounds or not.
    /// </summary>
    // orig: ASOUND 0000:073a voice_tick driver; tests: AdlibSoundOracleTests
    private void VoiceTick(Voice voice)
    {
        if (voice.Ticks != 0)
        {
            if (voice.ReleaseCountdown != 0 && --voice.ReleaseCountdown == 0)
            {
                KeyOff();
            }
            if (--voice.Ticks == 0)
            {
                NextNote(voice);
            }
        }
        if (voice.Bend != 0)
        {
            BendPitch((sbyte)voice.Bend);
        }
        if (voice.SlideCountdown != 0 && --voice.SlideCountdown == 0)
        {
            voice.SlideCountdown = voice.SlidePeriod;
            if (voice.VolumeStep != 0)
            {
                voice.Volume += voice.VolumeStep;
                SetVolume(voice.Volume);
            }
        }
        _channel++;
    }

    /// <summary>The tune's events, then its next note: a rest or the tune's last (length 0) releases the key.</summary>
    private void NextNote(Voice voice)
    {
        while (_file.Byte(voice.Position) >= (byte)TuneEvent.Detune)
        {
            TuneEventAt(voice);
        }
        voice.Note = _file.Byte(voice.Position);
        voice.Ticks = _file.Byte(voice.Position + 1);
        voice.Position += 2;
        if (voice.Note == 0 || voice.Ticks == 0)
        {
            KeyOff();
            return;
        }
        voice.ReleaseCountdown = (byte)(voice.Ticks - voice.EarlyRelease);
        StartNote(voice.Note, voice.Volume, (sbyte)voice.Detune);
    }

    /// <summary>One event of a voice's tune, and its operands.</summary>
    private void TuneEventAt(Voice voice)
    {
        var at = voice.Position;
        var operand = _file.Byte(at + 1);
        switch ((TuneEvent)_file.Byte(at))
        {
            case TuneEvent.Detune:
                voice.Detune = operand;
                voice.Position += 2;
                break;
            case TuneEvent.VolumeSlide:
                voice.SlidePeriod = operand;
                voice.VolumeStep = _file.Byte(at + 2);
                voice.SlideCountdown = 1;
                voice.Position += 3;
                break;
            case TuneEvent.Volume:
                voice.Volume = operand;
                voice.Position += 2;
                SetVolume(voice.Volume);
                break;
            case TuneEvent.Bend:
                voice.Bend = operand;
                voice.Position += 2;
                break;
            case TuneEvent.EarlyRelease:
                voice.EarlyRelease = operand;
                voice.Position += 2;
                break;
            case TuneEvent.Instrument:
                voice.Instrument = operand;
                voice.Position += 2;
                SetInstrument(voice.Instrument);
                break;
            case TuneEvent.End:
                if (voice.FollowingTune == 0)
                {
                    voice.Position = voice.Start;
                }
                else
                {
                    voice.Start = voice.Position = voice.InnerStart = voice.OuterStart = voice.FollowingTune;
                }
                break;
            case TuneEvent.OuterRepeat:
                OuterRepeat(voice, operand);
                break;
            case TuneEvent.InnerRepeat:
                InnerRepeat(voice, operand);
                break;
        }
    }

    /// <summary>FFh: with 0 the mark of an inner repeat, else a jump back to it until the count is used up.</summary>
    private static void InnerRepeat(Voice voice, byte count)
    {
        if (voice.InnerRepeats == 0)
        {
            if (count == 0)
            {
                voice.Position += 2;
                voice.InnerStart = voice.Position;
                voice.InnerRepeats = 0;
            }
            else
            {
                voice.InnerRepeats = (ushort)(sbyte)count;
                voice.Position = voice.InnerStart;
            }
            return;
        }
        if (--voice.InnerRepeats == 0)
        {
            voice.Position += 2;
            voice.InnerStart = voice.Position;
        }
        else
        {
            voice.Position = voice.InnerStart;
        }
    }

    /// <summary>FEh: as FFh one level out; passing it also marks the inner repeat's start.</summary>
    private static void OuterRepeat(Voice voice, byte count)
    {
        if (voice.OuterRepeats == 0)
        {
            if (count == 0)
            {
                voice.Position += 2;
                voice.OuterStart = voice.InnerStart = voice.Position;
                voice.InnerRepeats = voice.OuterRepeats = 0;
            }
            else
            {
                voice.OuterRepeats = (ushort)(sbyte)count;
                voice.Position = voice.OuterStart;
            }
            return;
        }
        if (--voice.OuterRepeats == 0)
        {
            voice.Position += 2;
            voice.OuterStart = voice.InnerStart = voice.Position;
        }
        else
        {
            voice.Position = voice.OuterStart;
        }
    }

    // ---- the channel's registers (the channel the code works on: _channel)

    /// <summary>
    /// A note starts on the channel: the instrument's noise sweep, if it has one, on a free noise voice (the second if
    /// the first plays) with the fast timer; the note's frequency, the volume, and the key on.
    /// </summary>
    // orig: ASOUND 0000:029a start_note driver; tests: AdlibSoundOracleTests
    private void StartNote(byte note, byte volume, sbyte detune)
    {
        var sweep = _channelNoise[_channel];
        if (sweep.Ticks != 0)
        {
            var noise = _noiseA.Ticks == 0 ? _noiseA : _noiseB;
            noise.Channel = (byte)_channel;
            noise.Ticks = sweep.Ticks;
            noise.Mask = sweep.Mask;
            noise.Frequency = sweep.Frequency;
            noise.Step = sweep.Step;
            UseFastTimer();
        }
        SetNote(note, detune);
        SetVolume(volume);
        var keyRegister = 0xb0 + _channel;
        Write(keyRegister, _registers[keyRegister] | 0x20);
    }

    /// <summary>A note's frequency: the semitone's F-number (detuned) and the octave as the block; the key as it was.</summary>
    // orig: ASOUND 0000:009a set_note driver; tests: AdlibSoundOracleTests
    private void SetNote(byte note, sbyte detune)
    {
        var frequency = (ushort)(detune + _file.NoteFrequency(note % 12));
        Write(0xa0 + _channel, frequency & 0xff);
        var high = 0xb0 + _channel;
        // as a byte: from octave 8 on the block's bits reach the key bit
        Write(high, (_registers[high] & 0x20) | (byte)((note / 12) << 2 | frequency >> 8));
    }

    /// <summary>The carrier's level: 63 less the volume (its low six bits), the key scale as it was.</summary>
    // orig: ASOUND 0000:0048 set_volume driver; tests: AdlibSoundOracleTests
    private void SetVolume(byte volume)
    {
        var register = 0x40 + _file.OperatorOffset(_channel, carrier: true);
        Write(register, (_registers[register] & 0xc0) | (0x3f - (volume & 0x3f)));
    }

    /// <summary>The channel's frequency word moves by <paramref name="bend"/> (the block's bits included).</summary>
    // orig: ASOUND 0000:018e bend_pitch driver; tests: AdlibSoundOracleTests
    private void BendPitch(sbyte bend)
    {
        var low = 0xa0 + _channel;
        var high = low + 0x10;
        var frequency = (((_registers[high] & 0x1f) << 8) + _registers[low] + bend) & 0x1fff;
        Write(low, frequency & 0xff);
        Write(high, (_registers[high] & 0x20) | frequency >> 8);
    }

    // orig: ASOUND 0000:0272 key_off driver; tests: AdlibSoundOracleTests
    private void KeyOff()
    {
        var register = 0xb0 + _channel;
        Write(register, _registers[register] & 0xdf);
    }

    /// <summary>The channel takes an instrument: key off, the modulator's registers, its noise sweep noted, the carrier's.</summary>
    // orig: ASOUND 0000:061c set_instrument driver; tests: AdlibSoundOracleTests
    private void SetInstrument(int number)
    {
        KeyOff();
        var instrument = _file.Instrument(number);
        WriteOperator(instrument.Modulator, _file.OperatorOffset(_channel, carrier: false));
        _channelNoise[_channel] = instrument.Noise;
        WriteOperator(instrument.Carrier, _file.OperatorOffset(_channel, carrier: true));
    }

    /// <summary>
    /// An operator's registers, with the chip's tremolo/vibrato depth and note select again each time, and the
    /// channel's feedback and connection from this operator (so the carrier's are the ones that stay).
    /// </summary>
    // orig: ASOUND 0000:0602 write_operator driver; also ASOUND 0000:03d6, ASOUND 0000:0420, ASOUND 0000:043c, ASOUND 0000:0482, ASOUND 0000:04ca, ASOUND 0000:050c, ASOUND 0000:0550, ASOUND 0000:05d8; tests: AdlibSoundOracleTests
    private void WriteOperator(AdlibOperator op, int offset)
    {
        Write(0xbd, (_registers[0xbd] & 0x3f) | (_deepTremolo ? 0x80 : 0) | (_deepVibrato ? 0x40 : 0));
        Write(0x08, _noteSelect ? 0x40 : 0);
        Write(0x40 + offset, (0x3f - (op.Level & 0x3f)) | op.KeyScaleLevel << 6);
        Write(0xc0 + _channel, op.Feedback << 1 | (op.FrequencyModulation != 0 ? 0 : 1));
        Write(0x60 + offset, op.Attack << 4 | (op.Decay & 0x0f));
        Write(0x80 + offset, op.Sustain << 4 | (op.Release & 0x0f));
        Write(0x20 + offset, (op.Tremolo != 0 ? 0x80 : 0) + (op.Vibrato != 0 ? 0x40 : 0) + (op.Sustained != 0 ? 0x20 : 0)
            + (op.KeyScaleRate != 0 ? 0x10 : 0) + (op.Multiple & 0x0f));
        Write(0xe0 + offset, op.Waveform & 3);
    }

    // ---- pause

    /// <summary>The ticks stop, the noise voices are put aside, and every carrier is silenced.</summary>
    // orig: ASOUND 0000:15c2 pause_sound driver; also ASOUND 0000:0e28; tests: AdlibSoundOracleTests
    private void Pause()
    {
        (_pausedNoiseA, _noiseA.Ticks) = (_noiseA.Ticks, 0);
        (_pausedNoiseB, _noiseB.Ticks) = (_noiseB.Ticks, 0);
        _running = false;
        foreach (var register in (int[])[0x43, 0x44, 0x45, 0x4b, 0x4c, 0x4d, 0x53, 0x54, 0x55])
        {
            Write(register, _registers[register] | 0x3f);
        }
    }

    /// <summary>The carriers at the voices' volumes again (the engines' at fixed levels), the noise voices back, the ticks on.</summary>
    // orig: ASOUND 0000:1610 resume_sound driver; also ASOUND 0000:0e36; tests: AdlibSoundOracleTests
    private void Resume()
    {
        int[] carriers = [0x43, 0x44, 0x45, 0x4b, 0x4c, 0x4d, 0x53];
        for (var voice = 0; voice < Voices; voice++)
        {
            SetCarrierLevel(carriers[voice], _voices[voice].Volume);
        }
        SetCarrierLevel(0x54, 0x28);
        SetCarrierLevel(0x55, 0x3e);
        _noiseA.Ticks = _pausedNoiseA;
        _noiseB.Ticks = _pausedNoiseB;
        if ((_noiseA.Ticks | _noiseB.Ticks) != 0)
        {
            UseFastTimer();
        }
        _running = true;
    }

    private void SetCarrierLevel(int register, byte volume) => Write(register, (_registers[register] & 0xc0) | (volume ^ 0x3f));

    // ---- the engines (car chase, aeroplane)

    /// <summary>
    /// A car's gear (commands 26 and 27): the engine of that car glides to the gear's frequency on channel 8. The
    /// other car's engine, if it plays, stops for it; gear 0 of a silent engine starts nothing.
    /// </summary>
    // orig: ASOUND 0000:123f set_engine_gear driver; also ASOUND 0000:1232, ASOUND 0000:123a; tests: AdlibSoundOracleTests
    private void SetEngineGear(bool car1, ushort gear)
    {
        _car1Command = car1;
        var low = (byte)gear;
        var thisCar = car1 ? Engine.Car1 : Engine.Car2;
        if (_engine == Engine.None)
        {
            StartEngine();
            return;
        }
        if (_engine == thisCar)
        {
            SetCarGear(low);
            if (_gear == 0)
            {
                StartEngine();
                return;
            }
            GlideTo(gear);
            return;
        }
        // the other car's engine plays: it stops, and this one starts from the gear it had
        EngineKeyOff();
        _engine = thisCar;
        _gear = car1 ? _gearCar1 : _gearCar2;
        if ((_gear | low) == 0)
        {
            StartEngine();
            return;
        }
        SetEngineInstruments();
        SetCarGear(low);
        GlideTo(gear);
        _running = true;

        void StartEngine()
        {
            if (low == 0)
            {
                return;
            }
            _engine = _car1Command ? Engine.Car1 : Engine.Car2;
            SetEngineInstruments();
            SetCarGear(low);
            GlideTo(gear);
        }
    }

    private void SetCarGear(byte gear)
    {
        if (_car1Command)
        {
            _gearCar1 = gear;
        }
        else
        {
            _gearCar2 = gear;
        }
    }

    /// <summary>The glide to a gear's frequency, which becomes the gear playing.</summary>
    // orig: ASOUND 0000:1346 load_gear_target driver; tests: AdlibSoundOracleTests
    private void GlideTo(ushort gear)
    {
        _targetFrequency = _file.Word((GearFrequencies + (ushort)(gear * 2)) & 0xffff);
        ChooseGlide();
        _gear = (byte)gear;
    }

    /// <summary>
    /// The engine's instruments on its channels, at the frequency of the gear playing, key on (the aeroplane's whine
    /// too); the guard against interrupts is lifted at the end, also after a pause (as in the original).
    /// </summary>
    // orig: ASOUND 0000:135e set_engine_instruments driver; also ASOUND 0000:1332, ASOUND 0000:1353, ASOUND 0000:147f, ASOUND 0000:1495, ASOUND 0000:14c7, ASOUND 0000:14e0; tests: AdlibSoundOracleTests
    private void SetEngineInstruments()
    {
        _channel = EngineChannel;
        if (_plane)
        {
            SetInstrument(PlaneEngine);
            _channel = WhineChannel;
            SetInstrument(PlaneWhine);
        }
        else
        {
            SetInstrument(_engine == Engine.Car1 ? Car1Engine : Car2Engine);
        }
        _engineFrequency = _file.Word(GearFrequencies + 2 * _gear);
        Write(0x55, ((_registers[0x55] & 0xc0) | 1) + (_plane ? 0x14 : 0));
        Write(0xa8, _engineFrequency & 0xff);
        Write(0xb8, (_engineFrequency >> 8) | 0x20);
        if (_plane)
        {
            _whineFrequency = WhineFrequency;
            Write(0x54, (_registers[0x54] & 0xc0) | 0x17);
            Write(0xa7, _whineFrequency & 0xff);
            Write(0xb7, (_whineFrequency >> 8) | 0x20);
        }
        _running = true;
    }

    /// <summary>The glide's direction: up or down 4 a tick to the frequency glided to, or none.</summary>
    // orig: ASOUND 0000:13e2 choose_glide driver; tests: AdlibSoundOracleTests
    private void ChooseGlide() =>
        _glideStep = (short)_targetFrequency > (short)_engineFrequency ? GlideStep
            : (short)_targetFrequency < (short)_engineFrequency ? (short)-GlideStep : (short)0;

    /// <summary>
    /// A tick of the glide on channel 8, from the frequency its registers hold; at the frequency glided to it stops,
    /// and at the lowest gear's (gear 0) the key is released.
    /// </summary>
    // orig: ASOUND 0000:140a engine_glide driver; tests: AdlibSoundOracleTests
    private void EngineGlide()
    {
        var frequency = (ushort)(((_registers[0xb8] << 8 | _registers[0xa8]) + _glideStep) & 0x1fff);
        _engineFrequency = frequency;
        Write(0xa8, frequency & 0xff);
        Write(0xb8, frequency >> 8 | (_registers[0xb8] & 0x20));
        if (frequency == _targetFrequency)
        {
            _glideStep = 0;
            if (_gear == 0 && (short)_targetFrequency <= (short)StopBelowFrequency)
            {
                Write(0xb8, _registers[0xb8] & 0xdf);
            }
        }
        _running = true;
    }

    // orig: ASOUND 0000:14ab engine_key_off driver; tests: AdlibSoundOracleTests
    private void EngineKeyOff()
    {
        Write(0xb8, _registers[0xb8] & 0xdf);
        _running = true;
    }

    /// <summary>Command 35: the aeroplane's engine and whine start at gear 5 and glide up.</summary>
    // orig: ASOUND 0000:1577 take_off driver; tests: AdlibSoundOracleTests
    private void TakeOff()
    {
        _plane = true;
        _gear = 5;
        _targetFrequency = TakeOffFrequency;
        SetEngineInstruments();
        ChooseGlide();
    }

    /// <summary>Command 36: the whine stops, the engine glides down to its end; the engines are free again.</summary>
    // orig: ASOUND 0000:158e land driver; tests: AdlibSoundOracleTests
    private void Land()
    {
        _targetFrequency = LandedFrequency;
        ChooseGlide();
        Write(0xb7, _registers[0xb7] & 0xdf);
        _running = true;
        _gear = 0;
        _engine = Engine.None;
        _plane = false;
    }

    // ---- the raid's gunfire (command 44)

    /// <summary>Command 44: the gunfire at a level (1-3 sparse, 4 heavy, 5 a barrage) and an intensity; either 0 stops it.</summary>
    // orig: ASOUND 0000:172f start_gunfire driver; tests: AdlibSoundOracleTests
    private void StartGunfire(ushort level, ushort intensity)
    {
        _gunfireLevel = level;
        _gunfireIntensity = intensity;
        _gunfireOn = true;
    }

    // orig: ASOUND 0000:1991 gunfire_tick driver; tests: AdlibSoundOracleTests
    private void GunfireTick()
    {
        if (_gunfireLevel == 0 || _gunfireIntensity == 0)
        {
            StopGunfire();
            return;
        }
        NextGunfireRandom(carry: false);
        if (_gunfireLevel >= 4)
        {
            HeavyGunfire();
        }
        else
        {
            SparseGunfire();
        }
        _previousLevel = _gunfireLevel;
    }

    /// <summary>Voices 0-2 go on to the ending tune; the gunfire is off.</summary>
    // orig: ASOUND 0000:1951 stop_gunfire driver; tests: AdlibSoundOracleTests
    private void StopGunfire()
    {
        _gunfireOn = false;
        _voices[0].Position = _voices[1].Position = _voices[2].Position = Ending;
        _previousLevel = 0;
    }

    /// <summary>
    /// The gunfire's random number: the previous one plus 7131h plus the tick count, with the carries as the original's
    /// ADC instructions add them (<paramref name="carry"/>: the carry flag the caller leaves).
    /// </summary>
    // orig: ASOUND 0000:1906 next_gunfire_random driver; tests: AdlibSoundOracleTests
    private ushort NextGunfireRandom(bool carry)
    {
        var sum = _gunfireRandom + 0x7131u + (carry ? 1u : 0u);
        _gunfireRandom = (ushort)((sum & 0xffff) + _tickCount + (sum >> 16));
        return _gunfireRandom;
    }

    /// <summary>Levels 1-3: after heavier fire the rhythm voices end; voice 2 keeps shooting with pauses.</summary>
    // orig: ASOUND 0000:19b9 sparse_gunfire driver; also ASOUND 0000:196d; tests: AdlibSoundOracleTests
    private void SparseGunfire()
    {
        if (_previousLevel >= 4)
        {
            _voices[0].Position = Ending;
            EndShootingVoices();
        }
        KeepShooting();
        ChooseShotPause();
    }

    /// <summary>Levels 4 and 5: the rhythm on voice 0 and a beat; level 5 the barrage on voices 1 and 2.</summary>
    // orig: ASOUND 0000:19d4 heavy_gunfire driver; tests: AdlibSoundOracleTests
    private void HeavyGunfire()
    {
        VaryRhythm();
        if (_beat == 0)
        {
            _beat = _beatLength;
        }
        _beat--;
        if (_gunfireLevel == 5)
        {
            StartBarrage();
            return;
        }
        if (_previousLevel == 5)
        {
            EndShootingVoices();
        }
        KeepShooting();
        ChooseShotPause();
    }

    /// <summary>Voices 1 and 2 go on to the ending tune; the rhythms' instrument is the single shots again.</summary>
    // orig: ASOUND 0000:1976 end_shooting_voices driver; also ASOUND 0000:1982; tests: AdlibSoundOracleTests
    private void EndShootingVoices()
    {
        _voices[1].Position = _voices[2].Position = Ending;
        SetRhythmInstrument(SingleShots);
    }

    /// <summary>The instrument of the four rhythms (a byte in each of them, DS:1335 and so on).</summary>
    // orig: ASOUND 0000:1984 set_rhythm_instrument driver; tests: AdlibSoundOracleTests
    private void SetRhythmInstrument(byte instrument)
    {
        foreach (var at in RhythmInstrumentBytes)
        {
            _file.SetByte(at, instrument);
        }
    }

    /// <summary>
    /// Voice 0's rhythm: started with a new beat when the voice is silent; at the last tick of a note its next round
    /// is one of four rhythms at random.
    /// </summary>
    // orig: ASOUND 0000:1915 vary_rhythm driver; tests: AdlibSoundOracleTests
    private void VaryRhythm()
    {
        var voice = _voices[0];
        // the original compares the voice's first word: its ticks and its bend together
        if (voice.Ticks == 0 && voice.Bend == 0)
        {
            _beatLength = 0x70;
            _beat = 0x72;
            SetRhythmInstrument(SingleShots);
            StartVoice(voice, Rhythm, following: 0);
        }
        else if (voice.Ticks == 1 && voice.Bend == 0)
        {
            voice.Start = _file.Word(RhythmVariants + (_gunfireRandom & 6));
        }
    }

    /// <summary>The barrage starts (when the level was not 5 already) on voices 1 and 2; voice 2's next round is chosen at its last tick.</summary>
    private void StartBarrage()
    {
        if (_previousLevel != 5)
        {
            SetRhythmInstrument(Barrage);
            StartVoice(_voices[1], Shot, following: ShotFollowUp);
            StartVoice(_voices[2], Shot, following: 0);
            var pause = (short)(_beat - 1);
            _file.SetByte(ShotLength, (byte)(pause < 0 ? (int)_beatLength : pause));
        }
        if (_voices[2].Ticks == 1)
        {
            ChooseShotPattern();
        }
    }

    /// <summary>Voice 2, when silent, shoots again, its next round chosen at random.</summary>
    // orig: ASOUND 0000:1a48 keep_shooting driver; tests: AdlibSoundOracleTests
    private void KeepShooting()
    {
        if (_voices[2].Ticks != 0)
        {
            return;
        }
        StartVoice(_voices[2], Shot, following: 0);
        ChooseShotPattern();
    }

    /// <summary>Where voice 2 goes on at the end of its shot: one of the level's patterns at random (16 for level 1, 8 for the others).</summary>
    // orig: ASOUND 0000:1a5c choose_shot_pattern driver; tests: AdlibSoundOracleTests
    private void ChooseShotPattern()
    {
        var levelIndex = (ushort)(_gunfireLevel - 1);
        var row = (ushort)(levelIndex << 4);
        // the carry the original's last shift of the row leaves goes into the random number
        var random = NextGunfireRandom(carry: (levelIndex & 0x1000) != 0);
        var entry = _gunfireLevel == 1 ? SparsePatterns + row + (random & 0x1e) : HeavyPatterns + row + (random & 0x0e);
        _voices[2].Start = _file.Word(entry & 0xffff);
    }

    /// <summary>The length of the shot's note: random, shorter at higher levels, longer at higher intensity, at most 255 ticks.</summary>
    // orig: ASOUND 0000:1a91 choose_shot_pause driver; also ASOUND 0000:1ab7; tests: AdlibSoundOracleTests
    private void ChooseShotPause()
    {
        var spread = (ushort)((((_gunfireRandom & 0xff) + 1) / _gunfireLevel) | 1);
        var length = (ushort)(_gunfireIntensity * 20 + spread);
        _file.SetByte(ShotLength, (short)length > 0xff ? (byte)0xff : (byte)length);
    }

    /// <summary>A voice of the tunes (26 bytes at DS:2322 + 26 * n in the original).</summary>
    private sealed class Voice
    {
        public byte Ticks;              // +00 ticks left of the note; 0: the voice is silent
        public byte Bend;               // +01 added to the frequency each tick (signed)
        public byte VolumeStep;         // +02 added to the volume each slide period
        public byte Note;               // +03
        public byte Instrument;         // +04
        public byte Volume;             // +05
        public byte EarlyRelease;       // +06
        public byte ReleaseCountdown;   // +07 ticks to the note's release
        public byte SlideCountdown;     // +08
        public byte SlidePeriod;        // +09
        public ushort Start;            // +0a where the tune starts again at its end
        public ushort Position;         // +0c the next byte of the tune
        public ushort InnerStart;       // +0e
        public ushort OuterStart;       // +10
        public ushort InnerRepeats;     // +12 repeats left
        public ushort OuterRepeats;     // +14
        public ushort FollowingTune;    // +16 the tune played at the end instead (0: this one again)
        public byte Detune;             // +18 (signed)
    }

    /// <summary>One of the two noise voices (DS:23f2/23fd and so on, DS:23f3/23fe and so on).</summary>
    private sealed class NoiseVoice
    {
        public byte Channel;
        public byte Ticks;
        public ushort Mask;
        public ushort Frequency;
        public ushort Step;
    }
}
