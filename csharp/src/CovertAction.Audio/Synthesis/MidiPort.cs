using CovertAction.Audio.Roland;

namespace CovertAction.Audio.Synthesis;

/// <summary>
/// New code (not a reconstruction of the game): the MPU-401 in UART mode with an MT-32 behind it, as the Roland sound
/// driver's <see cref="IMpu401"/> and as a synthesiser of the sound device's frames. Every byte the driver sends goes
/// to the module as MIDI (<see cref="MidiStream"/>: running status, system-exclusive messages); the module renders.
/// </summary>
/// <remarks>
/// A ready MPU-401: it acknowledges every command, the reset in UART mode included (a real one may let the first
/// reset of the driver's end go unanswered; the original then sends it again, which reaches no MIDI). It never
/// sends anything else to the computer (the MT-32 sends no MIDI back), so the driver's reading of received bytes and
/// the MPU's interrupt have nothing to do.
/// </remarks>
/// <param name="synth">The MIDI module (the MT-32 emulation, with the player's ROMs).</param>
public sealed class MidiPort(IMidiSynth synth) : IMpu401, ISoundSynth
{
    private readonly MidiStream _stream = new(synth);

    /// <inheritdoc/>
    public bool Command(byte command) => true;

    /// <inheritdoc/>
    public void Send(byte value) => _stream.Add(value);

    /// <inheritdoc/>
    // UNCERTAIN: U-8 how the MT-32 sounds is the emulation's (Munt's mt32emu with the player's ROMs).
    public void Render(Span<short> interleavedStereo) => synth.Render(interleavedStereo);
}
