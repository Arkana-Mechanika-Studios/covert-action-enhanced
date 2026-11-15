using System.Buffers.Binary;
using System.Text;
using CovertAction.Content;

namespace CovertAction.Launcher.Tests;

/// <summary>The readers of the player's game files, on small files made up for the tests (no game file is needed).</summary>
public sealed class ContentTests
{
    // ---- programs ----

    /// <summary>An MZ program file: a header of 32 bytes with the given entry point, then the payload.</summary>
    private static byte[] Program(byte[] payload, int entrySegment, int entryOffset)
    {
        var file = new byte[32 + payload.Length];
        "MZ"u8.CopyTo(file);
        void Word(int index, int value) => BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2 * index), (ushort)value);
        Word(1, file.Length % 512);
        Word(2, (file.Length + 511) / 512);
        Word(4, 2);                 // the header's size in paragraphs
        Word(10, entryOffset);
        Word(11, entrySegment);
        payload.CopyTo(file, 32);
        return file;
    }

    [Fact]
    public void APlainProgramIsItsPayload()
    {
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        var program = Executable.Read(Program(payload, 1, 2));
        Assert.False(program.Packed);
        Assert.Equal(payload, program.Image);
        Assert.Equal(18, program.Entry);
    }

    [Fact]
    public void APackedProgramIsUnpacked()
    {
        // packed data, read from its end: "fill 3 bytes with 77h", then "copy 13 bytes" (the last command), padded to a paragraph
        byte[] literal = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22];
        byte[] packed = [.. literal, 13, 0, 0xb3, 0x77, 3, 0, 0xb0, .. Enumerable.Repeat((byte)0xff, 12)];
        // the unpacking stub's header: real IP 5, real CS 0, (unused), stub size, SP, SS, unpacked size in paragraphs (1), "RB"
        byte[] stub = [5, 0, 0, 0, 0, 0, 18, 0, 0, 1, 0, 0, 1, 0, (byte)'R', (byte)'B', 0x90, 0x90];
        var program = Executable.Read(Program([.. packed, .. stub], packed.Length / 16, 0x10));
        Assert.True(program.Packed);
        Assert.Equal([.. literal, 0x77, 0x77, 0x77], program.Image);
        Assert.Equal(5, program.Entry);
    }

    [Fact]
    public void TheDataSegmentIsFoundThroughTheCStartUpCode()
    {
        // the Microsoft C start-up code at the entry names the data segment's paragraph (here 2), where "DATA" stands
        byte[] startUp = [0xb4, 0x30, 0xcd, 0x21, 0x3c, 0x02, 0x73, 0x02, 0xcd, 0x20, 0xbf, 2, 0];
        var payload = new byte[40];
        startUp.CopyTo(payload, 0);
        "DATA"u8.CopyTo(payload.AsSpan(32));
        var data = Executable.Read(Program(payload, 0, 0)).DataSegment();
        Assert.Equal(0x10000, data.Length);
        Assert.Equal("DATA", Encoding.ASCII.GetString(data, 0, 4));
        Assert.All(data[8..], b => Assert.Equal(0, b));     // behind the file's data: zeros, as the C start-up clears them

        Assert.Throws<InvalidDataException>(() => Executable.Read(Program(new byte[40], 0, 0)).DataSegment());
        Assert.Throws<InvalidDataException>(() => Executable.Read([1, 2, 3]));
    }

    [Fact]
    public void TheGraphicsDriverFileGivesItsColourTable()
    {
        // a driver file names itself at 10h and its data segment's paragraph at 2Ah; the colours are 80h into the data
        var image = new byte[0x40 + 0x80 + 3 * McgaDriverFile.Colours];
        "MGRAPHIC.EXE"u8.CopyTo(image.AsSpan(0x10));
        image[0x2a] = 4;            // the data segment is at 40h
        image[0x40 + 0x80 + 3] = 42;
        var colours = McgaDriverFile.Read(Program(image, 0, 0)).DefaultColours;
        Assert.Equal(3 * McgaDriverFile.Colours, colours.Length);
        Assert.Equal(42, colours[3]);

        "EGRAPHIC.EXE"u8.CopyTo(image.AsSpan(0x10));
        Assert.Throws<InvalidDataException>(() => McgaDriverFile.Read(Program(image, 0, 0)));
    }

    // ---- fonts ----

    [Fact]
    public void AFontGivesItsCharactersWidthsAndPictures()
    {
        // one font, 'A' and 'B', two rows high, one padding column and row; widths 2 and 3
        byte[] file = [1, 0, 14, 0, 2, 3, (byte)'A', (byte)'B', 1, 0, 2, 1, 1, 0, 0b1100_0000, 0b1010_0000, 0b1000_0000, 0b1110_0000, 0];
        var fonts = new FontSet(file);
        Assert.Equal(1, fonts.Count);
        Assert.Null(fonts.Font(0));
        Assert.Null(fonts.Font(2));
        Assert.Null(fonts.Font(-1));
        var font = fonts.Font(1)!.Value;
        Assert.Equal(('A', 'B', 2, 1, 1, 0), ((char)font.FirstCharacter, (char)font.LastCharacter, font.Rows, font.PaddingRows, font.PaddingColumns, font.FixedWidth));
        Assert.Equal(3, font.Advance((byte)'A'));
        Assert.Equal(4, font.Advance((byte)'B'));
        // a row is 16 bits from the left: the character's byte, then the next character's
        Assert.Equal(0b1100_0000_1010_0000, font.RowBits((byte)'A', 0));
        Assert.Equal(0b1110_0000_0000_0000, font.RowBits((byte)'B', 1));
        Assert.Throws<InvalidDataException>(() => new FontSet([5]));
    }

    // ---- texts ----

    [Fact]
    public void ATextEntryIsTheLinesUnderItsTagUpToTheNextTag()
    {
        var file = new TextFile("*ONE\r\nfirst\r\n\r\n   second \r\n*TWO\n*THREE\nshared\n*FOUR\n\u001a*FIVE\nnever read\n"u8);
        Assert.Equal(["first", "second "], file.Entry("*ONE"));     // blank lines and leading blanks are skipped, as the original's reader skips them
        Assert.Equal(["shared"], file.Entry("*TWO"));               // tags in a row share the text
        Assert.Equal(["shared"], file.Entry("*THREE"));
        Assert.Empty(file.Entry("*FOUR"));                          // the last tag, with nothing under it
        Assert.Empty(file.Entry("*FIVE"));                          // behind the end-of-file mark
        Assert.Empty(file.Entry("*ON"));                            // a tag is a whole line
    }

    // ---- packed blocks ----

    /// <summary>
    /// A packed block as load_picture_surface reads it: the format word, the count, a largest code width of 9 bits,
    /// and the data's bytes as codes of 9 bits each, the lowest bit first (a code below 256 is the byte itself).
    /// </summary>
    private static byte[] Block(int format, int count, params byte[] data)
    {
        var file = new byte[5 + (data.Length * 9 + 7) / 8];
        BinaryPrimitives.WriteUInt16LittleEndian(file, (ushort)format);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)count);
        file[4] = 9;
        for (var i = 0; i < data.Length * 9; i++)
        {
            if (i % 9 < 8 && (data[i / 9] >> i % 9 & 1) != 0)
            {
                file[5 + i / 8] |= (byte)(1 << i % 8);
            }
        }
        return file;
    }

    [Fact]
    public void APackedBlockIsItsBytesOrTheirHalves()
    {
        // an even format number: the bytes as they are, a run (90h, 4: the byte before three more times) and a plain 90h (90h, 0)
        Assert.Equal([0x12, 0xab, 0xab, 0xab, 0xab, 0x90, 0x34], PictureFile.ReadBlock(Block(6, 7, 0x12, 0xab, 0x90, 4, 0x90, 0, 0x34)));
        // an odd format number: two bytes of the block to a byte of data, the low half first
        Assert.Equal([2, 1, 0xb, 0xa, 0xb, 0xa], PictureFile.ReadBlock(Block(7, 6, 0x12, 0xab, 0x90, 2)));
        // ... and both halves are always written: a count of 5 gives six bytes
        Assert.Equal([2, 1, 0xb, 0xa, 4, 3], PictureFile.ReadBlock(Block(15, 5, 0x12, 0xab, 0x34)));
        // a run of count 1 repeats 256 times (docs/original-bugs.md CA-11)
        var run = PictureFile.ReadBlock(Block(0, 258, 0x55, 0x90, 1, 0x66));
        Assert.Equal(257, run.Count(b => b == 0x55));
        Assert.Equal(0x66, run[^1]);
        Assert.Throws<InvalidDataException>(() => PictureFile.ReadBlock([7, 0, 1]));
        Assert.Throws<InvalidDataException>(() => PictureFile.ReadBlock(Block(6, 3, 0x12)));     // the data ends too early
    }

    // ---- worlds ----

    /// <summary>A world file with the given names.</summary>
    internal static byte[] World(string[] cities, string[] organisations)
    {
        var file = new byte[4 + 34 * cities.Length + 36 * organisations.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file, (ushort)cities.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)organisations.Length);
        for (var i = 0; i < cities.Length; i++)
        {
            Encoding.Latin1.GetBytes(cities[i]).CopyTo(file, 4 + 34 * i);
        }
        for (var i = 0; i < organisations.Length; i++)
        {
            Encoding.Latin1.GetBytes(organisations[i]).CopyTo(file, 4 + 34 * cities.Length + 36 * i + 6);
        }
        return file;
    }

    [Fact]
    public void AWorldFileGivesTheNamesOfItsCitiesAndOrganisations()
    {
        var world = WorldFile.Read(World(["Paris", "Rome"], ["CIA", "Red Battalion", "Mafia"]));
        Assert.Equal(["Paris", "Rome"], world.CityNames);
        Assert.Equal(["CIA", "Red Battalion", "Mafia"], world.OrgNames);
        Assert.Throws<InvalidDataException>(() => WorldFile.Read([2, 0, 3, 0, 1, 2, 3]));
    }
}
