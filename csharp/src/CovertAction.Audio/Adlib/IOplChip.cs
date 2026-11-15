namespace CovertAction.Audio.Adlib;

/// <summary>
/// The AdLib card as its driver uses it: the OPL2 chip's registers, written through the index and data ports
/// (388h, 389h), and its status, read at 388h (the timers' overflow flags, by which the driver detects the card).
/// </summary>
/// <remarks>New code.</remarks>
public interface IOplChip
{
    /// <summary>Writes a register of the chip (00h-FFh).</summary>
    void Write(int register, byte value);

    /// <summary>Reads the chip's status.</summary>
    byte ReadStatus();
}
