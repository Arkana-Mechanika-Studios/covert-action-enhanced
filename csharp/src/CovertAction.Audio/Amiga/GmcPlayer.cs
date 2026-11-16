using CovertAction.Platform;

namespace CovertAction.Audio.Amiga;

/// <summary>
/// Plays a <see cref="GmcTune"/> as the Amiga did: a tick player over four Paula voices, rendered as stereo PCM at the
/// sound device's rate. A tick lasts 20 ms (125 beats a minute, 2.5 / 125 s); a row lasts <see cref="Speed"/> ticks
/// (6 until the tune sets another with command 8). A voice plays its 8-bit sample at the chip's clock divided by the
/// note's period, at the note's volume; voices 0 and 3 sound on the left, 1 and 2 on the right.
/// </summary>
/// <remarks>
/// New code: the Amiga version's music, a player's option (D-640). The samples are resampled with linear interpolation (Paula
/// itself holds each byte for its period); an optional low-pass filter stands in for the A500's fixed one (about 4.9 kHz).
/// The tunes use only commands 0 and 8, so no other command is played. Not thread safe: <see cref="AmigaMusicDriver"/> takes a lock.
/// </remarks>
public sealed class GmcPlayer : IPcmSource
{
    /// <summary>Paula's clock on a PAL Amiga, in Hz: a voice reads a sample byte every <c>period</c> cycles of it.</summary>
    public const double PaulaClock = 3546895.0;

    /// <summary>The ticks' tempo, beats a minute: 125 is 50 ticks a second.</summary>
    public const int TicksPerSecond = 50;

    /// <summary>The speed (ticks a row) a tune starts with, before a set-speed command.</summary>
    public const int DefaultSpeed = 6;

    /// <summary>The effect command that sets the speed.</summary>
    public const int SetSpeedCommand = 8;

    /// <summary>The A500's fixed low-pass filter, a one-pole filter, in Hz.</summary>
    public const double LowPassHz = 4900.0;

    /// <summary>How much of a voice goes to the other side: the Amiga's channels are hard left and right; a little mixing is easier on headphones.</summary>
    private const double CrossMix = 0.3;

    /// <summary>The level of the sum, so that four loud voices do not clip.</summary>
    private const double OutputGain = 0.4;

    private const double SampleFull = 128.0;
    private const double VolumeFull = 64.0;

    private readonly GmcTune _tune;
    private readonly int _sampleRate;
    private readonly bool _loops;
    private readonly bool _lowPass;
    private readonly Voice[] _voices = [new(), new(), new(), new()];
    private readonly double _filterAlpha;
    private double _filteredLeft;
    private double _filteredRight;

    private long _frame;
    private long _tickCount;
    private int _order;
    private int _row;
    private int _ticksLeftInRow;

    /// <summary>Starts a tune at its first order.</summary>
    /// <param name="tune">The tune.</param>
    /// <param name="sampleRate">The device's frames a second.</param>
    /// <param name="loops">The tune starts again from its first order at its end (the PC's theme repeats); else it ends and is silent.</param>
    /// <param name="lowPass">Filters the sound as the A500's low-pass filter does.</param>
    public GmcPlayer(GmcTune tune, int sampleRate, bool loops, bool lowPass = true)
    {
        _tune = tune;
        _sampleRate = sampleRate;
        _loops = loops;
        _lowPass = lowPass;
        var rc = 1.0 / (2.0 * Math.PI * LowPassHz);
        _filterAlpha = (1.0 / sampleRate) / (rc + 1.0 / sampleRate);
        Speed = DefaultSpeed;
    }

    /// <summary>Ticks a row lasts.</summary>
    public int Speed { get; private set; }

    /// <summary>The order the next tick plays in (the row about to start, or the row being played).</summary>
    public int Order => _order;

    /// <summary>The row of the order's pattern, 0 to 63.</summary>
    public int Row => _row;

    /// <summary>Ticks played so far.</summary>
    public long Ticks => _tickCount;

    /// <summary>The tune has ended and does not repeat: only silence follows.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>The rate a voice plays its sample at, in samples a second, for a Paula period.</summary>
    /// <param name="period">The note's period, 1 or more.</param>
    public static double VoiceRate(int period) => PaulaClock / period;

    /// <summary>The period a voice plays at; 0 before its first note.</summary>
    /// <param name="voice">The voice, 0 to 3.</param>
    public int PeriodOf(int voice) => _voices[voice].Period;

    /// <summary>The volume a voice plays at, 0 to 64.</summary>
    /// <param name="voice">The voice, 0 to 3.</param>
    public int VolumeOf(int voice) => _voices[voice].Volume;

    /// <summary>Whether a voice sounds: it has a sample, a period and a volume, and has not run off the end of its sample.</summary>
    /// <param name="voice">The voice, 0 to 3.</param>
    public bool Sounds(int voice) => _voices[voice].Sounds;

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        var frames = interleavedStereo.Length / 2;
        var done = 0;
        while (done < frames)
        {
            if (IsFinished)
            {
                interleavedStereo[(2 * done)..].Clear();
                _frame += frames - done;
                return;
            }
            // the first frame of the next tick: ticks start 1/50 s apart, counted in whole frames from the start
            var nextTick = _tickCount * _sampleRate / TicksPerSecond;
            if (_frame >= nextTick)
            {
                Tick();
                _tickCount++;
                continue;
            }
            var chunk = (int)Math.Min(frames - done, nextTick - _frame);
            for (var i = 0; i < chunk; i++)
            {
                RenderFrame(out var left, out var right);
                interleavedStereo[2 * (done + i)] = left;
                interleavedStereo[2 * (done + i) + 1] = right;
            }
            _frame += chunk;
            done += chunk;
        }
    }

    /// <summary>One tick: a new row's cells at the first tick of a row, and the row on after its last.</summary>
    private void Tick()
    {
        if (_ticksLeftInRow == 0 && !StartRow())
        {
            return;
        }
        _ticksLeftInRow--;
        if (_ticksLeftInRow == 0)
        {
            _row++;
            if (_row >= GmcTune.RowsPerPattern)
            {
                _row = 0;
                _order++;
            }
        }
    }

    /// <summary>Plays the row's cells; false when the tune is over (an end order, or the last order's end) and does not repeat.</summary>
    private bool StartRow()
    {
        if (!HasPattern())
        {
            if (!_loops)
            {
                IsFinished = true;
                return false;
            }
            // the tune starts again as at its start, its speed too
            (_order, _row, Speed) = (0, 0, DefaultSpeed);
            if (!HasPattern())
            {
                IsFinished = true;
                return false;
            }
        }
        var pattern = _tune.Orders[_order];
        var newSpeed = 0;
        for (var v = 0; v < GmcTune.VoiceCount; v++)
        {
            var cell = _tune.Cell(pattern, _row, v);
            _voices[v].Play(cell, _tune.Samples);
            if (cell.Command == SetSpeedCommand && cell.Parameter != 0)
            {
                newSpeed = cell.Parameter;
            }
        }
        // the speed counts for the row that sets it
        if (newSpeed != 0)
        {
            Speed = newSpeed;
        }
        _ticksLeftInRow = Speed;
        return true;
    }

    /// <summary>The order the player stands at holds a pattern: the order list has not run out and the order is not the end pattern.</summary>
    private bool HasPattern() => _order < _tune.Orders.Count && _tune.Orders[_order] != GmcTune.EndPattern && _tune.Orders[_order] < _tune.PatternCount;

    /// <summary>One output frame: the voices summed on their sides, filtered, at the output level.</summary>
    private void RenderFrame(out short left, out short right)
    {
        double l = 0, r = 0;
        for (var v = 0; v < GmcTune.VoiceCount; v++)
        {
            var sample = _voices[v].Next(_sampleRate, _tune.Samples);
            if (v is 0 or 3)
            {
                l += sample;
                r += sample * CrossMix;
            }
            else
            {
                r += sample;
                l += sample * CrossMix;
            }
        }
        l *= OutputGain;
        r *= OutputGain;
        if (_lowPass)
        {
            _filteredLeft += _filterAlpha * (l - _filteredLeft);
            _filteredRight += _filterAlpha * (r - _filteredRight);
            (l, r) = (_filteredLeft, _filteredRight);
        }
        left = (short)Math.Clamp(l * short.MaxValue, short.MinValue, short.MaxValue);
        right = (short)Math.Clamp(r * short.MaxValue, short.MinValue, short.MaxValue);
    }

    /// <summary>One of Paula's four voices: the sample it plays, where it is in it, and its note's period and volume.</summary>
    private sealed class Voice
    {
        private int _sample = -1;
        private double _position;
        private bool _ended;

        /// <summary>The note's period; 0 before the first note.</summary>
        public int Period { get; private set; }

        /// <summary>The volume, 0 to 64.</summary>
        public int Volume { get; private set; }

        /// <summary>The voice has a sample, a period and a volume, and has not run off the end of a sample that does not repeat.</summary>
        public bool Sounds => _sample >= 0 && Period > 0 && Volume > 0 && !_ended;

        /// <summary>
        /// A row's cell: a note cut silences the voice; a note with a period starts the sample again from its start, at that
        /// period, and with an instrument named sets the instrument and its volume; an instrument alone changes the sample and
        /// volume of the running voice without restarting it.
        /// </summary>
        public void Play(GmcCell cell, IReadOnlyList<GmcSample> samples)
        {
            if (cell.IsNoteCut)
            {
                (Volume, _sample) = (0, -1);
                return;
            }
            if (cell.Sample != 0 && cell.Sample <= samples.Count)
            {
                _sample = cell.Sample - 1;
                Volume = samples[_sample].Volume;
            }
            if (cell.Period != 0)
            {
                Period = cell.Period;
                _position = 0;
                _ended = false;
            }
        }

        /// <summary>The next output sample, -1 to 1 before the volume: the sample read at the voice's rate, between two bytes linearly.</summary>
        public double Next(int outputRate, IReadOnlyList<GmcSample> samples)
        {
            if (!Sounds)
            {
                return 0;
            }
            var sample = samples[_sample];
            var data = sample.Data;
            if (data.Length == 0)
            {
                return 0;
            }
            var index = (int)_position;
            var fraction = _position - index;
            var next = index + 1;
            if (next >= data.Length)
            {
                next = sample.Loops ? sample.LoopStart : index;
            }
            var value = (data[index] + (data[next] - data[index]) * fraction) / SampleFull * (Volume / VolumeFull);
            _position += VoiceRate(Period) / outputRate;
            if (_position >= data.Length)
            {
                if (sample.Loops)
                {
                    _position = sample.LoopStart + (_position - sample.LoopStart) % sample.LoopLength;
                }
                else
                {
                    _ended = true;
                }
            }
            return value;
        }
    }
}
