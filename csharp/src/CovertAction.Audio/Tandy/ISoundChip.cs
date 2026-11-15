namespace CovertAction.Audio.Tandy;

/// <summary>
/// The Tandy 1000's and PCjr's sound chip (Texas Instruments SN76496) as its driver uses it: a byte written to port
/// 00C0h. The chip has three square-wave tone channels and one noise channel, each with 16 attenuation levels.
/// </summary>
/// <remarks>
/// New code. A byte with its top bit set latches a register (bits 6-5 the channel, bit 4 whether it is the
/// attenuation, the low four bits the first data bits); a byte without it gives the rest of a tone's period (six bits).
/// </remarks>
public interface ISoundChip
{
    /// <summary>Writes a byte to the chip's port.</summary>
    void Write(byte value);
}
