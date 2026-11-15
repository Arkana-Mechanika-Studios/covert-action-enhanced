using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// One operator (one of the two cells of an FM voice) of an instrument of the AdLib sound driver: the values the
/// driver writes into the operator's registers and those of its channel.
/// </summary>
/// <remarks>
/// New code. In the driver's data an operator is 22 bytes; the first fourteen hold these values one per byte, the
/// modulator's next eight the instrument's noise sweep (<see cref="NoiseSweep"/>).
/// </remarks>
/// <param name="Attack">Attack rate (0-15; register 60h, high half).</param>
/// <param name="Decay">Decay rate (register 60h, low half).</param>
/// <param name="Sustain">Sustain level (register 80h, high half; 15 the softest).</param>
/// <param name="Release">Release rate (register 80h, low half).</param>
/// <param name="Sustained">Whether the note holds at its sustain level until released (register 20h, 20h bit).</param>
/// <param name="KeyScaleRate">Whether the envelope is quicker for higher notes (register 20h, 10h bit).</param>
/// <param name="Level">Output level, 63 the loudest (register 40h holds 63 less it).</param>
/// <param name="KeyScaleLevel">How much softer higher notes are (0-3; register 40h, high bits).</param>
/// <param name="Waveform">Waveform (0-3; register E0h).</param>
/// <param name="Multiple">Frequency multiple (register 20h, low half).</param>
/// <param name="Feedback">The modulator's feedback (register C0h; the driver writes each operator's in turn).</param>
/// <param name="Tremolo">Amplitude vibrato (register 20h, 80h bit).</param>
/// <param name="Vibrato">Frequency vibrato (register 20h, 40h bit).</param>
/// <param name="FrequencyModulation">The modulator shapes the carrier (register C0h bit 0 clear); otherwise the two are added.</param>
public sealed record AdlibOperator(byte Attack, byte Decay, byte Sustain, byte Release, byte Sustained, byte KeyScaleRate,
    byte Level, byte KeyScaleLevel, byte Waveform, byte Multiple, byte Feedback, byte Tremolo, byte Vibrato, byte FrequencyModulation);

/// <summary>
/// An instrument's noise sweep: a note of the instrument starts a tone whose frequency word gets random bits and
/// moves by a step each tick, for a number of ticks (the drums, the shots, the engines' rumble). 0 ticks: none.
/// </summary>
/// <param name="Ticks">How many ticks the sweep lasts; 0: the instrument has none.</param>
/// <param name="Mask">The random bits let through onto the frequency word.</param>
/// <param name="Frequency">The frequency word the sweep starts at (block and F-number, as registers B0h/A0h hold them).</param>
/// <param name="Step">Added to the frequency word each tick.</param>
public sealed record NoiseSweep(byte Ticks, ushort Mask, ushort Frequency, ushort Step);

/// <summary>An instrument of the AdLib sound driver: its two operators and its noise sweep.</summary>
/// <param name="Number">The instrument's number (the tunes select it by it).</param>
/// <param name="Modulator">The first operator.</param>
/// <param name="Carrier">The second operator, the one heard.</param>
/// <param name="Noise">The noise sweep a note of it starts.</param>
public sealed record AdlibInstrument(int Number, AdlibOperator Modulator, AdlibOperator Carrier, NoiseSweep Noise);

/// <summary>
/// What the rewrite's AdLib sound driver needs from the player's ASOUND.CA, the original's driver for the AdLib card
/// ("Covert Adlib10-18-90"): its instruments, its note table, which operators make each channel, and its tunes. The
/// driver's code is not used; it is reimplemented in CovertAction.Audio.
/// </summary>
/// <remarks>
/// New code. A driver file is an MZ program; at image offset 0x2a it holds the paragraph of its data segment. The
/// tunes are byte streams in that segment (CovertAction.Audio's AdlibSoundDriver reads them); the driver changes a
/// few of their bytes as it plays the raid's gunfire, so each driver has its own copy (<see cref="SetByte"/>).
/// </remarks>
public sealed class AdlibDriverFile
{
    /// <summary>The number of instruments (0-41), from DS:0050h to the first tune at DS:0788h.</summary>
    public const int Instruments = 42;

    private const int NameOffset = 0x10;
    private const int DataSegmentWord = 0x2a;
    private const int InstrumentTable = 0x50;
    private const int InstrumentSize = 0x2c;
    private const int OperatorSize = 0x16;
    private const int NoteTable = 0x202a;
    private const int ChannelOperatorTable = 0x2046;
    private const int OperatorOffsetTable = 0x2058;
    private static ReadOnlySpan<byte> Name => "Covert Adlib10-18-90"u8;

    private readonly byte[] _data;

    private AdlibDriverFile(byte[] data) => _data = data;

    /// <summary>Reads the driver file.</summary>
    /// <param name="file">The whole of ASOUND.CA.</param>
    /// <exception cref="InvalidDataException">The file is not the AdLib sound driver.</exception>
    public static AdlibDriverFile Read(ReadOnlySpan<byte> file)
    {
        var image = Executable.Read(file).Image;
        if (image.Length < DataSegmentWord + 2 || !image.AsSpan(NameOffset, Name.Length).SequenceEqual(Name))
        {
            throw new InvalidDataException("not the AdLib sound driver (ASOUND.CA)");
        }
        var data = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(DataSegmentWord)) * 16;
        var driver = new AdlibDriverFile(image[data..]);
        // what the driver reads at fixed places: a short file fails here, not in the middle of the game
        _ = driver.Word(OperatorOffsetTable + 16);
        return driver;
    }

    /// <summary>The byte at <paramref name="offset"/> of the data segment.</summary>
    /// <exception cref="InvalidDataException">The offset is behind the end of the file.</exception>
    public byte Byte(int offset) => offset >= 0 && offset < _data.Length
        ? _data[offset]
        : throw new InvalidDataException($"ASOUND.CA ends before DS:{offset:x4}");

    /// <summary>The word at <paramref name="offset"/> of the data segment.</summary>
    /// <exception cref="InvalidDataException">The offset is behind the end of the file.</exception>
    public ushort Word(int offset) => offset >= 0 && offset + 2 <= _data.Length
        ? BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset))
        : throw new InvalidDataException($"ASOUND.CA ends before DS:{offset:x4}");

    /// <summary>Changes a byte of this copy of the data (a tune's byte the driver rewrites as it plays).</summary>
    /// <exception cref="InvalidDataException">The offset is behind the end of the file.</exception>
    public void SetByte(int offset, byte value)
    {
        _ = Byte(offset);
        _data[offset] = value;
    }

    /// <summary>The instrument of number <paramref name="number"/>, as the driver reads it from DS:0050h + 44 * number.</summary>
    /// <exception cref="InvalidDataException">The instrument is behind the end of the file.</exception>
    public AdlibInstrument Instrument(int number)
    {
        var at = InstrumentTable + number * InstrumentSize;
        return new AdlibInstrument(number, Operator(at), Operator(at + OperatorSize),
            new NoiseSweep(Byte(at + 0x0e), Word(at + 0x10), Word(at + 0x12), Word(at + 0x14)));
    }

    private AdlibOperator Operator(int at) => new(Byte(at), Byte(at + 1), Byte(at + 2), Byte(at + 3), Byte(at + 4), Byte(at + 5),
        Byte(at + 6), Byte(at + 7), Byte(at + 8), Byte(at + 9), Byte(at + 10), Byte(at + 11), Byte(at + 12), Byte(at + 13));

    /// <summary>The F-number of each of the twelve semitones from C, before the block (octave) is added.</summary>
    public ushort NoteFrequency(int semitone) => Word(NoteTable + 2 * semitone);

    /// <summary>
    /// The register offset (as 20h + offset and so on) of a channel's modulator or carrier: the driver's table of
    /// each channel's two operators, and of each operator's register offset.
    /// </summary>
    /// <param name="channel">The channel, 0-8.</param>
    /// <param name="carrier">The carrier (true) or the modulator.</param>
    public int OperatorOffset(int channel, bool carrier) =>
        Byte(OperatorOffsetTable + (sbyte)Byte(ChannelOperatorTable + 2 * channel + (carrier ? 1 : 0)));
}
