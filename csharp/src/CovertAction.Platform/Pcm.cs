// Reused from the Darklands Restoration Project, src/Darklands.Audio/Pcm.cs; same author.
// Adapted: the two interfaces only (no .wav writer), in the Platform namespace; a silent output added.
namespace CovertAction.Platform;

/// <summary>Produces sound: 16-bit signed stereo frames, left then right.</summary>
public interface IPcmSource
{
    /// <summary>Fills the buffer with the next frames.</summary>
    void Render(Span<short> interleavedStereo);
}

/// <summary>A sound device (or file) that pulls frames from a source at its own rate.</summary>
public interface IPcmOutput : IDisposable
{
    /// <summary>Frames per second.</summary>
    int SampleRate { get; }

    /// <summary>Starts pulling from <paramref name="source"/>, on the device's own thread.</summary>
    void Start(IPcmSource source);

    /// <summary>Frames the listener has heard so far (an estimate: what was pulled less what waits in the device).</summary>
    long FramesPlayed { get; }

    /// <summary>The most frames the device pulls ahead of what is heard.</summary>
    int LatencyFrames { get; }
}

/// <summary>A sound device that plays nothing and never asks for frames: for hosts without sound.</summary>
public sealed class SilentPcmOutput : IPcmOutput
{
    /// <inheritdoc/>
    public int SampleRate => 44100;

    /// <inheritdoc/>
    public long FramesPlayed => 0;

    /// <inheritdoc/>
    public int LatencyFrames => 0;

    /// <inheritdoc/>
    public void Start(IPcmSource source)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
