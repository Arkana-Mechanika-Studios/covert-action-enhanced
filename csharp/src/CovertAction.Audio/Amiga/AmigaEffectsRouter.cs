using CovertAction.Platform;

namespace CovertAction.Audio.Amiga;

/// <summary>
/// The effects slot of the split drivers (D-620) with the Amiga version's sound effects (D-641): a command that has an Amiga
/// twin in the part that plays (<see cref="AmigaEffectMap"/>) goes to the Amiga driver; a command the Amiga game never plays
/// goes to the sound card's effects driver, so that the raid keeps its gunfire, or, with "only where the Amiga had it",
/// is not played. Stop, the end of a scene, pause and resume go to both.
/// </summary>
/// <remarks>
/// New code: the PC game has one driver for all sounds; the routing by command is the player's option's own. The part
/// (<see cref="Bank"/>) is what the launcher tells it when a part of the game starts. Both drivers' sound is mixed.
/// </remarks>
public sealed class AmigaEffectsRouter : ISoundDriver, IPcmSource
{
    private readonly AmigaEffectsDriver _amiga;
    private readonly ISoundDriver? _card;
    private readonly PcmMixer _mixer;

    /// <summary>Makes the router.</summary>
    /// <param name="amiga">The Amiga's effects.</param>
    /// <param name="card">The sound card's effects driver for the commands the Amiga has no effect for; null: they are not played.</param>
    public AmigaEffectsRouter(AmigaEffectsDriver amiga, ISoundDriver? card)
    {
        _amiga = amiga;
        _card = card;
        _mixer = card is IPcmSource cardSource ? new PcmMixer(amiga, cardSource) : new PcmMixer(amiga);
    }

    /// <summary>The part of the game that plays: see <see cref="AmigaEffectsDriver.Bank"/>. Null: a part the Amiga plays no effects in.</summary>
    public AmigaBank? Bank
    {
        get => _amiga.Bank;
        set => _amiga.Bank = value;
    }

    /// <inheritdoc/>
    public void PlaySound(int effect)
    {
        if (RoutedToAmiga(effect, out var toCard))
        {
            _amiga.PlaySound(effect);
        }
        if (toCard)
        {
            _card?.PlaySound(effect);
        }
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int parameter)
    {
        if (RoutedToAmiga(effect, out var toCard))
        {
            _amiga.PlaySound(effect, parameter);
        }
        if (toCard)
        {
            _card?.PlaySound(effect, parameter);
        }
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int mode, int variant)
    {
        if (RoutedToAmiga(effect, out var toCard))
        {
            _amiga.PlaySound(effect, mode, variant);
        }
        if (toCard)
        {
            _card?.PlaySound(effect, mode, variant);
        }
    }

    /// <summary>Which driver a command goes to: the Amiga's when it has a twin, both for a control command, else the card's.</summary>
    /// <param name="command">The game's sound command.</param>
    /// <param name="toCard">The card's driver gets it too.</param>
    /// <returns>The Amiga driver gets it.</returns>
    private bool RoutedToAmiga(int command, out bool toCard)
    {
        var control = command is SoundCommands.StopAll or SoundCommands.EndOfScene or SoundCommands.Pause or SoundCommands.Resume;
        var twin = AmigaEffectMap.Find(_amiga.Bank, command) is not null;
        toCard = control || !twin;
        return control || twin;
    }

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo) => _mixer.Render(interleavedStereo);
}
