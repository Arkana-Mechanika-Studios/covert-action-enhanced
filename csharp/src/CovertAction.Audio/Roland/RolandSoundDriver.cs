using CovertAction.Content;

namespace CovertAction.Audio.Roland;

/// <summary>
/// The original's Roland MT-32 sound driver (RSOUND.CA, "RolandMT-32g15-10-90") as C#: its 55 commands and what it does
/// on each timer interrupt, sending MIDI through an MPU-401 in UART mode. Its tunes, system-exclusive messages and
/// gunfire tables are read from the player's driver file (<see cref="RolandDriverFile"/>); which command plays which
/// tune, and how a tune becomes MIDI, is the driver's code, here.
/// </summary>
/// <remarks>
/// <para>
/// At its start the driver resets the MPU-401, puts it in UART mode and sends the MT-32 its set-up as
/// <b>system-exclusive</b> messages (Roland's "data set 1"): a text on the module's display, the system settings, the
/// patches and 38 timbres of its own (car engine, guitars, drums, gunshot, jet plane, doors, telephone...).
/// </para>
/// <para>
/// Eight <b>voices</b> play tunes: voices 0-6 on MIDI channels 2-8 (the MT-32's melodic parts; status nibbles 1-7),
/// voice 7 on channel 10, the rhythm part. A tune is a byte stream in the driver's data: a note is two bytes (its MIDI
/// note number, 0 a rest, and its length in ticks; a length of 0 ends the voice), and the bytes F2h-FFh are events
/// (<see cref="TuneEvent"/>): program, velocity, volume, pan and pitch bend with their slides, an early release, a
/// chord, two levels of repeats, a random choice that rewrites a later byte of the tune, and the end. A tick is
/// every interrupt of the timer, which the driver sets to 60.9 a second.
/// </para>
/// <para>
/// The <b>engines</b> of the car chase and the aeroplane are notes on channel 8 (status nibble 7); the raid's
/// <b>gunfire</b> (command 44) rewrites the gap of a rhythm and the sound of a shot in the driver's data as it plays,
/// on voices 0-2. The driver remembers the last status byte it sent and leaves it out when it is the same (running
/// status). Bytes and words wrap as the original's do. Not reconstructed: the MPU's interrupt handler (04f2), which
/// only reads a byte the MPU may send (the MT-32 sends none), the delays the driver waits after each system-exclusive
/// message (D-19), and the far entries no caller uses (0171, 0513, 0537, the error exit 0df0).
/// </para>
/// </remarks>
public sealed class RolandSoundDriver : ISoundProgram
{
    /// <summary>The timer's divisor: 60.88 interrupts a second, one tick each.</summary>
    public const int TimerDivisor = 0x4c90;

    private const int Commands = 55;
    private const int Voices = 8;
    private const int ResetTries = 5;

    /// <summary>The tune that ends the others: no slides, volume 100, bend centred, then a rest of length 0 (DS:0050).</summary>
    private const ushort Ending = 0x0050;

    // the system-exclusive messages: a header (DS:1889, F0h 41h 10h 16h 12h: Roland, device 17, MT-32, data set 1),
    // then each message's address and data, its checksum and F7h. The display's text (DS:188f, address 20 00 00), the
    // system settings (DS:18a7, 10 00 00), the patches (DS:18c2 and DS:19c6, 05 00 00 and 05 02 00) and the timbres
    // (DS:19fa to DS:2d51, 08 00 00 to 08 4a 00: "Car Engine", "Elec Gtr 1" ... "Typewriter")
    private const ushort SysExHeader = 0x1889;
    private const ushort DisplayText = 0x188f;
    private const byte EndOfData = 0xff;
    private static readonly ushort[] SetUpMessages =
    [
        DisplayText, 0x18a7, 0x18c2, 0x19c6,
        0x19fa, 0x1a81, 0x1b7b, 0x1c75, 0x1cfc, 0x1d83, 0x1dce, 0x1e19, 0x1e64, 0x1eeb, 0x1f42, 0x1fc9, 0x2050, 0x214a,
        0x21a1, 0x2228, 0x22af, 0x2306, 0x235d, 0x23e4, 0x246b, 0x24f2, 0x25ec, 0x2673, 0x2736, 0x27bd, 0x2844, 0x289b,
        0x28f2, 0x2979, 0x2a00, 0x2a87, 0x2b0e, 0x2b59, 0x2c1c, 0x2c73, 0x2cca, 0x2d51,
    ];

    // DS:2d9c the last status byte sent (its first value is the file's)
    private const int LastStatusByte = 0x2d9c;

    // DS:17f2 the tunes' random number, DS:1815 the gunfire's (their first values are the file's)
    private const int TuneRandomSeed = 0x17f2, GunfireRandomSeed = 0x1815;

    // the engines: channel 8, a car's notes from its base note two semitones a gear, the aeroplane's note
    private const byte EngineChannel = 7;
    private const byte EnginePatch = 0, PlanePatch = 12;
    private const ushort Car1BaseNote = 0x2b, Car2BaseNote = 0x30;
    private const byte PlaneNote = 0x30;
    private const byte EngineVelocity = 0x69, PlaneVelocity = 0x64;
    private const ushort NoEngine = 0xffff;

    // the gunfire: its tunes, the bytes of them it rewrites, and its tables of tunes to go on with
    private const ushort ShotTune = 0x08e6, RhythmTune = 0x04be, RhythmGap = 0x04bf, BarrageFollowUp = 0x087e;
    private static readonly ushort[] ShotSoundBytes = [0x08e9, 0x0901, 0x0919, 0x0931];
    private const byte SingleShotSound = 0x45, BarrageSound = 0x5f;
    private const ushort ShotChoices = 0x1877, FirstLevelRhythms = 0x1817, Rhythms = 0x1827;
    private const ushort ShotBeatLength = 0x70, FirstShotBeat = 0x72;
    private const int HeavyLevel = 4, BarrageLevel = 5;
    private const int ShotGapPerIntensity = 20;

    /// <summary>The MPU-401's commands the driver gives.</summary>
    private enum MpuCommand : byte
    {
        UartMode = 0x3f,
        Reset = 0xff,
    }

    /// <summary>The MIDI status bytes the driver sends, before the channel in their low half.</summary>
    private enum MidiStatus : byte
    {
        /// <summary>9nh k v: a note on (velocity 0: off).</summary>
        NoteOn = 0x90,

        /// <summary>Bnh c v: a controller.</summary>
        ControlChange = 0xb0,

        /// <summary>Cnh p: the part's patch.</summary>
        ProgramChange = 0xc0,

        /// <summary>Enh 00 m: the pitch bend (only its high byte; 40h centred).</summary>
        PitchBend = 0xe0,

        /// <summary>The end of a system-exclusive message.</summary>
        EndOfExclusive = 0xf7,
    }

    /// <summary>The MIDI controllers the driver sets.</summary>
    private enum Controller : byte
    {
        Volume = 0x07,
        Pan = 0x0a,
        ResetAllControllers = 0x79,
        AllNotesOff = 0x7b,
    }

    /// <summary>The bytes F2h-FFh of a tune: events, each with its operands; any other byte starts a note.</summary>
    private enum TuneEvent : byte
    {
        /// <summary>F2h n t0..tn-1 d: one of the n bytes t, at random, is written d+1 bytes behind the event (n a power of 2).</summary>
        RandomByte = 0xf2,

        /// <summary>F3h p s: every p ticks the pan moves by s; s 0: centred (40h sent, the pan kept).</summary>
        PanSlide = 0xf3,

        /// <summary>F4h v: the pan.</summary>
        Pan = 0xf4,

        /// <summary>F5h n k0..kn-1 l: n notes at once, for l ticks; the tick's reading stops there.</summary>
        Chord = 0xf5,

        /// <summary>F6h v: the volume (controller 7).</summary>
        Volume = 0xf6,

        /// <summary>F7h b: the pitch bend's high byte.</summary>
        Bend = 0xf7,

        /// <summary>F8h p s: every p ticks the volume moves by s (0: it stays).</summary>
        VolumeSlide = 0xf8,

        /// <summary>F9h v: the notes' velocity.</summary>
        Velocity = 0xf9,

        /// <summary>FAh p s t: every p ticks the bend moves by s, for t ticks.</summary>
        BendSlide = 0xfa,

        /// <summary>FBh t: a note is released t ticks before its end (signed; below 0 the next same note is tied).</summary>
        EarlyRelease = 0xfb,

        /// <summary>FCh p: the program (patch).</summary>
        Program = 0xfc,

        /// <summary>FDh: the end; the tune plays again from its start (or from the tune that follows it).</summary>
        End = 0xfd,

        /// <summary>FEh 0 marks where an outer repeat starts; FEh n goes back there n times.</summary>
        OuterRepeat = 0xfe,

        /// <summary>FFh 0 marks where an inner repeat starts; FFh n goes back there n times.</summary>
        InnerRepeat = 0xff,
    }

    private readonly RolandDriverFile _file;
    private readonly IMpu401 _mpu;
    private readonly ISoundTimer _timer;
    private readonly Voice[] _voices = [.. Enumerable.Range(0, Voices).Select(_ => new Voice())];

    // orig: RSOUND DS:2eb4 the notes each channel holds, four a channel (FFh: none). Behind the file's end, as the voices.
    // UNCERTAIN: U-9 the driver's memory behind the file's end (the voices, the held notes) is 0 before the driver sets it.
    private readonly byte[] _heldNotes = new byte[4 * 10];

    // orig: RSOUND DS:2da0 (ticks play the tunes), DS:17e8 (the set-up sent), CS:0054 (the handlers installed), CS:0059 (ticks), DS:2d9c (the last status)
    private bool _tunesPlay;
    private bool _setUpSent;
    private bool _handlersInstalled;
    private ushort _tickCount;
    private byte _lastStatus;
    private ushort _tuneRandom;
    private ushort _gunfireRandom;

    // the engines. orig: RSOUND DS:187f (the base note of the car), DS:1881 (the last one), DS:1883 (the gear), DS:1885 (the last one), DS:1887 (the note playing)
    private ushort _engineBase, _previousEngineBase, _gear, _previousGear, _engineNote = NoEngine;

    // the gunfire. orig: RSOUND DS:1814 (on), DS:1810 (its level), DS:1812 (its intensity), DS:180e (the level of the last tick),
    // DS:180a (the beat's countdown), DS:180c (the beat's length)
    private bool _gunfireOn;
    private ushort _gunfireLevel, _gunfireIntensity, _previousLevel;
    private ushort _beatCountdown, _beatLength;

    /// <summary>Makes the driver from the player's RSOUND.CA, sending its MIDI through <paramref name="mpu"/>.</summary>
    /// <param name="file">The driver file's data; the driver changes some of its tune bytes as it plays.</param>
    /// <param name="mpu">The MPU-401 and the MT-32 behind it.</param>
    /// <param name="timer">The timer interrupt's rate.</param>
    public RolandSoundDriver(RolandDriverFile file, IMpu401 mpu, ISoundTimer timer)
    {
        _file = file;
        _mpu = mpu;
        _timer = timer;
        _lastStatus = file.Byte(LastStatusByte);
        _tuneRandom = file.Word(TuneRandomSeed);
        _gunfireRandom = file.Word(GunfireRandomSeed);
    }

    /// <summary>The driver on an MT-32 of its own, as the game's sound driver on a device of <paramref name="sampleRate"/>, started.</summary>
    /// <param name="file">The driver file's data.</param>
    /// <param name="synth">The MT-32 (the player's ROMs).</param>
    /// <param name="sampleRate">The device's frames per second.</param>
    /// <param name="clock">Real time (the Roland driver has no command that waits).</param>
    /// <param name="watch">Told of every MIDI byte and command to the interface, and every rate and interrupt (for tests and tools); null: nothing is told.</param>
    public static ClockedSoundDriver OnMidi(RolandDriverFile file, Synthesis.IMidiSynth synth, int sampleRate, CovertAction.Platform.IClock clock, Action<SoundDeviceEvent>? watch = null) =>
        new(sampleRate, clock, timer =>
        {
            var port = new Synthesis.MidiPort(synth);
            return (new RolandSoundDriver(file, WatchedDevice.Mpu(port, watch), timer), port);
        }, watch);

    /// <summary>Whether the start found the MPU-401 (the original's start returns 0 then, 52h otherwise; the game does not look).</summary>
    public bool MpuFound { get; private set; }

    /// <summary>Ticks since the driver was loaded (the driver's slot 105 tells the game; no part of it asks).</summary>
    // orig: RSOUND 0000:0c31 read_tick_count driver; tests: RolandSoundOracleTests
    public int ReadTickCount() => _tickCount;

    /// <inheritdoc/>
    public bool IsQuiet => _voices.All(v => v.Ticks == 0);

    /// <inheritdoc/>
    /// <remarks>The caller's word is not read.</remarks>
    // orig: RSOUND 0000:0bb9 init_driver driver; also RSOUND 0000:005b, RSOUND 0000:0260, RSOUND 0000:012d, RSOUND 0000:00fd; tests: RolandSoundOracleTests
    public void Start()
    {
        // DIVERGENCE: D-19 the PIT calibration of the delays after each system-exclusive message (012d) is not done.
        ClearVoices();
        if (!_handlersInstalled)
        {
            _handlersInstalled = true;
            _timer.SetInterruptDivisor(TimerDivisor);
        }
        MpuFound = ResetMpu();
        if (MpuFound)
        {
            _ = SendCommand(MpuCommand.UartMode);
            SendSetUp();
        }
    }

    /// <inheritdoc/>
    // orig: RSOUND 0000:0bfe shutdown_driver driver; also RSOUND 0000:00ae; tests: RolandSoundOracleTests
    public void Stop()
    {
        ResetChannels();
        _ = ResetMpu();
        // the PC's own handlers back, and the timer's rate (divisor 0: 65,536)
        _timer.SetInterruptDivisor(0);
        _handlersInstalled = false;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">A negative command: the original calls through a word in front of its table.</exception>
    // orig: RSOUND 0000:0be3 play_sound driver; tests: RolandSoundOracleTests
    public bool Play(int command, int parameter, int variant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(command);
        if (command < Commands)
        {
            Carry(command, (ushort)parameter, (ushort)variant);
        }
        return false;
    }

    /// <summary>The tune each voice starts, of the commands that start tunes (driver words CS:0b4b-0bb8 give the routines).</summary>
    private static (int Voice, ushort Tune)[]? TunesOf(int command) => command switch
    {
        2 => [(0, 0x005e), (1, 0x00ee), (2, 0x019c), (3, 0x02cc), (4, 0x0354), (5, 0x03f4), (6, 0x0490)],   // 0601: the theme
        39 => [(0, 0x12c4), (1, 0x1306), (2, 0x1382), (3, 0x13b8), (4, 0x13f0)],                          // 0651
        40 => [(0, 0x0946), (1, 0x0956), (7, 0x0966)],                                                    // 068a
        41 => [(0, 0x097c), (1, 0x0a5e), (2, 0x0ab2), (3, 0x0af2), (4, 0x0b1c), (5, 0x0bb0)],             // 06ad
        42 => [(0, 0x0c82), (1, 0x0d3e), (2, 0x0da6), (3, 0x0e04), (4, 0x0cde), (7, 0x0e40)],             // 06f1
        43 => [(0, 0x0e7e), (1, 0x0eec), (2, 0x0fe8), (3, 0x1158), (7, 0x11d8)],                          // 0735
        3 => [(3, 0x1464)], 4 => [(4, 0x1472)], 5 => [(3, 0x14ac)], 6 => [(3, 0x1490)], 7 => [(5, 0x1512)],
        8 => [(5, 0x151a)], 9 => [(3, 0x14b4)], 10 => [(3, 0x1562), (4, 0x1570)], 11 => [(3, 0x15a2)], 12 => [(3, 0x1594)],
        13 => [(4, 0x14be)], 14 => [(4, 0x14c6)], 15 => [(4, 0x14ce)], 16 => [(3, 0x14d6)], 17 => [(3, 0x14de)],
        18 => [(5, 0x14ec), (7, 0x1428)], 19 => [(2, 0x1522)], 20 => [(2, 0x152a)], 22 => [(7, 0x14fc)],
        23 or 25 => [(1, 0x1508), (7, 0x1502)],                                                           // 0854: the same routine
        24 => [(7, 0x1510)], 28 => [(0, 0x1536), (4, 0x154c)], 29 => [(0, 0x157e)], 30 => [(0, 0x1586)],
        31 => [(0, 0x15aa)], 32 => [(0, 0x15bc)], 33 => [(3, 0x1480)], 34 => [(4, 0x1488)], 45 => [(0, 0x15be)], 46 => [(0, 0x15d6)],
        _ => null,
    };

    /// <summary>The command table: what each command does (the numbers are its routine's address in the driver).</summary>
    // orig: RSOUND 0000:0601 play_theme driver; also RSOUND 0000:0651, RSOUND 0000:068a, RSOUND 0000:06ad, RSOUND 0000:06f1, RSOUND 0000:0735, RSOUND 0000:076e, RSOUND 0000:077b, RSOUND 0000:0788, RSOUND 0000:0795, RSOUND 0000:07a2, RSOUND 0000:07af, RSOUND 0000:07bc, RSOUND 0000:07c9, RSOUND 0000:07d6, RSOUND 0000:07e3, RSOUND 0000:07fb, RSOUND 0000:0808, RSOUND 0000:0815, RSOUND 0000:0822, RSOUND 0000:083a, RSOUND 0000:0847, RSOUND 0000:0854, RSOUND 0000:086c, RSOUND 0000:0879, RSOUND 0000:0886, RSOUND 0000:0893, RSOUND 0000:08ab, RSOUND 0000:08ec, RSOUND 0000:08f9, RSOUND 0000:0906, RSOUND 0000:0913, RSOUND 0000:0920, RSOUND 0000:092d, RSOUND 0000:094c, RSOUND 0000:0959, RSOUND 0000:0a09; tests: RolandSoundOracleTests
    private void Carry(int command, ushort parameter, ushort variant)
    {
        switch (command)
        {
            case 0: ResetChannels(); return;                                // 0acd
            case 1: EndTunes(); return;                                     // 05cb: the menus' and scenes' end
            case 26: SetEngineGear(Car1BaseNote, parameter); return;        // 0966
            case 27: SetEngineGear(Car2BaseNote, parameter); return;        // 0972
            case 35: TakeOff(); return;                                     // 08b8
            case 36: Land(); return;                                        // 08d9
            case 37: Pause(); return;                                       // 0a21
            case 38: Resume(); return;                                      // 0a65
            case 44: StartGunfire(parameter, variant); return;              // 093a
            case >= 47: RestartVoice(_voices[command - 47]); return;        // 0b13-0b44: voices 0-7
        }
        // 21 does nothing (0a09)
        foreach (var (voice, tune) in TunesOf(command) ?? [])
        {
            StartVoice(_voices[voice], tune, following: 0);
        }
    }

    // ---- the MPU-401 ----

    /// <summary>Resets the MPU-401, five tries at most; whether it acknowledged.</summary>
    // orig: RSOUND 0000:04c3 reset_mpu driver; tests: RolandSoundOracleTests
    private bool ResetMpu()
    {
        for (var i = 0; i < ResetTries; i++)
        {
            if (_mpu.Command((byte)MpuCommand.Reset))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A command to the MPU-401; whether it acknowledged.</summary>
    /// <remarks>The original waits for the interface to be ready and for its acknowledgement, a while at most; a ready MPU is always.</remarks>
    // orig: RSOUND 0000:044e send_command driver; tests: RolandSoundOracleTests
    private bool SendCommand(MpuCommand command) => _mpu.Command((byte)command);

    /// <summary>A MIDI byte to the MT-32.</summary>
    /// <remarks>The original waits for the interface to be ready, then reads away any byte it has received.</remarks>
    // orig: RSOUND 0000:048a send_byte driver; also RSOUND 0000:0525; tests: RolandSoundOracleTests
    private void Send(byte value) => _mpu.Send(value);

    private void Send(Controller controller, byte value)
    {
        Send((byte)controller);
        Send(value);
    }

    /// <summary>A status byte that is sent and remembered as the last one.</summary>
    private void SendStatus(MidiStatus status, int channel)
    {
        _lastStatus = (byte)((byte)status | channel);
        Send(_lastStatus);
    }

    /// <summary>A status byte, left out when it is the last one sent (running status).</summary>
    private void SendRunningStatus(MidiStatus status, int channel)
    {
        var value = (byte)((byte)status | channel);
        if (value != _lastStatus)
        {
            Send(value);
            _lastStatus = value;
        }
    }

    /// <summary>A system-exclusive message of the driver's data: the header, the message's bytes up to FFh, its checksum, F7h.</summary>
    // orig: RSOUND 0000:0406 send_sysex driver; also RSOUND 0000:0480; tests: RolandSoundOracleTests
    private void SendSysEx(ushort message)
    {
        for (var at = SysExHeader; _file.Byte(at) != EndOfData; at++)
        {
            Send(_file.Byte(at));
        }
        byte sum = 0;
        for (var at = message; _file.Byte(at) != EndOfData; at++)
        {
            sum += _file.Byte(at);
            Send(_file.Byte(at));
        }
        // Roland's checksum: address and data and it add up to a multiple of 128
        Send((byte)(-sum & 0x7f));
        Send((byte)MidiStatus.EndOfExclusive);
        // DIVERGENCE: D-19 the original waits after each message (the calibrated loop at 0444), for the MT-32 to take it.
    }

    /// <summary>The MT-32's set-up, once: the channels reset, then the display's text, the system settings, the patches and the timbres.</summary>
    // orig: RSOUND 0000:02cd send_set_up driver; tests: RolandSoundOracleTests
    private void SendSetUp()
    {
        if (_setUpSent)
        {
            return;
        }
        ResetChannels();
        foreach (var message in SetUpMessages)
        {
            SendSysEx(message);
        }
        _setUpSent = true;
    }

    /// <summary>Command 0: every voice silent, then channels 10 down to 2 (9 included) reset (notes off, controllers, volume 100, pan centred), and the display's text again.</summary>
    /// <remarks>
    /// The last status remembered is B1h, though the message that follows ends the running status: a channel 2
    /// controller sent next goes out without its status byte (a slip of the original, kept; BUG: CA-52).
    /// </remarks>
    // orig: RSOUND 0000:0acd reset_channels driver; tests: RolandSoundOracleTests
    private void ResetChannels()
    {
        ClearVoices();
        for (var channel = 9; channel >= 1; channel--)
        {
            SendStatus(MidiStatus.ControlChange, channel);
            Send(Controller.AllNotesOff, 0);
            Send(Controller.ResetAllControllers, 0);
            Send(Controller.Volume, 100);
            Send(Controller.Pan, 0x40);
        }
        SendSysEx(DisplayText);
    }

    // ---- the voices ----

    /// <summary>Every voice silent (its ticks and slides 0) and the gunfire's level gone; the tunes play on (there is nothing to play).</summary>
    // orig: RSOUND 0000:0183 clear_voices driver; tests: RolandSoundOracleTests
    private void ClearVoices()
    {
        _gunfireIntensity = _gunfireLevel = _previousLevel = 0;
        foreach (var voice in _voices)
        {
            voice.Ticks = voice.BendStep = voice.VolumeStep = voice.PanStep = 0;
        }
        _tunesPlay = true;
    }

    /// <summary>A voice starts a tune at its next tick: volume 100, bend and pan centred, no slides, repeats or early release.</summary>
    // orig: RSOUND 0000:0546 start_voice driver; tests: RolandSoundOracleTests
    private static void StartVoice(Voice voice, ushort tune, ushort following)
    {
        voice.BendStep = voice.VolumeStep = voice.PanStep = 0;
        voice.VolumePeriod = 0xff;
        voice.Volume = 100;
        voice.Bend = voice.Pan = 0x40;
        voice.Start = voice.Position = voice.InnerStart = voice.OuterStart = tune;
        voice.InnerRepeats = voice.OuterRepeats = 0;
        voice.EarlyRelease = 0;
        voice.FollowingTune = following;
        voice.Ticks = 1;
    }

    /// <summary>Commands 47-54: a voice starts its tune again from its start, with no bend or volume slide (its pan slide, volume, bend and pan stay).</summary>
    // orig: RSOUND 0000:0592 restart_voice driver; also RSOUND 0000:0b13, RSOUND 0000:0b1a, RSOUND 0000:0b21, RSOUND 0000:0b28, RSOUND 0000:0b2f, RSOUND 0000:0b36, RSOUND 0000:0b3d, RSOUND 0000:0b44; tests: RolandSoundOracleTests
    private static void RestartVoice(Voice voice)
    {
        voice.BendStep = 0;
        voice.VolumePeriod = 0xff;
        voice.VolumeStep = 0;
        voice.Position = voice.InnerStart = voice.OuterStart = voice.Start;
        voice.InnerRepeats = voice.OuterRepeats = 0;
        voice.EarlyRelease = 0;
        voice.Ticks = 1;
    }

    /// <summary>Command 1: voices 0-6 go on to the ending tune at the end of their note (voice 7, the rhythm, plays on; suspected BUG: CA-53).</summary>
    // orig: RSOUND 0000:05cb end_tunes driver; tests: RolandSoundOracleTests
    private void EndTunes()
    {
        foreach (var voice in _voices.Take(7))
        {
            voice.Position = Ending;
        }
    }

    /// <summary>One timer interrupt: a tick (the original passes every third interrupt on to the BIOS clock, D-19).</summary>
    // orig: RSOUND 0000:029d timer_interrupt driver; tests: RolandSoundOracleTests
    public void TimerInterrupt()
    {
        if (_handlersInstalled)
        {
            Tick();
        }
    }

    /// <summary>A tick by hand (the driver's slot 103; no part of the game calls it).</summary>
    // orig: RSOUND 0000:0c1c run_tick driver; tests: RolandSoundOracleTests
    public void RunTick() => Tick();

    /// <summary>A tick: the gunfire, the tunes' random number, the voices.</summary>
    // orig: RSOUND 0000:0220 timer_tick driver; tests: RolandSoundOracleTests
    private void Tick()
    {
        _tickCount++;
        if (_gunfireOn)
        {
            GunfireTick();
        }
        NextTuneRandom();
        VoicesTick();
    }

    /// <summary>The tunes' random number: (r + 9248h) rotated right by 3.</summary>
    // orig: RSOUND 0000:020f next_random driver; tests: RolandSoundOracleTests
    private void NextTuneRandom()
    {
        var r = (ushort)(_tuneRandom + 0x9248);
        _tuneRandom = (ushort)(r >> 3 | r << 13);
    }

    /// <summary>Each voice's tick while the tunes play: voices 0-6 on channels 2-8, voice 7 on channel 10.</summary>
    // orig: RSOUND 0000:170e voices_tick driver; tests: RolandSoundOracleTests
    private void VoicesTick()
    {
        if (!_tunesPlay)
        {
            return;
        }
        for (var i = 0; i < 7; i++)
        {
            VoiceTick(_voices[i], i + 1);
        }
        VoiceTick(_voices[7], 9);
    }

    /// <summary>A voice's tick: its early release, then at the end of its note the tune's next events and note; the slides go on even when the voice is silent.</summary>
    // orig: RSOUND 0000:1084 voice_tick driver; tests: RolandSoundOracleTests
    private void VoiceTick(Voice voice, int channel)
    {
        if (voice.Ticks != 0)
        {
            if (voice.ReleaseCountdown != 0 && --voice.ReleaseCountdown == 0)
            {
                NotesOff(channel);
            }
            if (--voice.Ticks == 0)
            {
                ReadTune(voice, channel);
            }
        }
        Slide(voice, channel);
    }

    /// <summary>The tune's events up to its next note (or chord), which starts.</summary>
    private void ReadTune(Voice voice, int channel)
    {
        while (true)
        {
            var at = voice.Position;
            var value = _file.Byte(at);
            if (value < (byte)TuneEvent.RandomByte)
            {
                StartNote(voice, channel);
                return;
            }
            if (ReadEvent(voice, channel, (TuneEvent)value, at))
            {
                return;
            }
        }
    }

    /// <summary>One event at <paramref name="at"/>; whether the tick's reading ends with it (a chord).</summary>
    private bool ReadEvent(Voice voice, int channel, TuneEvent tuneEvent, ushort at)
    {
        byte Operand(int i) => _file.Byte(at + i);
        switch (tuneEvent)
        {
            case TuneEvent.RandomByte:
                {
                    var count = (sbyte)Operand(1);
                    NextTuneRandom();
                    var choice = (sbyte)(byte)((count - 1) & _tuneRandom);
                    var distance = (sbyte)Operand(2 + count);
                    _file.SetByte(at + 3 + count + distance, Operand(2 + choice));
                    voice.Position = (ushort)(at + count + 3);
                    return false;
                }
            case TuneEvent.PanSlide:
                voice.PanPeriod = Operand(1);
                voice.PanStep = Operand(2);
                if (voice.PanStep == 0)
                {
                    SetPan(channel, 0x40);
                }
                voice.PanCountdown = 1;
                voice.Position += 3;
                return false;
            case TuneEvent.Pan:
                voice.Pan = Operand(1);
                SetPan(channel, voice.Pan);
                voice.Position += 2;
                return false;
            case TuneEvent.Chord:
                {
                    var count = (sbyte)Operand(1);
                    var k = 0;
                    for (; k < count; k++)
                    {
                        _heldNotes[4 * channel + k] = Operand(2 + k);
                        NoteOn(channel, _heldNotes[4 * channel + k], voice.Velocity);
                    }
                    for (; k < 4; k++)
                    {
                        _heldNotes[4 * channel + k] = 0xff;
                    }
                    voice.Ticks = Operand(2 + count);
                    voice.ReleaseCountdown = (byte)(voice.Ticks - (sbyte)voice.EarlyRelease);
                    voice.Position = (ushort)(voice.Position + count + 3);
                    return true;
                }
            case TuneEvent.Volume:
                voice.Volume = Operand(1);
                voice.Position += 2;
                SetVolume(channel, voice.Volume);
                return false;
            case TuneEvent.Bend:
                voice.Bend = Operand(1);
                voice.Position += 2;
                SetBend(channel, voice.Bend);
                return false;
            case TuneEvent.VolumeSlide:
                voice.VolumePeriod = Operand(1);
                voice.VolumeStep = Operand(2);
                voice.VolumeCountdown = 1;
                voice.Position += 3;
                return false;
            case TuneEvent.Velocity:
                voice.Velocity = Operand(1);
                voice.Position += 2;
                return false;
            case TuneEvent.BendSlide:
                voice.BendPeriod = Operand(1);
                voice.BendStep = Operand(2);
                voice.BendTicks = Operand(3);
                voice.BendCountdown = 1;
                voice.Position += 4;
                return false;
            case TuneEvent.EarlyRelease:
                voice.EarlyRelease = Operand(1);
                voice.Position += 2;
                return false;
            case TuneEvent.Program:
                voice.Program = Operand(1);
                voice.Position += 2;
                ProgramChange(channel, Operand(1));
                return false;
            case TuneEvent.End:
                if (voice.FollowingTune == 0)
                {
                    voice.Position = voice.Start;
                }
                else
                {
                    voice.Start = voice.Position = voice.InnerStart = voice.OuterStart = voice.FollowingTune;
                }
                return false;
            case TuneEvent.OuterRepeat:
                if (voice.OuterRepeats == 0)
                {
                    if (Operand(1) == 0)
                    {
                        voice.Position += 2;
                        voice.OuterStart = voice.Position;
                        voice.InnerRepeats = voice.OuterRepeats = 0;
                    }
                    else
                    {
                        voice.OuterRepeats = (ushort)(sbyte)Operand(1);
                        voice.Position = voice.OuterStart;
                    }
                }
                else if (--voice.OuterRepeats == 0)
                {
                    voice.Position += 2;
                    voice.OuterStart = voice.InnerStart = voice.Position;
                }
                else
                {
                    voice.Position = voice.OuterStart;
                }
                return false;
            default: // InnerRepeat
                if (voice.InnerRepeats == 0)
                {
                    if (Operand(1) == 0)
                    {
                        voice.Position += 2;
                        voice.InnerStart = voice.Position;
                        voice.InnerRepeats = 0;
                    }
                    else
                    {
                        voice.InnerRepeats = (ushort)(sbyte)Operand(1);
                        voice.Position = voice.InnerStart;
                    }
                }
                else if (--voice.InnerRepeats == 0)
                {
                    voice.Position += 2;
                    voice.InnerStart = voice.Position;
                }
                else
                {
                    voice.Position = voice.InnerStart;
                }
                return false;
        }
    }

    /// <summary>
    /// The note at the voice's place: its number and length. A rest or a length of 0 ends the notes the channel holds
    /// (length 0: the voice stops); otherwise the note sounds, unless the early release is negative (a tie) and the
    /// channel's first note is this one already.
    /// </summary>
    private void StartNote(Voice voice, int channel)
    {
        voice.Note = _file.Byte(voice.Position);
        voice.Ticks = _file.Byte(voice.Position + 1);
        voice.Position += 2;
        if (voice.Note == 0 || voice.Ticks == 0)
        {
            NotesOff(channel);
            return;
        }
        voice.ReleaseCountdown = (byte)(voice.Ticks - (sbyte)voice.EarlyRelease);
        if ((sbyte)voice.EarlyRelease >= 0 || _heldNotes[4 * channel] != voice.Note)
        {
            NoteOn(channel, voice.Note, voice.Velocity);
        }
        _heldNotes[4 * channel] = voice.Note;
    }

    /// <summary>The volume, bend and pan slides of a voice; the bend's slide lasts its number of ticks.</summary>
    private void Slide(Voice voice, int channel)
    {
        if (voice.VolumeStep != 0 && --voice.VolumeCountdown == 0)
        {
            voice.VolumeCountdown = voice.VolumePeriod;
            voice.Volume += voice.VolumeStep;
            SetVolume(channel, voice.Volume);
        }
        if (voice.BendStep != 0)
        {
            if (--voice.BendCountdown == 0)
            {
                voice.BendCountdown = voice.BendPeriod;
                voice.Bend += voice.BendStep;
                SetBend(channel, voice.Bend);
            }
            if (--voice.BendTicks == 0)
            {
                voice.BendStep = 0;
            }
        }
        if (voice.PanStep != 0 && --voice.PanCountdown == 0)
        {
            voice.PanCountdown = voice.PanPeriod;
            voice.Pan += voice.PanStep;
            SetPan(channel, voice.Pan);
        }
    }

    // ---- MIDI messages ----

    /// <summary>The notes a channel holds off (note on, velocity 0), up to the first free place.</summary>
    // orig: RSOUND 0000:0e02 notes_off driver; tests: RolandSoundOracleTests
    private void NotesOff(int channel)
    {
        for (var k = 0; k < 4 && _heldNotes[4 * channel + k] != 0xff; k++)
        {
            SendRunningStatus(MidiStatus.NoteOn, channel);
            Send(_heldNotes[4 * channel + k]);
            Send(0);
            _heldNotes[4 * channel + k] = 0xff;
        }
    }

    // orig: RSOUND 0000:0eba note_on driver; tests: RolandSoundOracleTests
    private void NoteOn(int channel, byte note, byte velocity)
    {
        SendRunningStatus(MidiStatus.NoteOn, channel);
        Send(note);
        Send(velocity);
    }

    // orig: RSOUND 0000:0f1a program_change driver; tests: RolandSoundOracleTests
    private void ProgramChange(int channel, byte program)
    {
        SendRunningStatus(MidiStatus.ProgramChange, channel);
        Send(program);
    }

    // orig: RSOUND 0000:0f6c set_volume driver; tests: RolandSoundOracleTests
    private void SetVolume(int channel, byte volume)
    {
        SendRunningStatus(MidiStatus.ControlChange, channel);
        Send(Controller.Volume, volume);
    }

    /// <summary>The pitch bend: its low byte 0, its high byte <paramref name="bend"/>.</summary>
    // orig: RSOUND 0000:0fca set_bend driver; tests: RolandSoundOracleTests
    private void SetBend(int channel, byte bend)
    {
        SendRunningStatus(MidiStatus.PitchBend, channel);
        Send(0);
        Send(bend);
    }

    // orig: RSOUND 0000:1026 set_pan driver; tests: RolandSoundOracleTests
    private void SetPan(int channel, byte pan)
    {
        SendRunningStatus(MidiStatus.ControlChange, channel);
        Send(Controller.Pan, pan);
    }

    // ---- pause ----

    /// <summary>Command 37: the tunes stop, every channel of the voices silent (volume 0, notes off).</summary>
    /// <remarks>The status bytes are sent but not remembered: the next message may leave out a status it needs (kept).</remarks>
    // orig: RSOUND 0000:0a21 pause_sound driver; also RSOUND 0000:0a0a; tests: RolandSoundOracleTests
    private void Pause()
    {
        _tunesPlay = false;
        foreach (var channel in VoiceChannels())
        {
            Send((byte)((byte)MidiStatus.ControlChange | channel));
            Send(Controller.Volume, 0);
            Send(Controller.AllNotesOff, 0);
        }
    }

    /// <summary>Command 38: each voice's channel gets its volume back, and the tunes go on.</summary>
    // orig: RSOUND 0000:0a65 resume_sound driver; also RSOUND 0000:0a58; tests: RolandSoundOracleTests
    private void Resume()
    {
        var i = 0;
        foreach (var channel in VoiceChannels())
        {
            Send((byte)((byte)MidiStatus.ControlChange | channel));
            Send(Controller.Volume, _voices[i++].Volume);
        }
        _tunesPlay = true;
    }

    private static int[] VoiceChannels() => [1, 2, 3, 4, 5, 6, 7, 9];

    // ---- the engines ----

    /// <summary>
    /// Commands 26 and 27: a car's engine in a gear, a note on channel 8 two semitones higher a gear; gear 0 ends it.
    /// A new gear or car ends the note before and starts the new one (after the engine's patch, when no gear was in).
    /// </summary>
    /// <remarks>Gear 0 with no engine note sends the note FFh (a system reset byte) and 0: a slip of the original, kept (BUG: CA-51).</remarks>
    // orig: RSOUND 0000:097b set_engine_gear driver; also RSOUND 0000:0966, RSOUND 0000:0972; tests: RolandSoundOracleTests
    private void SetEngineGear(ushort baseNote, ushort gear)
    {
        _engineBase = baseNote;
        _gear = gear;
        if (gear == 0)
        {
            SendStatus(MidiStatus.NoteOn, EngineChannel);
            Send((byte)_engineNote);
            Send(0);
            _engineNote = NoEngine;
            _engineBase = NoEngine;
        }
        else if (_engineBase != _previousEngineBase || gear != _previousGear)
        {
            if (_previousGear == 0)
            {
                SendStatus(MidiStatus.ProgramChange, EngineChannel);
                Send(EnginePatch);
            }
            SendStatus(MidiStatus.NoteOn, EngineChannel);
            if (_engineNote != NoEngine)
            {
                Send((byte)_engineNote);
                Send(0);
            }
            _engineNote = (ushort)(2 * _gear + _engineBase);
            Send((byte)_engineNote);
            Send(EngineVelocity);
        }
        _previousGear = _gear;
        _previousEngineBase = _engineBase;
    }

    /// <summary>Command 35: the aeroplane's engine (the jet plane's patch on channel 8, its note).</summary>
    // orig: RSOUND 0000:08b8 take_off driver; tests: RolandSoundOracleTests
    private void TakeOff()
    {
        SendStatus(MidiStatus.ProgramChange, EngineChannel);
        Send(PlanePatch);
        SendStatus(MidiStatus.NoteOn, EngineChannel);
        Send(PlaneNote);
        Send(PlaneVelocity);
    }

    /// <summary>Command 36: the aeroplane's note off.</summary>
    // orig: RSOUND 0000:08d9 land driver; tests: RolandSoundOracleTests
    private void Land()
    {
        SendStatus(MidiStatus.NoteOn, EngineChannel);
        Send(PlaneNote);
        Send(0);
    }

    // ---- the gunfire ----

    /// <summary>Command 44: the raid's gunfire at a level (1-5) and an intensity; 0 for either ends it at the next tick.</summary>
    // orig: RSOUND 0000:093a start_gunfire driver; tests: RolandSoundOracleTests
    private void StartGunfire(ushort level, ushort intensity)
    {
        _gunfireLevel = level;
        _gunfireIntensity = intensity;
        _gunfireOn = true;
    }

    /// <summary>The gunfire's tick: single shots and a rhythm at levels 1-3, a beat of shots from level 4, a barrage at 5.</summary>
    // orig: RSOUND 0000:0cc6 gunfire_tick driver; tests: RolandSoundOracleTests
    private void GunfireTick()
    {
        if (_gunfireLevel == 0 || _gunfireIntensity == 0)
        {
            StopGunfire();
            return;
        }
        _ = NextGunfireRandom(carry: false);
        if (_gunfireLevel >= HeavyLevel)
        {
            HeavyGunfire();
        }
        else
        {
            LightGunfire();
        }
        _previousLevel = _gunfireLevel;
    }

    /// <summary>The gunfire ends: voices 0-2 go on to the ending tune.</summary>
    // orig: RSOUND 0000:0c86 stop_gunfire driver; tests: RolandSoundOracleTests
    private void StopGunfire()
    {
        _gunfireOn = false;
        foreach (var voice in _voices.Take(3))
        {
            voice.Position = Ending;
        }
        _previousLevel = 0;
    }

    /// <summary>
    /// The gunfire's random number: r + 7131h + the carry the caller leaves, + the tick count with the carry of that.
    /// The carry is 0 but for the rhythm's choice at levels from 4097 up.
    /// </summary>
    // orig: RSOUND 0000:0c3b next_gunfire_random driver; tests: RolandSoundOracleTests
    private ushort NextGunfireRandom(bool carry)
    {
        var sum = _gunfireRandom + 0x7131 + (carry ? 1 : 0);
        sum = (sum & 0xffff) + _tickCount + (sum >> 16);
        _gunfireRandom = (ushort)sum;
        return _gunfireRandom;
    }

    /// <summary>Levels 1-3: the beat and barrage of levels 4-5 end, the rhythm goes on, its gap chosen anew.</summary>
    // orig: RSOUND 0000:0cee light_gunfire driver; tests: RolandSoundOracleTests
    private void LightGunfire()
    {
        if (_previousLevel >= HeavyLevel)
        {
            EndShots();
            EndBarrage();
        }
        KeepRhythm();
        ChooseShotGap();
    }

    /// <summary>Levels 4 and 5: the shots on voice 0 on a beat; at 4 the rhythm goes on, at 5 the barrage on voices 1 and 2.</summary>
    // orig: RSOUND 0000:0d09 heavy_gunfire driver; tests: RolandSoundOracleTests
    private void HeavyGunfire()
    {
        KeepShooting();
        if (_beatCountdown == 0)
        {
            _beatCountdown = _beatLength;
        }
        _beatCountdown--;
        if (_gunfireLevel != BarrageLevel)
        {
            if (_previousLevel != HeavyLevel && _previousLevel == BarrageLevel)
            {
                EndBarrage();
            }
            KeepRhythm();
            ChooseShotGap();
            return;
        }
        if (_previousLevel != BarrageLevel)
        {
            SetShotSound(BarrageSound);
            StartVoice(_voices[1], RhythmTune, following: BarrageFollowUp);
            StartVoice(_voices[2], RhythmTune, following: 0);
            var gap = (short)(_beatCountdown - 1);
            SetRhythmGap((byte)(gap < 0 ? _beatLength : (ushort)gap));
        }
        if (_voices[2].Ticks == 1)
        {
            ChooseRhythm();
        }
    }

    /// <summary>Voice 0 shoots: when silent it starts the shot's tune on a new beat; on its last tick it chooses the next shot's tune.</summary>
    // orig: RSOUND 0000:0c4a keep_shooting driver; tests: RolandSoundOracleTests
    private void KeepShooting()
    {
        var voice = _voices[0];
        // the original compares the voice's ticks and bend step as one word
        var ticksAndBend = voice.Ticks | voice.BendStep << 8;
        if (ticksAndBend == 0)
        {
            _beatLength = ShotBeatLength;
            _beatCountdown = FirstShotBeat;
            SetShotSound(SingleShotSound);
            StartVoice(voice, ShotTune, following: 0);
        }
        else if (ticksAndBend == 1)
        {
            voice.Start = _file.Word(ShotChoices + (_gunfireRandom & 6));
        }
    }

    /// <summary>Voice 0's shots end at the end of their note.</summary>
    // orig: RSOUND 0000:0ca2 end_shots driver; tests: RolandSoundOracleTests
    private void EndShots() => _voices[0].Position = Ending;

    /// <summary>Voices 1 and 2 end at the end of their note, and the shots sound single again.</summary>
    // orig: RSOUND 0000:0cab end_barrage driver; tests: RolandSoundOracleTests
    private void EndBarrage()
    {
        _voices[1].Position = _voices[2].Position = Ending;
        SetShotSound(SingleShotSound);
    }

    /// <summary>The byte of the four shots of the shot's tune that makes them single shots or a barrage.</summary>
    // orig: RSOUND 0000:0cb9 set_shot_sound driver; also RSOUND 0000:0cb7; tests: RolandSoundOracleTests
    private void SetShotSound(byte sound)
    {
        foreach (var at in ShotSoundBytes)
        {
            _file.SetByte(at, sound);
        }
    }

    /// <summary>When voice 2 is silent it starts the rhythm again, and chooses the tune it goes on with.</summary>
    // orig: RSOUND 0000:0d7d keep_rhythm driver; tests: RolandSoundOracleTests
    private void KeepRhythm()
    {
        if (_voices[2].Ticks != 0)
        {
            return;
        }
        StartVoice(_voices[2], RhythmTune, following: 0);
        ChooseRhythm();
    }

    /// <summary>Voice 2's next tune at random among those of the level (16 at level 1, 8 at the others).</summary>
    // orig: RSOUND 0000:0d91 choose_rhythm driver; tests: RolandSoundOracleTests
    private void ChooseRhythm()
    {
        var row = (ushort)((_gunfireLevel - 1) << 4);
        // the carry the original's shifts leave: bit 12 of the level less 1
        var random = NextGunfireRandom(carry: ((_gunfireLevel - 1) & 0x1000) != 0);
        var choice = _gunfireLevel == 1 ? FirstLevelRhythms + row + (random & 0x1e) : Rhythms + row + (random & 0x0e);
        _voices[2].Start = _file.Word(choice & 0xffff);
    }

    /// <summary>The rhythm's gap: at random, shorter the higher the level, longer with the intensity (20 ticks a step), 255 at most.</summary>
    // orig: RSOUND 0000:0dc6 choose_shot_gap driver; tests: RolandSoundOracleTests
    private void ChooseShotGap()
    {
        var random = (ushort)(((_gunfireRandom & 0xff) + 1) / _gunfireLevel | 1);
        var gap = (short)(ushort)(_gunfireIntensity * ShotGapPerIntensity + random);
        SetRhythmGap((byte)(gap > 0xff ? 0xff : gap));
    }

    // orig: RSOUND 0000:0dec set_rhythm_gap driver; tests: RolandSoundOracleTests
    private void SetRhythmGap(byte gap) => _file.SetByte(RhythmGap, gap);

    /// <summary>A voice (DS:2da2 + 22h a voice). Behind the file's end: 0 until the driver sets it (U-9).</summary>
    private sealed class Voice
    {
        public byte Ticks;              // +00 ticks left of the note; 0: the voice is silent
        public byte BendStep;           // +01 added to the bend each bend period
        public byte VolumeStep;         // +02 added to the volume each volume period
        public byte PanStep;            // +03 added to the pan each pan period
        public byte Note;               // +04
        public byte Program;            // +05
        public byte Velocity;           // +06
        public byte EarlyRelease;       // +07 (signed)
        public byte ReleaseCountdown;   // +08 ticks to the note's release
        public byte VolumeCountdown;    // +09
        public byte BendCountdown;      // +0a
        public byte PanCountdown;       // +0b
        public byte Volume;             // +0c
        public byte Bend;               // +0d the pitch bend's high byte
        public byte Pan;                // +0e
        public byte VolumePeriod;       // +0f
        public byte BendPeriod;         // +10
        public byte PanPeriod;          // +11
        public byte BendTicks;          // +12 ticks the bend slide lasts
        public ushort Start;            // +14 where the tune starts again at its end
        public ushort Position;         // +16 the next byte of the tune
        public ushort InnerStart;       // +18
        public ushort OuterStart;       // +1a
        public ushort InnerRepeats;     // +1c repeats left
        public ushort OuterRepeats;     // +1e
        public ushort FollowingTune;    // +20 the tune played at the end instead (0: this one again)
    }
}
