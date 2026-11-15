using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// One note of a noise sound of the IBM sound driver: a tone whose divisor is the note's divisor plus random bits
/// (as many as the noise mask lets through), gliding by a step each tick; or a rest when the mask is 0.
/// </summary>
/// <remarks>
/// New code. In the driver's data a note is five words (priority, duration, mask, divisor, glide), a rest three
/// (priority, duration, 0). The teleprinter's sound has its mask and its rest's length changed while it plays, so
/// those two are settable.
/// </remarks>
public sealed class NoiseNote
{
    /// <summary>The note's priority: a sound of a lower priority does not cut in while the note plays.</summary>
    public required ushort Priority { get; init; }

    /// <summary>Ticks the note lasts (60 a second).</summary>
    public required ushort Duration { get; set; }

    /// <summary>The bits of the random number added to the divisor; 0: the note is a rest.</summary>
    public required ushort NoiseMask { get; set; }

    /// <summary>The timer divisor of the tone (1,193,182 Hz divided by it); 0 in a rest.</summary>
    public required ushort Divisor { get; init; }

    /// <summary>Added to the divisor every tick (a word: 0xfff0 lowers it by 16).</summary>
    public required ushort Glide { get; init; }

    /// <summary>Whether the note is a rest (silence for its duration).</summary>
    public bool IsRest => NoiseMask == 0;
}

/// <summary>
/// One note of a sweep sound of the IBM sound driver: a tone that trills between two divisors and moves both by a
/// step each tick; or a rest when the start divisor is 0.
/// </summary>
/// <remarks>New code. Five words in the driver's data (priority, duration, start, other end, glide), a rest three.</remarks>
public sealed class SweepNote
{
    /// <summary>The note's priority.</summary>
    public required ushort Priority { get; init; }

    /// <summary>Ticks the note lasts.</summary>
    public required ushort Duration { get; init; }

    /// <summary>The divisor the note starts at; 0: the note is a rest.</summary>
    public required ushort Start { get; init; }

    /// <summary>The divisor of the trill's other tone.</summary>
    public required ushort OtherEnd { get; init; }

    /// <summary>Added to the divisor every tick.</summary>
    public required ushort Glide { get; init; }

    /// <summary>Whether the note is a rest.</summary>
    public bool IsRest => Start == 0;
}

/// <summary>A sound of the IBM driver: its notes in order, from where it starts in the driver's data to the word 0 that ends it.</summary>
/// <typeparam name="TNote">The kind of its notes.</typeparam>
/// <param name="Offset">Where the sound starts in the driver's data segment.</param>
/// <param name="Notes">The notes.</param>
/// <param name="NoteOffsets">Where each note starts in the data segment.</param>
public sealed record SpeakerSound<TNote>(int Offset, IReadOnlyList<TNote> Notes, IReadOnlyList<int> NoteOffsets)
{
    /// <summary>The number of the note that starts at <paramref name="offset"/> in the data segment.</summary>
    /// <exception cref="InvalidDataException">No note of the sound starts there.</exception>
    public int NoteAt(int offset) =>
        NoteOffsets.ToList().IndexOf(offset) is var index and >= 0 ? index : throw new InvalidDataException($"no note at DS:{offset:x4}");
}

/// <summary>
/// What the rewrite's PC-speaker sound driver needs from the player's ISOUND.CA, the original's driver for the PC
/// speaker: its sounds, read as typed notes. The driver's code is not used; it is reimplemented in CovertAction.Audio.
/// </summary>
/// <remarks>
/// New code. A driver file is an MZ program the launcher loads without running it. Its header area names the driver
/// ("CovertActIBM10-12-90" at image offset 0x10) and holds, at 0x2a, the paragraph of its data segment. Each sound is
/// a run of notes ended by a word 0; which sound a command plays is the driver's code, in CovertAction.Audio.
/// </remarks>
public sealed class SpeakerDriverFile
{
    private const int NameOffset = 0x10;
    private const int DataSegmentWord = 0x2a;
    private static ReadOnlySpan<byte> Name => "CovertActIBM10-12-90"u8;

    private readonly byte[] _data;
    private readonly Dictionary<(int, Type), object> _sounds = [];

    private SpeakerDriverFile(byte[] data) => _data = data;

    /// <summary>Reads the driver file.</summary>
    /// <param name="file">The whole of ISOUND.CA.</param>
    /// <exception cref="InvalidDataException">The file is not the IBM sound driver.</exception>
    public static SpeakerDriverFile Read(ReadOnlySpan<byte> file)
    {
        var image = Executable.Read(file).Image;
        if (image.Length < DataSegmentWord + 2 || !image.AsSpan(NameOffset, Name.Length).SequenceEqual(Name))
        {
            throw new InvalidDataException("not the IBM sound driver (ISOUND.CA)");
        }
        var data = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(DataSegmentWord)) * 16;
        return new SpeakerDriverFile(image[data..]);
    }

    /// <summary>The word at <paramref name="offset"/> of the data segment.</summary>
    /// <exception cref="InvalidDataException">The offset is behind the end of the file.</exception>
    public ushort Word(int offset) => offset >= 0 && offset + 2 <= _data.Length
        ? BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset))
        : throw new InvalidDataException($"ISOUND.CA ends before DS:{offset:x4}");

    /// <summary>The noise sound that starts at <paramref name="offset"/>; the same object for the same offset.</summary>
    public SpeakerSound<NoiseNote> NoiseSound(int offset) => Sound(offset, (at, rest) => new NoiseNote
    {
        Priority = Word(at), Duration = Word(at + 2), NoiseMask = Word(at + 4),
        Divisor = rest ? (ushort)0 : Word(at + 6), Glide = rest ? (ushort)0 : Word(at + 8),
    });

    /// <summary>The sweep sound that starts at <paramref name="offset"/>; the same object for the same offset.</summary>
    public SpeakerSound<SweepNote> SweepSound(int offset) => Sound(offset, (at, rest) => new SweepNote
    {
        Priority = Word(at), Duration = Word(at + 2), Start = Word(at + 4),
        OtherEnd = rest ? (ushort)0 : Word(at + 6), Glide = rest ? (ushort)0 : Word(at + 8),
    });

    private SpeakerSound<TNote> Sound<TNote>(int offset, Func<int, bool, TNote> note)
    {
        if (_sounds.TryGetValue((offset, typeof(TNote)), out var known))
        {
            return (SpeakerSound<TNote>)known;
        }
        var notes = new List<TNote>();
        var offsets = new List<int>();
        // a note whose third word is 0 is a rest of three words; the others have five
        for (var at = offset; Word(at) != 0;)
        {
            var rest = Word(at + 4) == 0;
            notes.Add(note(at, rest));
            offsets.Add(at);
            at += rest ? 6 : 10;
        }
        var sound = new SpeakerSound<TNote>(offset, notes, offsets);
        _sounds[(offset, typeof(TNote))] = sound;
        return sound;
    }
}
