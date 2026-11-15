using CovertAction.Platform;

namespace CovertAction.Audio;

/// <summary>
/// Several sound sources played on one device: each renders the same frames, and the samples are added, held at the
/// 16-bit limits (two cards into one output, D-620). Each source keeps its own level; two loud parts at once clip.
/// </summary>
/// <remarks>New code. Called on the device's thread; each source takes its own lock.</remarks>
/// <param name="sources">The sources, rendered in this order.</param>
public sealed class PcmMixer(params IPcmSource[] sources) : IPcmSource
{
    private short[] _scratch = [];

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        if (sources.Length == 0)
        {
            interleavedStereo.Clear();
            return;
        }
        sources[0].Render(interleavedStereo);
        if (_scratch.Length < interleavedStereo.Length)
        {
            _scratch = new short[interleavedStereo.Length];
        }
        var other = _scratch.AsSpan(0, interleavedStereo.Length);
        foreach (var source in sources.Skip(1))
        {
            source.Render(other);
            for (var i = 0; i < interleavedStereo.Length; i++)
            {
                interleavedStereo[i] = (short)Math.Clamp(interleavedStereo[i] + other[i], short.MinValue, short.MaxValue);
            }
        }
    }
}
