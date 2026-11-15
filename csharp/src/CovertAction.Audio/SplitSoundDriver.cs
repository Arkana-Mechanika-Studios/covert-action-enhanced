using CovertAction.Platform;

namespace CovertAction.Audio;

/// <summary>
/// The game's sound driver as two (D-620): each command goes to the music driver, the effects driver or both, by its
/// <see cref="SoundCommands.RoleOf"/>, with all its values. Either driver may be missing (no card for it): its commands
/// are then not played.
/// </summary>
/// <remarks>New code: the original has one driver for both.</remarks>
/// <param name="music">The driver of the tunes; null: the tunes are not played.</param>
/// <param name="effects">The driver of the sound effects; null: the effects are not played.</param>
public sealed class SplitSoundDriver(ISoundDriver? music, ISoundDriver? effects) : ISoundDriver
{
    /// <inheritdoc/>
    public void PlaySound(int effect)
    {
        foreach (var driver in DriversOf(effect))
        {
            driver.PlaySound(effect);
        }
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int parameter)
    {
        foreach (var driver in DriversOf(effect))
        {
            driver.PlaySound(effect, parameter);
        }
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int mode, int variant)
    {
        foreach (var driver in DriversOf(effect))
        {
            driver.PlaySound(effect, mode, variant);
        }
    }

    /// <summary>The drivers a command goes to, the music driver first when both get it.</summary>
    private IEnumerable<ISoundDriver> DriversOf(int command)
    {
        var role = SoundCommands.RoleOf(command);
        if (role != SoundRole.Effect && music is not null)
        {
            yield return music;
        }
        if (role != SoundRole.Music && effects is not null)
        {
            yield return effects;
        }
    }
}
