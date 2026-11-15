using CovertAction.Audio.Adlib;

namespace CovertAction.Audio.Synthesis;

/// <summary>
/// New code (not a reconstruction of the game): the AdLib card's chip (<see cref="Opl3"/>, which is an OPL2 until its
/// OPL3 mode is switched on) as a synthesiser of the sound device's frames. The chip makes 49,716 frames a second;
/// they are taken to the device's rate by linear interpolation, as the Darklands Restoration Project's host does.
/// </summary>
public sealed class OplSynth : IOplChip, ISoundSynth
{
    private const int Gain = 2;

    private readonly Opl3 _chip = new();
    private readonly double _step;
    private double _position = 1;
    private int _previousLeft, _previousRight, _currentLeft, _currentRight;

    /// <summary>The chip rendered at <paramref name="sampleRate"/> frames a second.</summary>
    public OplSynth(int sampleRate) => _step = Opl3.SampleRate / sampleRate;

    /// <inheritdoc/>
    public void Write(int register, byte value) => _chip.Write(register, value);

    /// <inheritdoc/>
    public byte ReadStatus() => _chip.Status;

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        // UNCERTAIN: U-7 the chip's sound, its resampling and its gain are a model of the card.
        for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
        {
            while (_position >= 1)
            {
                _position -= 1;
                (_previousLeft, _previousRight) = (_currentLeft, _currentRight);
                _chip.Generate(out _currentLeft, out _currentRight);
            }
            interleavedStereo[i] = Clip(_previousLeft + (_currentLeft - _previousLeft) * _position);
            interleavedStereo[i + 1] = Clip(_previousRight + (_currentRight - _previousRight) * _position);
            _position += _step;
        }
    }

    private static short Clip(double value) => (short)Math.Clamp(value * Gain, short.MinValue, short.MaxValue);
}
