using CovertAction.Platform;

namespace CovertAction.Audio.Amiga;

/// <summary>The five sound banks of the Amiga version, read from the player's own files.</summary>
/// <param name="Game">gamesnd.dat.</param>
/// <param name="Tac">tacsnd.dat.</param>
/// <param name="Code">codesnd.dat.</param>
/// <param name="Bug">bugsnd.dat.</param>
/// <param name="Chase">chasesnd.dat.</param>
public sealed record AmigaSoundBanks(AmigaSoundBank Game, AmigaSoundBank Tac, AmigaSoundBank Code, AmigaSoundBank Bug, AmigaSoundBank Chase)
{
    /// <summary>The five files' names, in the order of <see cref="AmigaBank"/>.</summary>
    public static IReadOnlyList<string> FileNames { get; } = ["gamesnd.dat", "tacsnd.dat", "codesnd.dat", "bugsnd.dat", "chasesnd.dat"];

    /// <summary>The bank of a part of the game.</summary>
    /// <param name="bank">The part's bank.</param>
    public AmigaSoundBank Of(AmigaBank bank) => bank switch
    {
        AmigaBank.Game => Game,
        AmigaBank.Tac => Tac,
        AmigaBank.Code => Code,
        AmigaBank.Bug => Bug,
        _ => Chase,
    };

    /// <summary>Reads the five banks.</summary>
    /// <param name="read">Gives a file's bytes by its name (one of <see cref="FileNames"/>).</param>
    /// <exception cref="FileNotFoundException">A file is not there.</exception>
    /// <exception cref="InvalidDataException">A file is not a sound bank.</exception>
    public static AmigaSoundBanks Read(Func<string, byte[]> read)
    {
        AmigaSoundBank Load(string name)
        {
            try
            {
                return AmigaSoundBank.Parse(read(name));
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{name}: {e.Message}", e);
            }
        }
        return new AmigaSoundBanks(Load(FileNames[0]), Load(FileNames[1]), Load(FileNames[2]), Load(FileNames[3]), Load(FileNames[4]));
    }
}

/// <summary>
/// The Amiga version's sound effects as an effects driver (D-641): the game's sound commands that have an Amiga twin
/// (<see cref="AmigaEffectMap"/>) play that effect on four Paula voices, allocated as the Amiga's <c>DoSound</c> does. An effect
/// is a pair of samples, the left one for the left voices (Paula's channels 0 and 3) and the right one for the right voices
/// (1 and 2); the driver numbers its voices 0 to 3 in the order of the Amiga's table: 0 and 2 left, 1 and 3 right; each side takes a free voice, else the busy voice of the lowest priority if its priority is not above the
/// effect's, else that side is not played. The volume and the pan are the Amiga call's: a table of divisors by the pan
/// position's twentieth of the screen's width takes a side's volume down to nothing at the far edge.
/// </summary>
/// <remarks>
/// New code: the Amiga version's sound effects, a player's option; the PC game has none of this. The voice choice follows
/// the Amiga's program (the routine at hunk 132+0x3244); samples are resampled with linear interpolation at the chip's clock
/// over the period, with the optional A500 low-pass filter of <see cref="GmcPlayer"/>. The device's thread renders; the
/// game's commands take turns with it under one lock. Not a part of the router: <see cref="AmigaEffectsRouter"/> decides
/// which commands come here.
/// </remarks>
public sealed class AmigaEffectsDriver : ISoundDriver, IPcmSource
{
    /// <summary>The number of Paula's voices.</summary>
    public const int Voices = 4;

    /// <summary>The highest volume of a Paula voice.</summary>
    public const int MaximumVolume = 64;

    /// <summary>The widest pan position: the screen's width in pixels (positions 0 to 319).</summary>
    public const int ScreenWidth = 320;

    /// <summary>The level of one voice at full volume.</summary>
    private const double VoiceGain = 0.75;

    /// <summary>The pan table's step: a position's twentieth selects the divisor of each side.</summary>
    private const int PanStep = 20;

    /// <summary>
    /// A volume's divisors from the middle outwards, in 256ths: 256 leaves the volume, 341 takes off a quarter, ... 16384
    /// takes it down to nothing (the far side of the screen). The Amiga's table: the left side's from the screen's
    /// middle to its right, the right side's the other way round.
    /// </summary>
    private static readonly int[] Divisors = [256, 341, 409, 512, 682, 1024, 2048, 16384];

    private readonly object _lock = new();
    private readonly AmigaSoundBanks _banks;
    private readonly int _sampleRate;
    private readonly bool _lowPass;
    private readonly Voice[] _voices = [new(), new(), new(), new()];
    private readonly double _filterAlpha;
    private double _filteredLeft;
    private double _filteredRight;
    private AmigaBank? _bank;
    private bool _paused;

    /// <summary>Makes the driver; nothing plays until the game asks for an effect.</summary>
    /// <param name="banks">The Amiga's sound banks.</param>
    /// <param name="sampleRate">The sound device's frames a second.</param>
    /// <param name="lowPass">The A500's low-pass filter is applied.</param>
    public AmigaEffectsDriver(AmigaSoundBanks banks, int sampleRate, bool lowPass = true)
    {
        _banks = banks;
        _sampleRate = sampleRate;
        _lowPass = lowPass;
        var rc = 1.0 / (2.0 * Math.PI * GmcPlayer.LowPassHz);
        _filterAlpha = (1.0 / sampleRate) / (rc + 1.0 / sampleRate);
    }

    /// <summary>
    /// The part of the game that plays: its bank gives the commands their meaning. Changing it silences the voices, as the
    /// Amiga unloads a part's bank when the next part starts. Null: a part without effects on the Amiga (FINAL).
    /// </summary>
    public AmigaBank? Bank
    {
        get
        {
            lock (_lock)
            {
                return _bank;
            }
        }
        set
        {
            lock (_lock)
            {
                StopAll();
                _bank = value;
            }
        }
    }

    /// <summary>Some voice sounds (not stopped, not finished); a pause does not end it.</summary>
    public bool Playing
    {
        get
        {
            lock (_lock)
            {
                return _voices.Any(v => v.Busy);
            }
        }
    }

    /// <summary>The volume of one side of a sound at a pan position, 0 to 64: the Amiga's table, <c>(volume * 256) / divisor</c>.</summary>
    /// <param name="volume">The call's volume, 0 to 64.</param>
    /// <param name="x">The pan position 0 (left) to 319 (right); a value outside is held to the screen.</param>
    /// <param name="left">The left side's volume, else the right side's.</param>
    public static int SideVolume(int volume, int x, bool left)
    {
        var step = Math.Clamp(x, 0, ScreenWidth - 1) / PanStep;
        var distance = left ? step - 8 : 7 - step;
        var divisor = Divisors[Math.Clamp(distance, 0, Divisors.Length - 1)];
        return Math.Min(MaximumVolume, (volume << 8) / divisor);
    }

    /// <summary>Which of two voices of a side an effect takes, or -1 when neither (the Amiga's choice).</summary>
    /// <param name="first">The side's first voice (the left side's channel 0, the right side's channel 1).</param>
    /// <param name="second">The side's second voice (channel 3, channel 2).</param>
    /// <param name="priority">The new effect's priority.</param>
    private int ChooseVoice(int first, int second, int priority)
    {
        if (!_voices[first].Busy)
        {
            return first;
        }
        if (!_voices[second].Busy)
        {
            return second;
        }
        // both busy: the lower priority goes, the second when they are equal, if the new effect is not below it
        var (lowest, pick) = _voices[first].Priority < _voices[second].Priority ? (_voices[first].Priority, first) : (_voices[second].Priority, second);
        return lowest <= priority ? pick : -1;
    }

    /// <summary>Plays an effect of a bank as the Amiga's <c>DoSound(n, volume, x, pitch offset)</c> does.</summary>
    /// <param name="effect">The effect's number in the part's bank.</param>
    /// <param name="id">What the effect is called, for stopping it (the Amiga uses the effect's number).</param>
    /// <param name="volume">The volume 0 to 64.</param>
    /// <param name="x">The pan position 0 to 319.</param>
    /// <param name="periodOffset">Added to both samples' periods (a higher period is a lower pitch); 0 for none.</param>
    public void Play(AmigaEffect effect, int id, int volume, int x = ScreenWidth / 2, int periodOffset = 0)
    {
        lock (_lock)
        {
            // DIVERGENCE: D-680 each effect is placed left or right by the Amiga call's x (the PC plays every effect in the middle)
            // the voices the Amiga numbers 0 and 3 sound on the left, 1 and 2 on the right
            Start(ChooseVoice(0, 2, effect.Priority), effect.Left, effect, id, SideVolume(volume, x, left: true), periodOffset);
            Start(ChooseVoice(1, 3, effect.Priority), effect.Right, effect, id, SideVolume(volume, x, left: false), periodOffset);
        }
    }

    private void Start(int voice, AmigaSample sample, AmigaEffect effect, int id, int volume, int periodOffset)
    {
        if (voice >= 0)
        {
            _voices[voice].Start(sample, effect.Priority, id, volume, periodOffset);
        }
    }

    /// <inheritdoc/>
    public void PlaySound(int effect) => Command(effect);

    /// <inheritdoc/>
    public void PlaySound(int effect, int parameter) => Command(effect);

    /// <inheritdoc/>
    public void PlaySound(int effect, int mode, int variant) => Command(effect);

    private void Command(int command)
    {
        lock (_lock)
        {
            switch (command)
            {
                case SoundCommands.StopAll:
                    StopAll();
                    break;
                case SoundCommands.Pause:
                    _paused = true;
                    break;
                case SoundCommands.Resume:
                    _paused = false;
                    break;
                default:
                    // the Amiga's DoSound ignores a number the bank does not have
                    if (_bank is { } bank && AmigaEffectMap.Find(bank, command) is { } row && row.Index < _banks.Of(bank).Effects.Count)
                    {
                        Play(_banks.Of(bank).Effects[row.Index], row.Index, row.Volume, row.PanX);
                    }
                    break;
            }
        }
    }

    private void StopAll()
    {
        foreach (var voice in _voices)
        {
            voice.Stop();
        }
        _paused = false;
    }

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        lock (_lock)
        {
            if (_paused)
            {
                interleavedStereo.Clear();
                return;
            }
            for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
            {
                double left = 0, right = 0;
                for (var v = 0; v < Voices; v++)
                {
                    var sample = _voices[v].Next(_sampleRate);
                    // channels 0 and 3 are the left voices, 1 and 2 the right
                    if (v is 0 or 2)
                    {
                        left += sample;
                    }
                    else
                    {
                        right += sample;
                    }
                }
                left *= VoiceGain;
                right *= VoiceGain;
                if (_lowPass)
                {
                    _filteredLeft += _filterAlpha * (left - _filteredLeft);
                    _filteredRight += _filterAlpha * (right - _filteredRight);
                    (left, right) = (_filteredLeft, _filteredRight);
                }
                interleavedStereo[i] = (short)Math.Clamp(left * short.MaxValue, short.MinValue, short.MaxValue);
                interleavedStereo[i + 1] = (short)Math.Clamp(right * short.MaxValue, short.MinValue, short.MaxValue);
            }
        }
    }

    /// <summary>The voice's state, for tests: whether it sounds, its priority and the effect it plays.</summary>
    /// <param name="voice">The voice, 0 to 3, in the Amiga's order of its program's table: 0 and 2 left, 1 and 3 right.</param>
    public (bool Busy, int Priority, int Id) VoiceState(int voice)
    {
        lock (_lock)
        {
            return (_voices[voice].Busy, _voices[voice].Priority, _voices[voice].Id);
        }
    }

    /// <summary>One of Paula's voices: the sample it plays, where it is in it, and its volume, priority and passes left.</summary>
    private sealed class Voice
    {
        private AmigaSample? _sample;
        private double _position;
        private double _step;
        private int _passesLeft;

        /// <summary>The voice is playing a sample (the Amiga's flag byte is not negative).</summary>
        public bool Busy => _sample is not null;

        /// <summary>The playing effect's priority.</summary>
        public int Priority { get; private set; }

        /// <summary>The playing effect's number.</summary>
        public int Id { get; private set; } = -1;

        /// <summary>The volume, 0 to 64.</summary>
        private int _volume;

        /// <summary>Starts a sample over, taking the voice from whatever it played.</summary>
        public void Start(AmigaSample sample, int priority, int id, int volume, int periodOffset)
        {
            // a period under 124 is more than the chip can fetch (its highest pitch)
            var period = Math.Max(sample.Period + periodOffset, 124);
            (_sample, _position, _volume, Priority, Id) = (sample.Data.Length == 0 ? null : sample, 0.0, volume, priority, id);
            _step = GmcPlayer.VoiceRate(period);
            _passesLeft = sample.Passes;
        }

        /// <summary>Silences the voice and frees it.</summary>
        public void Stop() => (_sample, Priority, Id) = (null, 0, -1);

        /// <summary>The next output sample, -1 to 1 with the volume: the sample's bytes read at the voice's rate, between two bytes linearly.</summary>
        public double Next(int outputRate)
        {
            if (_sample is not { } sample)
            {
                return 0;
            }
            var data = sample.Data;
            var index = (int)_position;
            var fraction = _position - index;
            var next = index + 1 < data.Length ? index + 1 : (sample.Endless ? 0 : index);
            var value = (data[index] + (data[next] - data[index]) * fraction) / 128.0 * (_volume / (double)MaximumVolume);
            _position += _step / outputRate;
            if (_position >= data.Length)
            {
                if (sample.Endless || --_passesLeft > 0)
                {
                    _position -= data.Length;
                }
                else
                {
                    Stop();
                }
            }
            return value;
        }
    }
}
