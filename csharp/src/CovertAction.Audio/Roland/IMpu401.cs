namespace CovertAction.Audio.Roland;

/// <summary>
/// The Roland MPU-401 MIDI interface as the Roland sound driver uses it: commands to the interface itself (reset, UART
/// mode), then, in UART mode, every byte written goes out as MIDI to the MT-32.
/// </summary>
/// <remarks>New code: the two ports (330h data, 331h command and status) as two calls; no port, no polling.</remarks>
public interface IMpu401
{
    /// <summary>A command to the interface (its command port).</summary>
    /// <param name="command">FFh reset, 3Fh UART mode.</param>
    /// <returns>Whether the interface acknowledged it (FEh in its data port).</returns>
    bool Command(byte command);

    /// <summary>A byte out of the interface's MIDI output (its data port, in UART mode).</summary>
    void Send(byte value);
}
