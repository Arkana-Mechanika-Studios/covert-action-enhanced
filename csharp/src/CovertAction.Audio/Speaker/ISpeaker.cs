namespace CovertAction.Audio.Speaker;

/// <summary>
/// The PC's speaker as the IBM sound driver plays it: a square wave of the PC timer's channel 2 (1,193,182 Hz divided
/// by a divisor), and a switch that connects the speaker to it.
/// </summary>
/// <remarks>New code.</remarks>
public interface ISpeaker
{
    /// <summary>Sets the timer's tone channel to a square wave and holds it until the next <see cref="SetDivisor"/>.</summary>
    void StartTone();

    /// <summary>The tone's divisor (0: 65,536). While a tone sounds, the new one starts at the end of the current half wave.</summary>
    void SetDivisor(ushort divisor);

    /// <summary>Connects the speaker to the tone.</summary>
    void Connect();

    /// <summary>Disconnects the speaker: silence.</summary>
    void Disconnect();
}
