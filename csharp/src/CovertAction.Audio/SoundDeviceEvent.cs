using CovertAction.Audio.Adlib;
using CovertAction.Audio.Roland;
using CovertAction.Audio.Speaker;
using CovertAction.Audio.Tandy;

namespace CovertAction.Audio;

/// <summary>What a sound driver does to its device or to the PC timer, one write (or one timer interrupt) at a time.</summary>
public enum SoundDeviceEventKind
{
    /// <summary>An OPL2 register of the AdLib card written: <see cref="SoundDeviceEvent.Value"/> the register, <see cref="SoundDeviceEvent.Data"/> the value.</summary>
    OplRegister,

    /// <summary>A byte out of the MPU-401's MIDI output (its data port): <see cref="SoundDeviceEvent.Value"/>.</summary>
    MidiByte,

    /// <summary>A command to the MPU-401 (its command port): <see cref="SoundDeviceEvent.Value"/>.</summary>
    MpuCommand,

    /// <summary>A byte to the Tandy's SN76496 (port C0h): <see cref="SoundDeviceEvent.Value"/>.</summary>
    ChipByte,

    /// <summary>The timer's tone channel set to a square wave (the PC speaker; port 43h &lt;- B6h).</summary>
    SpeakerTone,

    /// <summary>The tone's divisor (port 42h): <see cref="SoundDeviceEvent.Value"/>.</summary>
    SpeakerDivisor,

    /// <summary>The speaker connected to the tone (port 61h's two low bits set).</summary>
    SpeakerOn,

    /// <summary>The speaker disconnected (port 61h's two low bits cleared).</summary>
    SpeakerOff,

    /// <summary>The timer's interrupt rate set: <see cref="SoundDeviceEvent.Value"/> the divisor (65,536 for the PC's own rate).</summary>
    TimerRate,

    /// <summary>A timer interrupt starts: the driver's work on it follows.</summary>
    TimerTick,

    /// <summary>The timer interrupt's work has ended.</summary>
    TimerTickEnd,
}

/// <summary>
/// One thing a sound driver did to its device, as the session proof compares it with the original's port writes
/// (csharp/docs/SESSION-PROOF.md, item 8; for tests and tools).
/// </summary>
/// <remarks>New code.</remarks>
/// <param name="Kind">What was done.</param>
/// <param name="Value">The register, byte, divisor or command (see <see cref="SoundDeviceEventKind"/>).</param>
/// <param name="Data">The value written to an OPL register; 0 otherwise.</param>
public readonly record struct SoundDeviceEvent(SoundDeviceEventKind Kind, int Value = 0, int Data = 0);

/// <summary>The devices of the sound drivers with every write told to a watcher before it reaches the device (for tests and tools).</summary>
/// <remarks>New code.</remarks>
internal static class WatchedDevice
{
    public static IOplChip Opl(IOplChip chip, Action<SoundDeviceEvent>? watch) => watch is null ? chip : new Opl2(chip, watch);

    public static IMpu401 Mpu(IMpu401 mpu, Action<SoundDeviceEvent>? watch) => watch is null ? mpu : new Mpu401(mpu, watch);

    public static ISoundChip Chip(ISoundChip chip, Action<SoundDeviceEvent>? watch) => watch is null ? chip : new Sn76496(chip, watch);

    public static ISpeaker Speaker(ISpeaker speaker, Action<SoundDeviceEvent>? watch) => watch is null ? speaker : new PcSpeaker(speaker, watch);

    private sealed class Opl2(IOplChip chip, Action<SoundDeviceEvent> watch) : IOplChip
    {
        public void Write(int register, byte value)
        {
            watch(new(SoundDeviceEventKind.OplRegister, register, value));
            chip.Write(register, value);
        }

        public byte ReadStatus() => chip.ReadStatus();
    }

    private sealed class Mpu401(IMpu401 mpu, Action<SoundDeviceEvent> watch) : IMpu401
    {
        public bool Command(byte command)
        {
            watch(new(SoundDeviceEventKind.MpuCommand, command));
            return mpu.Command(command);
        }

        public void Send(byte value)
        {
            watch(new(SoundDeviceEventKind.MidiByte, value));
            mpu.Send(value);
        }
    }

    private sealed class Sn76496(ISoundChip chip, Action<SoundDeviceEvent> watch) : ISoundChip
    {
        public void Write(byte value)
        {
            watch(new(SoundDeviceEventKind.ChipByte, value));
            chip.Write(value);
        }
    }

    private sealed class PcSpeaker(ISpeaker speaker, Action<SoundDeviceEvent> watch) : ISpeaker
    {
        public void StartTone()
        {
            watch(new(SoundDeviceEventKind.SpeakerTone));
            speaker.StartTone();
        }

        public void SetDivisor(ushort divisor)
        {
            watch(new(SoundDeviceEventKind.SpeakerDivisor, divisor));
            speaker.SetDivisor(divisor);
        }

        public void Connect()
        {
            watch(new(SoundDeviceEventKind.SpeakerOn));
            speaker.Connect();
        }

        public void Disconnect()
        {
            watch(new(SoundDeviceEventKind.SpeakerOff));
            speaker.Disconnect();
        }
    }
}
