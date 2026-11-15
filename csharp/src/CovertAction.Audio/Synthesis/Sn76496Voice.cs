using CovertAction.Audio.Tandy;

namespace CovertAction.Audio.Synthesis;

/// <summary>
/// New code (not a reconstruction of the game): the SN76496 sound chip of the Tandy 1000 and the PCjr, rendered to
/// 16-bit stereo. Three tone channels count down from their 10-bit period and flip a square wave; the noise channel
/// shifts a 15-bit register at one of three rates or at tone channel 3's; each channel has 16 attenuation steps of 2 dB,
/// the last one silent. The chip's clock is 3.579545 MHz, divided by 16 for its counters; the counters are followed one
/// step at a time and averaged over each output frame.
/// </summary>
/// <remarks>
/// The register model as the data sheet gives it: a latch byte (bit 7) picks the channel and register and gives the
/// low four bits; a data byte (bit 7 clear) completes a tone's period with six more bits. Writing the noise control
/// restarts the shift register. A tone period of 0 counts as 1024. The levels and the noise register's width and taps
/// are a model, not a measurement of a chip.
/// </remarks>
public sealed class Sn76496Voice : ISoundChip, ISoundSynth
{
    /// <summary>The chip's clock.</summary>
    public const int Clock = 3579545;

    /// <summary>The rate of the chip's counters: the clock divided by 16.</summary>
    public const double StepRate = Clock / 16.0;

    private const int ToneChannels = 3;
    private const int NoiseChannel = 3;
    private const int Silent = 15;
    private const int ShiftRegisterStart = 0x4000;
    private const int Amplitude = 2200;

    private readonly double _stepsPerFrame;
    private readonly int[] _period = new int[ToneChannels];
    private readonly int[] _count = new int[ToneChannels];
    private readonly bool[] _high = [true, true, true];
    private readonly int[] _attenuation = [Silent, Silent, Silent, Silent];
    private int _noiseControl;
    private int _noiseCount;
    private bool _noiseHigh = true;
    private int _shift = ShiftRegisterStart;
    private int _latchedChannel;
    private bool _latchedAttenuation;
    private double _owed;

    /// <summary>A chip rendered at <paramref name="sampleRate"/> frames a second.</summary>
    public Sn76496Voice(int sampleRate)
    {
        _stepsPerFrame = StepRate / sampleRate;
        Array.Fill(_period, 1);
    }

    /// <inheritdoc/>
    public void Write(byte value)
    {
        if ((value & 0x80) != 0)
        {
            _latchedChannel = (value >> 5) & 3;
            _latchedAttenuation = (value & 0x10) != 0;
            Set(value & 0x0f, firstBits: true);
        }
        else
        {
            Set(value & 0x3f, firstBits: false);
        }
    }

    private void Set(int bits, bool firstBits)
    {
        if (_latchedAttenuation)
        {
            _attenuation[_latchedChannel] = bits & 0x0f;
        }
        else if (_latchedChannel == NoiseChannel)
        {
            _noiseControl = bits & 7;
            _shift = ShiftRegisterStart;
        }
        else if (firstBits)
        {
            _period[_latchedChannel] = (_period[_latchedChannel] & 0x3f0) | bits;
        }
        else
        {
            _period[_latchedChannel] = (_period[_latchedChannel] & 0x00f) | (bits << 4);
        }
    }

    private int ToneLength(int channel) => _period[channel] == 0 ? 0x400 : _period[channel];

    /// <summary>The noise register's counter length: three fixed rates, or tone channel 3's period.</summary>
    private int NoiseLength => (_noiseControl & 3) == 3 ? ToneLength(2) : 0x10 << (_noiseControl & 3);

    private static double Level(int attenuation) => attenuation >= Silent ? 0 : Amplitude * Math.Pow(10, -attenuation * 2 / 20.0);

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        // UNCERTAIN: U-10 the chip's levels, the noise register's width and taps, and the averaging are a model of the chip.
        for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
        {
            _owed += _stepsPerFrame;
            var steps = (int)_owed;
            _owed -= steps;
            double sum = 0;
            for (var step = 0; step < steps; step++)
            {
                sum += Step();
            }
            var sample = (short)Math.Clamp(Math.Round(steps == 0 ? 0 : sum / steps), short.MinValue, short.MaxValue);
            interleavedStereo[i] = interleavedStereo[i + 1] = sample;
        }
    }

    /// <summary>One step of the chip's counters; the mixed level.</summary>
    private double Step()
    {
        var mixed = 0.0;
        for (var channel = 0; channel < ToneChannels; channel++)
        {
            if (--_count[channel] <= 0)
            {
                _count[channel] = ToneLength(channel);
                _high[channel] = !_high[channel];
            }
            mixed += (_high[channel] ? 1 : -1) * Level(_attenuation[channel]);
        }
        if (--_noiseCount <= 0)
        {
            _noiseCount = NoiseLength;
            _noiseHigh = !_noiseHigh;
            if (_noiseHigh)
            {
                ShiftNoise();
            }
        }
        return mixed + ((_shift & 1) != 0 ? 1 : -1) * Level(_attenuation[NoiseChannel]);
    }

    /// <summary>The noise register on a rising step: white noise feeds back bits 0 and 1 (exclusive or), periodic noise bit 0.</summary>
    private void ShiftNoise()
    {
        var feedback = (_noiseControl & 4) != 0 ? (_shift ^ (_shift >> 1)) & 1 : _shift & 1;
        _shift = (_shift >> 1) | (feedback << 14);
    }
}
