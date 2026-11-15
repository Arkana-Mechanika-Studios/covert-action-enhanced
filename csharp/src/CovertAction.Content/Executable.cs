using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// One of the player's DOS programs (CODE.EXE, MGRAPHIC.EXE, ...) as DOS would have it in memory: the load image
/// with its segments at their link-time paragraphs, before relocation. The game's programs are stored compressed
/// (Microsoft EXEPACK); this reader undoes that. The rewrite never runs these programs: it reads their initialised
/// data (texts, tables, start values of variables) from the image, so that none of it has to be in the rewrite.
/// </summary>
/// <remarks>
/// New code, not part of the game. The EXEPACK format and the correction for its skipped prefix follow the project's
/// tools/executables.py, which follows CovertActionTools (MIT licence; THIRD_PARTY_NOTICES.md of the project).
/// </remarks>
public sealed class Executable
{
    private const int HeaderWords = 14;

    private Executable(byte[] image, int entry, bool packed)
    {
        Image = image;
        Entry = entry;
        Packed = packed;
    }

    /// <summary>The load image: the program's bytes from its first segment on, unpacked.</summary>
    public byte[] Image { get; }

    /// <summary>The offset in the image of the program's first instruction (its CS:IP as a linear offset).</summary>
    public int Entry { get; }

    /// <summary>Whether the file was EXEPACK-compressed.</summary>
    public bool Packed { get; }

    /// <summary>
    /// The data segment (DGROUP) of a small-model C program as it is when the program starts: 64 KiB, the initialised
    /// data from the file and zeros behind it (the uninitialised variables, which the C start-up code clears).
    /// </summary>
    /// <remarks>
    /// The segment is found the way the program finds it: every Microsoft C 5.1 program begins with the same start-up
    /// code (check the DOS version, then <c>mov di, DGROUP</c>), and the operand of that instruction is the data
    /// segment's paragraph in the image.
    /// </remarks>
    /// <exception cref="InvalidDataException">The program does not begin with the Microsoft C start-up code.</exception>
    public byte[] DataSegment()
    {
        // mov ah,30h / int 21h / cmp al,2 / jae +2 / int 20h / mov di, imm16
        ReadOnlySpan<byte> startUp = [0xb4, 0x30, 0xcd, 0x21, 0x3c, 0x02, 0x73, 0x02, 0xcd, 0x20, 0xbf];
        if (Entry + startUp.Length + 2 > Image.Length || !Image.AsSpan(Entry, startUp.Length).SequenceEqual(startUp))
        {
            throw new InvalidDataException("the program does not begin with the Microsoft C start-up code");
        }
        var start = Word(Image, Entry + startUp.Length) * 16;
        var segment = new byte[0x10000];
        Image.AsSpan(start, Math.Min(Image.Length - start, segment.Length)).CopyTo(segment);
        return segment;
    }

    /// <summary>Reads an MZ program file, unpacking it when it is EXEPACK-compressed.</summary>
    /// <param name="file">The whole file.</param>
    /// <exception cref="InvalidDataException">The bytes are not a program this reader understands.</exception>
    public static Executable Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 2 * HeaderWords || file[0] != 'M' || file[1] != 'Z')
        {
            throw new InvalidDataException("not an MZ executable");
        }
        // header words: 1 bytes in the last page, 2 pages, 4 header paragraphs, 7 SS, 10 IP, 11 CS
        var size = (Word(file, 4) - 1) * 512 + (Word(file, 2) == 0 ? 512 : Word(file, 2));
        var headerSize = Word(file, 8) * 16;
        if (headerSize < 2 * HeaderWords || headerSize > size || size > file.Length)
        {
            throw new InvalidDataException("invalid MZ sizes");
        }
        var payload = file[headerSize..size];
        var entrySegment = Word(file, 22) * 16;

        // an EXEPACK program starts in its unpacking stub: entry offset 0x10, the signature "RB" before it
        var packed = Word(file, 20) == 0x10 && entrySegment + 16 <= payload.Length
            && payload[entrySegment + 14] == 'R' && payload[entrySegment + 15] == 'B';
        if (!packed)
        {
            return new Executable(payload.ToArray(), entrySegment + Word(file, 20), false);
        }
        // the stub's own header: real IP, real CS, (unused), stub size, real SP, real SS, size of the unpacked image in paragraphs
        var stub = payload[entrySegment..];
        var image = Unpack(payload[..entrySegment], Word(stub, 12) * 16);
        return new Executable(image, Word(stub, 2) * 16 + Word(stub, 0), true);
    }

    /// <summary>
    /// Undoes EXEPACK: the packed data is read from its end backwards as commands "fill n bytes with one value"
    /// (B0), "copy n bytes" (B2), with the lowest bit marking the last command. What lies before the first command
    /// is the start of the image, stored as it is.
    /// </summary>
    private static byte[] Unpack(ReadOnlySpan<byte> packed, int size)
    {
        var output = new byte[size];
        var position = packed.Length - 1;
        var target = size;
        while (position >= 0 && packed[position] == 0xff)
        {
            position--; // padding to a paragraph
        }
        while (true)
        {
            if (position < 2)
            {
                throw new InvalidDataException("EXEPACK lacks a final command");
            }
            var command = packed[position];
            var count = Word(packed, position - 2);
            position -= 3;
            target -= count;
            if (target < 0)
            {
                throw new InvalidDataException("EXEPACK output underflow");
            }
            switch (command & 0xfe)
            {
                case 0xb0:
                    if (position < 0)
                    {
                        throw new InvalidDataException("EXEPACK truncated fill");
                    }
                    output.AsSpan(target, count).Fill(packed[position]);
                    position--;
                    break;
                case 0xb2:
                    if (position + 1 < count)
                    {
                        throw new InvalidDataException("EXEPACK truncated literal");
                    }
                    packed.Slice(position + 1 - count, count).CopyTo(output.AsSpan(target));
                    position -= count;
                    break;
                default:
                    throw new InvalidDataException($"unknown EXEPACK command 0x{command:x2}");
            }
            if ((command & 1) != 0)
            {
                break;
            }
        }

        // the bytes before the first command are the image's start; the packer skipped up to 15 bytes between them
        // and the unpacked rest so that the rest begins on a paragraph
        var prefix = position + 1;
        var correction = target - prefix;
        if (correction is < 0 or >= 16)
        {
            throw new InvalidDataException($"unexpected EXEPACK paragraph correction {correction}");
        }
        var image = new byte[prefix + size - target];
        packed[..prefix].CopyTo(image);
        output.AsSpan(target).CopyTo(image.AsSpan(prefix));
        return image;
    }

    private static int Word(ReadOnlySpan<byte> data, int offset) =>
        offset < 0 || offset + 2 > data.Length
            ? throw new InvalidDataException($"truncated word at 0x{offset:x}")
            : BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
}
