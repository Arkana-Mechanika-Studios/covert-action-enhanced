namespace CovertAction.Audio.Tandy;

/// <summary>
/// The original's Tandy sound driver (TSOUND.CA, "Tandy Covert10-09-90") as C#: its 47 commands and what it does on
/// each timer interrupt, writing bytes to the SN76496 chip (<see cref="ISoundChip"/>). The tunes and effects are bytes
/// of the player's driver file (<see cref="Content.TandyDriverFile"/>); which command starts which of them, and how a
/// note is played, is the driver's code, here.
/// </summary>
/// <remarks>
/// <para>
/// The driver has five <b>voices</b>: one for each of the chip's three tone channels and its noise channel, and a fifth,
/// the <b>overlay</b>, that a command points at any one of the four channels (the car chase's engine and the effects
/// that cut in over a tune use it; both voices then write to the channel and the overlay, written last, wins). Each
/// voice reads a tune: <b>notes</b> (a duration in ticks, below 80h, and a period for the chip, 0FFFFh for a rest) with
/// <b>commands</b> (80h and above) in front of them that set the volume, a fade-out, a slide of the period, a wobble
/// of the volume, loops, and the like. A tune ends with a duration of 0.
/// </para>
/// <para>
/// A tick is one timer interrupt (60.88 a second): every voice counts down its note, fades, slides and wobbles, and
/// writes the channel's attenuation and period to the chip again. A voice that has not started, or has ended, is
/// silent and costs nothing. The driver also runs a state machine for the building raid's gunfire (command 44, level
/// and intensity), which starts and ends tunes of the first three voices and patches their volume bytes, and a random
/// number (its seed, the tick count and constants) that the engine, the gunfire and the tunes' random bytes use.
/// </para>
/// <para>
/// The driver changes bytes of its own tunes as it plays (random pitches, a gear, the gunfire's volume); the C# does
/// the same on its copy of the data segment. Words wrap as the original's 16-bit words do.
/// </para>
/// </remarks>
public sealed class TandySoundDriver : ISoundProgram
{
    /// <summary>The timer's divisor: 60.88 interrupts a second, one tick each.</summary>
    public const int InterruptDivisor = 0x4c90;

    private const int Commands = 47;

    /// <summary>The attenuation command of each channel at attenuation 3: the byte a voice starts with.</summary>
    private const byte Tone1Channel = 0x93, Tone2Channel = 0xb3, Tone3Channel = 0xd3, NoiseChannel = 0xf3;

    /// <summary>A note's period that makes a rest.</summary>
    private const ushort Rest = 0xffff;

    /// <summary>A tune of one byte, 0: it ends the voice at its next note.</summary>
    private const int TuneEnd = 0x180c;

    /// <summary>Where the gunfire's two note bytes are (patched with the gunfire's volume), and the volume bytes of the first burst's tune.</summary>
    private const int GunfireNote = 0x105b, GunfireNoteEnd = 0x105e;

    private static readonly int[] GunfireVolumeBytes = [0x15b4, 0x15d3, 0x15f2, 0x1611];

    // the commands in front of a note. orig: TSOUND 0000:0483
    private const byte SetVolume = 0xc0, FadeOut = 0xa0, Slide = 0x90, Repeat = 0x80, InnerLoop = 0xf3, FadeFrom = 0xb0, FadeTo = 0xd0,
        Wobble = 0xf1, WobbleOff = 0xf2, SlideOff = 0xe0, RandomByte = 0xf5, EffectsOff = 0xf4;

    /// <summary>The chip's registers that silence it and set its start-up periods: what the driver writes to reset it.</summary>
    private static readonly byte[] ChipReset = [0x82, 0x00, 0xa3, 0x00, 0xc4, 0x00, 0xe3, 0x9f, 0xbf, 0xdf, 0xff];

    private static readonly Song Title = new(0x0f8, 0x2b7, 0x3da, 0x503);

    private static readonly Dictionary<int, Song> Songs = new()
    {
        [39] = new(0xf0a, 0xf6e, 0xfd2, 0x102a),
        [40] = new(0x82b, 0x88f, 0x8f3, 0x90b),
        [41] = new(0xbf7, 0xd11, 0xd93, 0xe71),
        [42] = new(0x5f8, 0x67f, 0x702, 0x7dc),
        [43] = new(0x932, 0xad6, 0xb49, 0xbc2),
    };

    private readonly byte[] _data;
    private readonly ISoundChip _chip;
    private readonly ISoundTimer _timer;
    private readonly Voice _tone1 = new(), _tone2 = new(), _tone3 = new(), _noise = new(), _overlay = new();
    private readonly Voice[] _voices;

    // orig: TSOUND DS:00ee (the random seed), CS:00f5 (ticks counted), DS:00e1 (the noise register last written)
    private ushort _seed;
    private ushort _tickCount;
    private byte _noiseWritten;
    private bool _installed;

    // the gunfire. orig: TSOUND DS:00e8 (level), DS:00ea (intensity), DS:00ec (on), DS:00ed (on, kept by a pause), DS:00e6 (level at the last tick),
    // DS:00e2 and DS:00e4 (the volume's count and its start)
    private ushort _gunfireLevel, _gunfireIntensity, _gunfirePreviousLevel, _gunfireCount, _gunfireCountStart;
    private bool _gunfireOn, _gunfireOnKept;

    /// <summary>The pause is on: no resume has come since the last pause (new code, for <see cref="IgnoreSecondPause"/>).</summary>
    private bool _paused;

    /// <summary>
    /// CA-55: a second pause with no resume between is ignored. Off (the default): it saves the silenced voices over the
    /// saved ones, as the original driver does, and the resume brings back no music. Set by the launcher's option "Fix
    /// the original's bugs"; the game always resumes after a pause, so only a test can tell.
    /// </summary>
    public bool IgnoreSecondPause { get; init; }

    /// <summary>Makes the driver from a copy of the player's TSOUND.CA data segment, playing on <paramref name="chip"/>.</summary>
    /// <param name="data">The driver file's data segment (<see cref="Content.TandyDriverFile.Segment"/>), changed as the driver plays.</param>
    /// <param name="chip">The sound chip.</param>
    /// <param name="timer">The timer interrupt's rate.</param>
    public TandySoundDriver(byte[] data, ISoundChip chip, ISoundTimer timer)
    {
        _data = data;
        _chip = chip;
        _timer = timer;
        _voices = [_tone1, _tone2, _tone3, _noise, _overlay];
    }

    /// <summary>The driver on a chip voice of its own, as the game's sound driver on a device of <paramref name="sampleRate"/>, started.</summary>
    /// <param name="file">The whole of TSOUND.CA.</param>
    /// <param name="sampleRate">The device's frames per second.</param>
    /// <param name="clock">Real time, for a caller that waits (the Tandy driver never makes the game wait).</param>
    /// <param name="watch">Told of every byte to the chip and every rate and interrupt (for tests and tools); null: nothing is told.</param>
    /// <param name="ignoreSecondPause">CA-55 fixed: see <see cref="IgnoreSecondPause"/>.</param>
    public static ClockedSoundDriver OnChip(ReadOnlySpan<byte> file, int sampleRate, CovertAction.Platform.IClock clock, Action<SoundDeviceEvent>? watch = null, bool ignoreSecondPause = false)
    {
        var data = Content.TandyDriverFile.Segment(file);
        return new(sampleRate, clock, timer =>
        {
            var chip = new Synthesis.Sn76496Voice(sampleRate);
            return (new TandySoundDriver(data, WatchedDevice.Chip(chip, watch), timer) { IgnoreSecondPause = ignoreSecondPause }, chip);
        }, watch);
    }

    /// <summary>Ticks since the driver started (the driver's slot 105 tells the game; no part of it asks).</summary>
    // orig: TSOUND 0000:010a read_tick_count driver; tests: TandySoundOracleTests
    public int ReadTickCount() => _tickCount;

    /// <inheritdoc/>
    public bool IsQuiet => !_gunfireOn && _voices.All(v => v.TicksLeft == 0);

    private byte Byte(int offset) => _data[offset & 0xffff];

    private ushort Word(int offset) => (ushort)(Byte(offset) | Byte(offset + 1) << 8);

    private void SetByte(int offset, byte value) => _data[offset & 0xffff] = value;

    private void SetWord(int offset, ushort value)
    {
        SetByte(offset, (byte)value);
        SetByte(offset + 1, (byte)(value >> 8));
    }

    /// <inheritdoc/>
    // orig: TSOUND 0000:00a6 init_driver driver; tests: TandySoundOracleTests
    public void Start()
    {
        ClearAndSilence();
        // DIVERGENCE: D-21 the Tandy BIOS (int 1Ah, AH=81h) is taken as answered: the chip is there; its answer (DS:00f4) is never read.
        if (!_installed)
        {
            InstallInterrupt();
        }
    }

    /// <inheritdoc/>
    // orig: TSOUND 0000:010f shutdown_driver driver; also TSOUND 0000:09d8; tests: TandySoundOracleTests
    public void Stop()
    {
        ClearAndSilence();
        if (_installed)
        {
            // the PC's own handler back, and its rate (divisor 0: 65,536)
            _installed = false;
            _timer.SetInterruptDivisor(0);
        }
    }

    /// <summary>Puts the driver's timer interrupt in and sets its rate, once.</summary>
    // orig: TSOUND 0000:0998 install_timer_handler driver; tests: TandySoundOracleTests
    private void InstallInterrupt()
    {
        _installed = true;
        _timer.SetInterruptDivisor(InterruptDivisor);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">A negative command: the original calls through a word in front of its table.</exception>
    // orig: TSOUND 0000:00d8 play_sound driver; tests: TandySoundOracleTests
    public bool Play(int command, int parameter, int variant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(command);
        if (command < Commands)
        {
            Carry(command, (ushort)parameter, (ushort)variant);
        }
        return false;
    }

    /// <summary>The command table (driver words 0048h-00a5h): the routine each command jumps to, with the tunes it starts.</summary>
    private void Carry(int command, ushort parameter, ushort variant)
    {
        switch (command)
        {
            case 0: ClearAndSilence(); break;                                                      // 0618
            case 1: foreach (var voice in _voices) { voice.Next = TuneEnd; } break;                 // 094f: every voice ends at its note's end
            case 2: PlaySong(Title); break;                                                        // 082e
            case >= 39 and <= 43: PlaySong(Songs[command]); break;                                // 06a6, 06fa, 06c2, 0716, 06de
            case 3: StartNoise(0x17b2, innerLoopStart: 0x17ac); break;                             // 07c5
            case 4: StartNoise(0x17be, innerLoopStart: 0x17ac); break;                             // 07b5
            case 33: StartNoise(0x17b2, innerLoopStart: TuneEnd); break;                           // 07cf
            case 34: StartNoise(0x17be, innerLoopStart: TuneEnd); break;                           // 07bb
            case 5: StartVoice(_noise, NoiseChannel, 0x16f9); break;                               // 07ae
            case 6: StartVoice(_noise, NoiseChannel, 0x179e); break;                               // 07a7
            case 7: StartVoice(_noise, NoiseChannel, 0x1802); break;                               // 07e6
            case 8: StartVoice(_noise, NoiseChannel, 0x17fa); break;                               // 07df
            case 9: StartVoice(_noise, NoiseChannel, 0x1755); break;                               // 076e
            case 11: StartVoice(_noise, NoiseChannel, 0x16d5); break;                              // 0827
            case 28: StartVoice(_noise, NoiseChannel, 0x16e1); break;                              // 0675
            case 29: StartVoice(_noise, NoiseChannel, 0x16ed); break;                              // 066e
            case 31: StartVoice(_noise, NoiseChannel, 0x1705); break;                              // 0683
            case 32: StartVoice(_noise, NoiseChannel, 0x16fd); break;                              // 067c
            case 10: StartVoice(_overlay, Tone3Channel, 0x16c9); break;                            // 0820
            case 14: StartVoice(_overlay, Tone3Channel, 0x1761); break;                            // 0775
            case 16: StartVoice(_overlay, Tone3Channel, 0x16a1); break;                            // 074e
            case 17: StartVoice(_overlay, Tone3Channel, 0x1695); break;                            // 0755
            case 12:                                                                               // 0740: the second start ends the first
                StartVoice(_overlay, Tone2Channel, 0x17ca);
                StartVoice(_overlay, Tone3Channel, 0x17d4);
                break;
            case 13: PlayWithOverlay(gearBytes: (8, 2, 8, 0x0a)); break;                           // 0785
            case 15: PlayWithOverlay(gearBytes: (0x0c, 7, 0x0c, 0x0e)); break;                     // 077c
            case 18: SetByte(0x1815, 0x00); StartVoice(_overlay, Tone3Channel, 0x180d); break;     // 0761
            case 30: SetByte(0x1815, 0xff); StartVoice(_overlay, Tone3Channel, 0x180d); break;     // 075c
            case 19: StartVoice(_tone1, Tone1Channel, 0x17ec); break;                              // 0739
            case 20: StartVoice(_tone1, Tone1Channel, 0x17de); break;                              // 0732
            case 21 or 22 or 24 or 25: break;                                                      // 0130: nothing
            case 23: IdleEngine(startsTone1: false); break;                                        // 085c
            case 26: CarEngine(0x0018, parameter); break;                                          // 088c
            case 27: CarEngine(0x0200, parameter); break;                                          // 0892
            case 35: StartVoice(_tone3, Tone3Channel, 0x172b); StartVoice(_noise, NoiseChannel, 0x1737); break;   // 068a
            case 36: StartVoice(_tone3, Tone3Channel, 0x1715); StartVoice(_noise, NoiseChannel, 0x1721); break;   // 0698
            case 37: Pause(); break;                                                               // 08ed
            case 38: Resume(); break;                                                              // 092a
            case 44: (_gunfireLevel, _gunfireIntensity, _gunfireOn) = (parameter, variant, true); break;   // 08db
            case 45: StartVoice(_tone3, Tone3Channel, 0x16b8); break;                              // 0819
            case 46: PlayDelayedTone(); break;                                                     // 07ed
        }
    }

    private void PlaySong(Song song)
    {
        StartVoice(_tone1, Tone1Channel, song.Tone1);
        StartVoice(_tone2, Tone2Channel, song.Tone2);
        StartVoice(_tone3, Tone3Channel, song.Tone3);
        StartVoice(_noise, NoiseChannel, song.Noise);
    }

    private void StartNoise(int tune, int innerLoopStart)
    {
        StartVoice(_noise, NoiseChannel, tune);
        _noise.InnerLoopStart = (ushort)innerLoopStart;
    }

    /// <summary>Commands 13 and 15: four bytes of a tune take the effect's values, then it sounds on the overlay and the noise voice.</summary>
    // orig: TSOUND 0000:078b start_patched_effect driver; tests: TandySoundOracleTests
    private void PlayWithOverlay((byte A, byte B, byte C, byte D) gearBytes)
    {
        SetByte(0x1789, gearBytes.A);
        SetByte(0x1790, gearBytes.B);
        SetByte(0x1799, gearBytes.C);
        SetByte(0x1794, gearBytes.D);
        StartVoice(_overlay, Tone3Channel, 0x1770);
        StartVoice(_noise, NoiseChannel, 0x1788);
    }

    /// <summary>Command 46: one tune on tone channels 2 and 3, the second held back for 21 ticks as a rest.</summary>
    private void PlayDelayedTone()
    {
        StartVoice(_tone3, Tone3Channel, 0x1062);
        StartVoice(_tone2, Tone2Channel, 0x1062);
        _tone3.InnerLoopStart = 0x1151;
        _tone2.InnerLoopStart = 0x117c;
        _tone2.TicksLeft = 0x15;
        _tone2.Period = Rest;
    }

    /// <summary>Command 0 and the driver's start and end: every voice and the gunfire cleared, the chip silent.</summary>
    // orig: TSOUND 0000:0618 reset_chip driver; also TSOUND 0000:011d, TSOUND 0000:064e, TSOUND 0000:0654; tests: TandySoundOracleTests
    private void ClearAndSilence()
    {
        foreach (var voice in _voices)
        {
            voice.Clear();
        }
        _seed = _noiseWritten = 0;
        (_gunfireCount, _gunfireCountStart, _gunfirePreviousLevel, _gunfireLevel, _gunfireIntensity) = (0, 0, 0, 0, 0);
        _gunfireOn = _gunfireOnKept = false;
        _paused = false;
        SilenceChip();
    }

    /// <summary>The bytes that mute the chip's four channels (and set the start-up periods).</summary>
    // orig: TSOUND 0000:0621 silence_chip driver; tests: TandySoundOracleTests
    private void SilenceChip()
    {
        foreach (var value in ChipReset)
        {
            _chip.Write(value);
        }
    }

    /// <summary>The voices stop where they are (their ticks kept in their spare bytes) and the chip is muted.</summary>
    // orig: TSOUND 0000:08ed pause_sound driver; tests: TandySoundOracleTests
    private void Pause()
    {
        // BUG: CA-55 a second pause in a row saves the voices the first one silenced; IgnoreSecondPause leaves it out
        if (IgnoreSecondPause && _paused)
        {
            return;
        }
        _paused = true;
        _gunfireOnKept = _gunfireOn;
        _gunfireOn = false;
        foreach (var voice in _voices)
        {
            voice.SavedTicks = voice.TicksLeft;
            voice.TicksLeft = 0;
        }
        SilenceChip();
    }

    /// <summary>The voices go on with the ticks the pause kept (a second pause in a row kept none, unless <see cref="IgnoreSecondPause"/>).</summary>
    // orig: TSOUND 0000:092a resume_sound driver; tests: TandySoundOracleTests
    private void Resume()
    {
        _paused = false;
        _gunfireOn = _gunfireOnKept;
        foreach (var voice in _voices)
        {
            voice.TicksLeft = voice.SavedTicks;
        }
    }

    // ---- starting and ending a voice ----

    /// <summary>Starts a tune on a voice: what played on it ends, and its first note comes with the next tick.</summary>
    // orig: TSOUND 0000:05e8 start_voice driver; also TSOUND 0000:05c7, TSOUND 0000:05d0, TSOUND 0000:05d9, TSOUND 0000:05e2, TSOUND 0000:05c0, TSOUND 0000:065a, TSOUND 0000:065f, TSOUND 0000:0664, TSOUND 0000:0669; tests: TandySoundOracleTests
    private void StartVoice(Voice voice, byte channel, int tune)
    {
        EndVoice(voice);
        voice.Base = channel;
        voice.InnerLoopStart = voice.OuterLoopStart = voice.Start = voice.Next = (ushort)tune;
        voice.FadeFrom = 0xff;
        voice.FadeTarget = 0x0f;
        voice.TicksLeft = 1;
    }

    /// <summary>The voice stops: its channel is muted if it sounded, and all it knew is cleared.</summary>
    // orig: TSOUND 0000:0439 end_voice driver; tests: TandySoundOracleTests
    private void EndVoice(Voice voice)
    {
        if (voice.Current != 0)
        {
            _chip.Write((byte)(voice.Current | 0x9f));
        }
        voice.Clear();
    }

    // ---- the tick ----

    /// <summary>
    /// The timer interrupt: every voice plays on, then the interrupt is passed on to the PC's own handler every third
    /// time (which keeps DOS's clock; the C# has none). The slot 103 of the driver is the same tick as a far call.
    /// </summary>
    /// <inheritdoc/>
    // orig: TSOUND 0000:0131 timer_tick driver; also TSOUND 0000:096d, TSOUND 0000:00f7; tests: TandySoundOracleTests
    public void TimerInterrupt()
    {
        // DIVERGENCE: D-19 nothing is passed on to a BIOS clock.
        if (!_installed)
        {
            return;
        }
        _tickCount++;
        Random();
        if (_gunfireOn)
        {
            GunfireTick();
        }
        foreach (var voice in _voices)
        {
            VoiceTick(voice);
        }
    }

    /// <summary>
    /// The driver's random number: the seed, plus a constant, plus the tick count, with the carries of the two
    /// additions. The carry the interrupted program left is not known; it is taken as clear.
    /// </summary>
    // orig: TSOUND 0000:0285 next_random driver; tests: TandySoundOracleTests
    private ushort Random()
    {
        // UNCERTAIN: U-11 the first addition takes the carry flag as the caller left it; on a PC the interrupted program's, here 0
        var first = _seed + 0x7131;
        var second = (ushort)first + _tickCount + (first >> 16);
        return _seed = (ushort)second;
    }

    /// <summary>One voice's tick: its note counts down; a sounding note fades, slides, wobbles and is written to the chip again.</summary>
    // orig: TSOUND 0000:016a voice_tick driver; tests: TandySoundOracleTests
    private void VoiceTick(Voice voice)
    {
        if (voice.TicksLeft == 0)
        {
            return;
        }
        if (--voice.TicksLeft == 0)
        {
            NextNote(voice);
            return;
        }
        if (voice.Period == Rest)
        {
            return;
        }
        FadeAttenuation(voice);
        SlidePeriod(voice);
        WobbleAttenuation(voice);
        Output(voice);
    }

    /// <summary>In the note's last ticks (from <see cref="Voice.FadeFrom"/>) the attenuation grows by the fade's step towards its target.</summary>
    private static void FadeAttenuation(Voice voice)
    {
        if (voice.FadeFrom < voice.TicksLeft || voice.FadeInterval == 0 || --voice.FadeCountdown != 0)
        {
            return;
        }
        voice.FadeCountdown = voice.FadeInterval;
        var attenuation = voice.Current & 0x0f;
        if (attenuation != voice.FadeTarget)
        {
            voice.Current = (byte)((voice.Current & 0xf0) | ((attenuation + voice.FadeStep) & 0x0f));
        }
    }

    /// <summary>Every <see cref="Voice.SlideInterval"/> ticks the period moves by the slide's step (10 bits).</summary>
    private static void SlidePeriod(Voice voice)
    {
        if (voice.SlideCountdown != 0 && --voice.SlideCountdown == 0)
        {
            voice.SlideCountdown = voice.SlideInterval;
            voice.Period = (ushort)((voice.Period + voice.SlideStep) & 0x3ff);
        }
    }

    /// <summary>
    /// Every interval the attenuation steps by one up or down from the note's own; when it would leave the one step
    /// either side of it, it goes back to the note's and the direction turns. Not while a fade works on the note.
    /// </summary>
    private static void WobbleAttenuation(Voice voice)
    {
        var fading = voice.TicksLeft < voice.FadeFrom && voice.FadeInterval != 0;
        if (voice.WobbleCountdown == 0 && fading)
        {
            return;
        }
        if (--voice.WobbleCountdown != 0)
        {
            return;
        }
        voice.WobbleCountdown = voice.WobbleInterval;
        var current = voice.WobbleCurrent != 0 ? voice.WobbleCurrent : voice.Current;
        if ((current & 0x0f) == 0x0f)
        {
            return;
        }
        var next = (byte)((current & 0xf0) | ((current + voice.WobbleStep) & 0x0f));
        voice.WobbleCurrent = next;
        if (next == voice.Current || (byte)(next + 1) == voice.Current || (byte)(next - 1) == voice.Current)
        {
            return;
        }
        voice.WobbleCurrent = voice.Current;
        voice.WobbleStep ^= 0xfe;
    }

    /// <summary>
    /// Writes the voice's channel to the chip: its attenuation, then its period (low four bits, high six); a noise
    /// channel gets its control byte only when it changes (a write restarts the chip's noise), and not while the
    /// overlay plays a noise period on it.
    /// </summary>
    // orig: TSOUND 0000:0224 write_channel driver; tests: TandySoundOracleTests
    private void Output(Voice voice)
    {
        if (voice.Period == Rest)
        {
            return;
        }
        var attenuation = voice.WobbleInterval == 0 ? voice.Current : voice.WobbleCurrent;
        _chip.Write(attenuation);
        var first = (byte)((attenuation & 0xe0) | (voice.Period & 0x0f));
        if (first < 0xe0)
        {
            _chip.Write(first);
            _chip.Write((byte)((voice.Period >> 4) & 0x3f));
            return;
        }
        if (voice == _noise && _overlay.TicksLeft != 0 && _overlay.Period <= 7)
        {
            return;
        }
        if (first != _noiseWritten)
        {
            _noiseWritten = first;
            _chip.Write(first);
        }
    }

    // ---- reading a tune ----

    /// <summary>The voice's next note: its commands are carried out, then its duration and period read; a duration of 0 ends the voice.</summary>
    // orig: TSOUND 0000:042d next_note driver; tests: TandySoundOracleTests
    private void NextNote(Voice voice)
    {
        var place = (int)voice.Next;
        if (Byte(place) == 0)
        {
            EndVoice(voice);
            return;
        }
        var duration = ReadCommands(voice, ref place);
        voice.TicksLeft = duration;
        if (duration == 0)
        {
            EndVoice(voice);
            return;
        }
        voice.Current = voice.Base;
        if (voice.WobbleInterval != 0)
        {
            voice.WobbleCurrent = voice.Base;
        }
        voice.Period = Word(place);
        voice.Next = (ushort)(place + 2);
        if (voice.Period != Rest)
        {
            Output(voice);
        }
        else
        {
            _chip.Write((byte)(voice.Base | 0x9f));
        }
    }

    /// <summary>Carries out the commands in front of a note (bytes of 80h and above); the first byte below 80h is the note's duration.</summary>
    /// <param name="voice">The voice.</param>
    /// <param name="place">Where the tune is read; behind the duration on return.</param>
    /// <returns>The note's duration in ticks.</returns>
    // orig: TSOUND 0000:0483 read_note_commands driver; tests: TandySoundOracleTests
    private byte ReadCommands(Voice voice, ref int place)
    {
        while (true)
        {
            var command = Byte(place++);
            if ((command & 0x80) == 0)
            {
                return command;
            }
            switch (command)
            {
                case SetVolume:
                    voice.Base = voice.Current = (byte)((voice.Base & 0xf0) | Byte(place++));
                    break;
                case FadeOut:
                    voice.FadeStep = (byte)(Byte(place) & 0x0f);
                    voice.FadeInterval = voice.FadeCountdown = (byte)(Byte(place++) >> 4);
                    break;
                case Slide:
                    voice.SlideInterval = voice.SlideCountdown = Byte(place++);
                    voice.SlideStep = Word(place);
                    place += 2;
                    break;
                case Repeat:
                    LoopBack(voice, Byte(place++), ref place, outer: true);
                    break;
                case InnerLoop:
                    LoopBack(voice, Byte(place++), ref place, outer: false);
                    break;
                case FadeFrom:
                    voice.FadeFrom = Byte(place++);
                    break;
                case FadeTo:
                    voice.FadeTarget = Byte(place++);
                    break;
                case Wobble:
                    voice.WobbleInterval = voice.WobbleCountdown = Byte(place++);
                    voice.WobbleStep = 1;
                    break;
                case WobbleOff:
                    voice.WobbleInterval = voice.WobbleCountdown = voice.WobbleStep = 0;
                    break;
                case SlideOff:
                    voice.SlideStep = 0;
                    voice.SlideInterval = voice.SlideCountdown = 0;
                    break;
                case RandomByte:
                    // a random byte (masked, then offset) into a byte of the data: mask, offset, address
                    SetByte(Word(place + 2), (byte)((Random() & Byte(place)) + Byte(place + 1)));
                    place += 4;
                    break;
                case EffectsOff:
                    voice.WobbleStep = voice.WobbleInterval = voice.WobbleCountdown = 0;
                    voice.SlideStep = 0;
                    voice.SlideInterval = voice.SlideCountdown = 0;
                    voice.FadeStep = voice.FadeInterval = voice.FadeCountdown = 0;
                    break;
                default:
                    // 0F0h, and every byte the driver has no command for: the tune starts again, its effects off
                    place = voice.Start;
                    voice.InnerLoopStart = voice.OuterLoopStart = voice.Start;
                    voice.FadeFrom = 0xff;
                    voice.SlideStep = 0;
                    voice.FadeStep = voice.FadeInterval = 0;
                    voice.WobbleInterval = voice.WobbleCountdown = 0;
                    break;
            }
        }
    }

    /// <summary>
    /// A loop command with its count: the first time it counts the repeats and goes back to where its loop starts, after
    /// the last it goes on and starts the next loop there. A count of 0 only marks where a loop starts. The outer loop
    /// (80h) also moves the inner one's start, the inner (0F3h) does not.
    /// </summary>
    private static void LoopBack(Voice voice, byte count, ref int place, bool outer)
    {
        ref var left = ref (outer ? ref voice.OuterRepeatsLeft : ref voice.InnerRepeatsLeft);
        var start = outer ? voice.OuterLoopStart : voice.InnerLoopStart;
        bool again;
        if (left != 0)
        {
            again = --left != 0;
        }
        else
        {
            again = count != 0;
            left = count;
        }
        if (again)
        {
            place = start;
            if (outer)
            {
                voice.InnerLoopStart = start;
            }
        }
        else if (outer)
        {
            voice.OuterLoopStart = voice.InnerLoopStart = (ushort)place;
        }
        else
        {
            voice.InnerLoopStart = (ushort)place;
        }
    }

    // ---- the car's engine and the idle engine (commands 26, 27 and 23) ----

    /// <summary>
    /// The car chase's engine (26: one car, 27: the other): tone channel 1 plays the engine's tune if it is free, and
    /// the gear (the command's second value) sets bytes of that tune: its pitch and length. Gear 0 is the idle engine.
    /// </summary>
    // orig: TSOUND 0000:0895 play_engine driver; also TSOUND 0000:088c, TSOUND 0000:0892; tests: TandySoundOracleTests
    private void CarEngine(ushort car, ushort gear)
    {
        if (_tone1.TicksLeft == 0)
        {
            StartVoice(_tone1, Tone1Channel, 0x1817);
        }
        if (gear == 0)
        {
            IdleEngine(startsTone1: true);
            return;
        }
        SetByte(0x181a, (byte)car);
        SetWord(0x181d, (ushort)((car & 0xff00) | (byte)(4 - (byte)gear)));
        SetByte(0x1818, (byte)(7 - gear));
        SetByte(0x181f, (byte)(0x18 - (gear << 2)));
        SetWord(0x1820, (ushort)(0x3d0 - (gear << 3)));
    }

    /// <summary>The idle engine: three tunes with two random pitches patched into them, on tone channels 2 and 3 and the overlay on channel 1.</summary>
    // orig: TSOUND 0000:0854 play_idle_engine driver; also TSOUND 0000:085c; tests: TandySoundOracleTests
    private void IdleEngine(bool startsTone1)
    {
        if (startsTone1)
        {
            _tone1.Start = 0x1823;
        }
        var first = (byte)IdlePitch();
        SetByte(0x1829, first);
        SetByte(0x1848, first);
        SetByte(0x1867, first);
        var second = (byte)IdlePitch();
        SetByte(0x182c, second);
        SetByte(0x184b, second);
        // a suspected slip of the original (for the bug register): a third pitch is drawn and dropped; the driver stores the second twice (at 0x184b), as if the third were meant for 0x186b
        IdlePitch();
        StartVoice(_tone2, Tone2Channel, 0x1823);
        StartVoice(_tone3, Tone3Channel, 0x1842);
        StartVoice(_overlay, Tone1Channel, 0x1861);
    }

    /// <summary>A random byte from 6 to 13.</summary>
    // orig: TSOUND 0000:084a idle_pitch driver; tests: TandySoundOracleTests
    private int IdlePitch() => (Random() & 7) + 6;

    // ---- the gunfire (command 44) ----

    /// <summary>
    /// The gunfire's tick (level 1 to 5 and an intensity, both set by the game; either 0 ends it): levels below 4 play a
    /// burst on tone channel 3 whose volume bytes are drawn anew each tick; level 4 and above also tone channel 1's.
    /// </summary>
    // orig: TSOUND 0000:030a gunfire_tick driver; tests: TandySoundOracleTests
    private void GunfireTick()
    {
        if (_gunfireLevel == 0 || _gunfireIntensity == 0)
        {
            EndGunfire();
            return;
        }
        if (_gunfireLevel >= 4)
        {
            HeavyGunfire();
        }
        else
        {
            LightGunfire();
        }
        _gunfirePreviousLevel = _gunfireLevel;
    }

    /// <summary>The gunfire is off: its tunes end at their note's end.</summary>
    // orig: TSOUND 0000:02ca end_gunfire driver; also TSOUND 0000:02e6, TSOUND 0000:02ef; tests: TandySoundOracleTests
    private void EndGunfire()
    {
        _gunfireOn = false;
        _tone1.Next = _tone2.Next = _tone3.Next = TuneEnd;
        _gunfirePreviousLevel = 0;
    }

    /// <summary>Levels 1 to 3: coming down from the heavy levels first ends their tunes.</summary>
    // orig: TSOUND 0000:032f light_gunfire driver; tests: TandySoundOracleTests
    private void LightGunfire()
    {
        if (_gunfirePreviousLevel >= 4)
        {
            _tone1.Next = _tone2.Next = _tone3.Next = TuneEnd;
            SetGunfireVolumeBytes(7);
        }
        GunfireBurst();
        GunfireVolume();
    }

    /// <summary>Levels 4 and 5: tone channel 1's burst goes on; level 4 plays the light levels' burst, level 5 its own tune on channels 2 and 3.</summary>
    // orig: TSOUND 0000:034a heavy_gunfire driver; also TSOUND 0000:037f; tests: TandySoundOracleTests
    private void HeavyGunfire()
    {
        ChooseGunfireBurst();
        if (_gunfireCount == 0)
        {
            _gunfireCount = _gunfireCountStart;
        }
        _gunfireCount--;
        if (_gunfireLevel != 5)
        {
            if (_gunfirePreviousLevel == 5)
            {
                _tone2.Next = _tone3.Next = TuneEnd;
                SetGunfireVolumeBytes(7);
            }
            GunfireBurst();
            GunfireVolume();
            return;
        }
        if (_gunfirePreviousLevel != 5)
        {
            SetGunfireVolumeBytes(3);
            StartVoice(_tone2, Tone2Channel, GunfireNote);
            _tone2.Start = 0x151d;
            StartVoice(_tone3, Tone3Channel, GunfireNote);
            var count = (ushort)(_gunfireCount - 1);
            SetGunfireNoteBytes((short)count >= 0 ? count : _gunfireCountStart);
        }
        if (_tone3.TicksLeft == 1)
        {
            ChooseGunfireTune();
        }
    }

    /// <summary>Tone channel 1's burst: started when free (with its count of 70h), when it is on its last tick given another start.</summary>
    // orig: TSOUND 0000:0294 choose_gunfire_burst driver; tests: TandySoundOracleTests
    private void ChooseGunfireBurst()
    {
        // the driver reads the word at the voice's tick byte, the spare byte (where a pause keeps the ticks) behind it
        var ticks = _tone1.TicksLeft | _tone1.SavedTicks << 8;
        if (ticks == 0)
        {
            _gunfireCountStart = 0x70;
            _gunfireCount = 0x72;
            SetGunfireVolumeBytes(7);
            StartVoice(_tone1, Tone1Channel, 0x15b1);
        }
        else if (ticks == 1)
        {
            _tone1.Start = Word(0x168d + (Random() & 6));
        }
    }

    /// <summary>Tone channel 3's burst, started when free, with a start drawn at random.</summary>
    // orig: TSOUND 0000:03b8 gunfire_burst driver; tests: TandySoundOracleTests
    private void GunfireBurst()
    {
        if (_tone3.TicksLeft == 0)
        {
            StartVoice(_tone3, Tone3Channel, GunfireNote);
            ChooseGunfireTune();
        }
    }

    /// <summary>Tone channel 3's tune restarts at a random one of the level's (a table of words per level).</summary>
    // orig: TSOUND 0000:03c6 choose_gunfire_tune driver; tests: TandySoundOracleTests
    private void ChooseGunfireTune()
    {
        var offset = (ushort)((_gunfireLevel - 1) << 4);
        var random = Random();
        _tone3.Start = _gunfireLevel == 1
            ? Word(0x162d + (random & 0x1e) + offset)
            : Word(0x163d + (random & 0x0e) + offset);
    }

    /// <summary>The burst's note bytes: a random number over the level, plus the intensity times 20, at most 255.</summary>
    // orig: TSOUND 0000:03fb gunfire_volume driver; tests: TandySoundOracleTests
    private void GunfireVolume()
    {
        var share = (((Random() & 0xff) + 1) / _gunfireLevel) | 1;
        var sum = (ushort)(_gunfireIntensity * 0x14 + share);
        SetGunfireNoteBytes((short)sum > 0xff ? (ushort)0xff : sum);
    }

    /// <summary>The gunfire note's two bytes: half the value, and half rounded up.</summary>
    // orig: TSOUND 0000:0421 set_gunfire_note_bytes driver; tests: TandySoundOracleTests
    private void SetGunfireNoteBytes(ushort value)
    {
        SetByte(GunfireNote, (byte)(value >> 1));
        SetByte(GunfireNoteEnd, (byte)((value >> 1) + (value & 1)));
    }

    /// <summary>The volume bytes of the first burst's tune.</summary>
    // orig: TSOUND 0000:02fb set_gunfire_volumes driver; also TSOUND 0000:02fd; tests: TandySoundOracleTests
    private void SetGunfireVolumeBytes(byte volume)
    {
        foreach (var offset in GunfireVolumeBytes)
        {
            SetByte(offset, volume);
        }
    }

    /// <summary>The five songs of commands 39 to 43, and the title (command 2): where the tune of each voice starts.</summary>
    private readonly record struct Song(int Tone1, int Tone2, int Tone3, int Noise);

    /// <summary>One voice: the tune it reads, the note it plays and the envelopes it applies to it.</summary>
    private sealed class Voice
    {
        /// <summary>The chip period of the note (a rest: 0FFFFh); its low 4 bits go to the chip first.</summary>
        public ushort Period;

        /// <summary>Added to the period every <see cref="SlideInterval"/> ticks.</summary>
        public ushort SlideStep;
        public byte SlideInterval, SlideCountdown;

        /// <summary>The fade-out: the attenuation grows by the step every interval, from <see cref="FadeFrom"/> ticks before the note's end.</summary>
        public byte FadeStep, FadeInterval, FadeCountdown, FadeFrom, FadeTarget;

        /// <summary>The wobble: the attenuation moves by the step (1 or 255) every interval, within one of the note's.</summary>
        public byte WobbleStep, WobbleInterval, WobbleCountdown;

        /// <summary>The two loops of a tune (commands 80h and 0F3h): repeats left and where each starts.</summary>
        public byte InnerRepeatsLeft, OuterRepeatsLeft;
        public ushort InnerLoopStart, OuterLoopStart;

        /// <summary>The tune's start (for a restart) and the next place to read it.</summary>
        public ushort Start, Next;

        /// <summary>The channel's attenuation command byte: as the voice started (<see cref="Base"/>), as the fade left it, as the wobble left it.</summary>
        public byte Base, Current, WobbleCurrent;

        /// <summary>Ticks left of the note; 0: the voice is not playing.</summary>
        public byte TicksLeft;

        /// <summary>Where the pause keeps <see cref="TicksLeft"/> (the voice's spare byte).</summary>
        public byte SavedTicks;

        /// <summary>The voice as the driver's clearing leaves it: all zero.</summary>
        public void Clear()
        {
            Period = SlideStep = InnerLoopStart = OuterLoopStart = Start = Next = 0;
            SlideInterval = SlideCountdown = FadeStep = FadeInterval = FadeCountdown = FadeFrom = FadeTarget = 0;
            WobbleStep = WobbleInterval = WobbleCountdown = InnerRepeatsLeft = OuterRepeatsLeft = 0;
            Base = Current = WobbleCurrent = TicksLeft = SavedTicks = 0;
        }
    }
}
