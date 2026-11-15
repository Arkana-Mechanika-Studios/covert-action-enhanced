using CovertAction.Audio.Speaker;

namespace CovertAction.Audio.Synthesis;

/// <summary>
/// New code (not a reconstruction of the game): the PC speaker driven by channel 2 of the PC's 8253/8254 timer in its
/// square-wave mode, rendered to 16-bit stereo. The square wave is followed cycle by cycle of the timer's 1,193,182 Hz
/// clock and averaged over each output frame; the speaker cannot hold a constant level, so a gentle high-pass
/// (about 20 Hz) takes the level away as a cone would.
/// </summary>
/// <remarks>
/// The timer's behaviour as its data sheet gives it: after the mode is set the output is high and the channel waits
/// for a divisor; the first divisor starts the wave at once; a later one takes over at the end of the current half
/// wave; an odd divisor N has a high half of (N+1)/2 and a low half of (N-1)/2 cycles. Disconnecting the speaker (the
/// gate and data bits off together) silences it and stops the count; connecting it again starts the count anew.
/// The amplitude and the filter are a model, not a measurement of a speaker.
/// </remarks>
public sealed class SpeakerVoice : ISpeaker, ISoundSynth
{
    /// <summary>The PC timer's clock.</summary>
    public const int TimerClock = 1193182;

    private const int Amplitude = 6000;
    private const double HighPass = 0.997;

    private readonly int _sampleRate;
    private long _frame;
    private bool _connected;
    private bool _counting;
    private bool _high = true;
    private int _divisor = 0x10000;
    private int _pending;
    private long _left;
    private double _lastInput, _lastOutput;

    /// <summary>A speaker rendered at <paramref name="sampleRate"/> frames a second.</summary>
    public SpeakerVoice(int sampleRate) => _sampleRate = sampleRate;

    /// <summary>Whether the speaker is connected.</summary>
    public bool Connected => _connected;

    /// <inheritdoc/>
    public void StartTone()
    {
        _counting = false;
        _high = true;
        _pending = 0;
    }

    /// <inheritdoc/>
    public void SetDivisor(ushort divisor)
    {
        var cycles = divisor == 0 ? 0x10000 : divisor;
        if (_counting)
        {
            _pending = cycles;
            return;
        }
        _divisor = cycles;
        _counting = true;
        Restart();
    }

    /// <inheritdoc/>
    public void Connect()
    {
        if (!_connected && _counting)
        {
            Restart();
        }
        _connected = true;
    }

    /// <inheritdoc/>
    public void Disconnect() => _connected = false;

    private void Restart()
    {
        if (_pending != 0)
        {
            _divisor = _pending;
            _pending = 0;
        }
        _high = true;
        _left = Half();
    }

    private long Half() => _high ? (_divisor + 1) / 2 : _divisor / 2;

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        // UNCERTAIN: U-6 the level and the cone's filter are a model of the speaker.
        for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
        {
            var start = _frame * TimerClock / _sampleRate;
            var end = ++_frame * TimerClock / _sampleRate;
            var input = _connected ? Amplitude * Level(end - start) : 0.0;
            _lastOutput = input - _lastInput + HighPass * _lastOutput;
            _lastInput = input;
            var sample = (short)Math.Clamp(Math.Round(_lastOutput), short.MinValue, short.MaxValue);
            interleavedStereo[i] = interleavedStereo[i + 1] = sample;
        }
    }

    /// <summary>The wave's mean level (-1 to 1) over the next <paramref name="cycles"/> timer cycles.</summary>
    private double Level(long cycles)
    {
        if (!_counting || !_connected)
        {
            return _high ? 1 : -1;
        }
        long sum = 0;
        for (var left = cycles; left > 0;)
        {
            var step = Math.Min(left, _left);
            sum += _high ? step : -step;
            left -= step;
            _left -= step;
            if (_left == 0)
            {
                _high = !_high;
                if (_pending != 0)
                {
                    _divisor = _pending;
                    _pending = 0;
                }
                _left = Half();
            }
        }
        return (double)sum / cycles;
    }
}
