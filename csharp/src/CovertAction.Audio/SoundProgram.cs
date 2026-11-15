namespace CovertAction.Audio;

/// <summary>
/// The logic of one of the original's sound drivers, as C#: the commands the game sends and the work the driver does
/// on each interrupt of the PC's timer. It plays through a synthesiser it is given and asks <see cref="ISoundTimer"/>
/// for the interrupt rate it wants. Not thread-safe: <see cref="ClockedSoundDriver"/> calls it under its lock.
/// </summary>
/// <remarks>New code: the shape the reconstructed drivers share.</remarks>
public interface ISoundProgram
{
    /// <summary>The driver's start (the launcher's first call after loading it).</summary>
    void Start();

    /// <summary>The driver's end (the launcher's last call).</summary>
    void Stop();

    /// <summary>Carries out a command of the game.</summary>
    /// <param name="command">The effect's or tune's number.</param>
    /// <param name="parameter">The command's second value (the car chase's gear); 0 when the game gives none.</param>
    /// <param name="variant">The command's third value (the building raid's gunfire intensity); 0 when the game gives none.</param>
    /// <returns>Whether the driver returns only when nothing plays any more (the game waits until <see cref="IsQuiet"/>).</returns>
    bool Play(int command, int parameter, int variant);

    /// <summary>Whether no sound is playing or waiting to be repeated.</summary>
    bool IsQuiet { get; }

    /// <summary>One interrupt of the PC's timer, at the rate the driver last asked for.</summary>
    void TimerInterrupt();
}

/// <summary>The rate of the PC timer's interrupt, which a sound driver sets for itself.</summary>
/// <remarks>New code.</remarks>
public interface ISoundTimer
{
    /// <summary>
    /// The timer interrupts from now on every <paramref name="divisor"/> cycles of its 1,193,182 Hz clock; 0 gives
    /// the interrupt back to the PC's own clock (65,536 cycles, and the driver gets none any more).
    /// </summary>
    void SetInterruptDivisor(int divisor);
}

/// <summary>A synthesiser a sound driver plays on, rendered by <see cref="ClockedSoundDriver"/> between the driver's interrupts.</summary>
/// <remarks>New code.</remarks>
public interface ISoundSynth
{
    /// <summary>Renders the next frames (16-bit stereo, left then right) of what the driver has set so far.</summary>
    void Render(Span<short> interleavedStereo);
}
