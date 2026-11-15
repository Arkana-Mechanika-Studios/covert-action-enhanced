using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// What the rewrite's Tandy sound driver needs from the player's TSOUND.CA, the original's driver for the Tandy 1000's
/// and the PCjr's sound chip ("Tandy Covert10-09-90"): its data segment, which holds the tunes and effects (byte
/// streams of notes and commands that the driver plays on the chip's four channels) and the small tables it picks
/// from. The driver's code is not used; it is reimplemented in CovertAction.Audio.
/// </summary>
/// <remarks>
/// New code. A driver file is an MZ program; at image offset 0x2a it holds the paragraph of its data segment. The
/// driver changes bytes of its tunes as it plays (random pitches, the car engine's gear, the gunfire's volume), so
/// each driver has its own copy (<see cref="Segment"/>), and the whole 64 KB segment is there: a tune that runs
/// past the file's end reads zeros, which end it.
/// </remarks>
public static class TandyDriverFile
{
    private const int NameOffset = 0x10;
    private const int DataSegmentWord = 0x2a;
    private const int SegmentSize = 0x10000;
    private static ReadOnlySpan<byte> Name => "Tandy Covert10-09-90"u8;

    /// <summary>The driver's data segment, as the file has it, in a fresh copy of 64 KB.</summary>
    /// <param name="file">The whole of TSOUND.CA.</param>
    /// <exception cref="InvalidDataException">The file is not the Tandy sound driver.</exception>
    public static byte[] Segment(ReadOnlySpan<byte> file)
    {
        var image = Executable.Read(file).Image;
        if (image.Length < DataSegmentWord + 2 || !image.AsSpan(NameOffset, Name.Length).SequenceEqual(Name))
        {
            throw new InvalidDataException("not the Tandy sound driver (TSOUND.CA)");
        }
        var start = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(DataSegmentWord)) * 16;
        if (start >= image.Length)
        {
            throw new InvalidDataException("TSOUND.CA has no data segment");
        }
        var segment = new byte[SegmentSize];
        image.AsSpan(start, Math.Min(SegmentSize, image.Length - start)).CopyTo(segment);
        return segment;
    }
}
