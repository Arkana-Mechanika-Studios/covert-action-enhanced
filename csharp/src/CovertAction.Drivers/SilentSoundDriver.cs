using CovertAction.Platform;

namespace CovertAction.Drivers;

/// <summary>
/// A sound driver that plays nothing: the set-up's "No sound" choice (the original's NSOUND.CA does nothing either),
/// and the place of the drivers not reconstructed yet (Tandy, AdLib, Roland; D-18). The game runs the same without sound.
/// </summary>
/// <remarks>New code: a stand-in, not a reconstruction (docs/faithfulness/DIVERGENCES.md D-7).</remarks>
public sealed class SilentSoundDriver : ISoundDriver
{
    /// <inheritdoc/>
    // DIVERGENCE: D-7 nothing is played (faithful for "No sound").
    public void PlaySound(int effect)
    {
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int parameter)
    {
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int mode, int variant)
    {
    }
}
