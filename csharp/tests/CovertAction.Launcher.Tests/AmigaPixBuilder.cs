using System.Buffers.Binary;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// Makes pictures in the form of the Amiga version's .pix files out of values the test chooses (D-642), so that the tests need
/// no Amiga file: the head, the palette words, and the PC's compressed stream byte-swapped in pairs. The compression used is the
/// simplest the format allows: every byte is sent as its own code (no run is found, no sequence reused), with the code
/// widths and the table's growth the decoder expects.
/// </summary>
internal static class AmigaPixBuilder
{
    /// <summary>The largest code width the stream announces: 9 bits, so the table starts over every 256 codes.</summary>
    private const int LargestWidth = 9;

    /// <summary>The first table entry after the 256 bytes.</summary>
    private const int FirstEntry = 256;

    /// <summary>The marker of a run in the stream; a byte of this value would need the escape, which the builder does not write.</summary>
    private const byte RepeatMark = 0x90;

    /// <summary>A .pix file.</summary>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <param name="pixels">One colour number per pixel, row by row: 0 to 31 (with two palettes), 0 to 15 (with one).</param>
    /// <param name="colours">The palette words <c>0x0RGB</c>: 16 (one palette) or 32 (two).</param>
    public static byte[] Make(int width, int height, byte[] pixels, ushort[] colours)
    {
        var palettes = colours.Length / 16;
        Assert(palettes is 1 or 2 && colours.Length % 16 == 0, "16 or 32 colours");
        Assert(pixels.Length == width * height, "one pixel number each");
        var packed = palettes == 1 ? Pack(pixels, width, height) : pixels;
        Assert(!packed.Contains(RepeatMark), "no byte that would need the run escape");
        var stream = new List<byte> { LargestWidth };
        stream.AddRange(Codes(packed));
        if (stream.Count % 2 != 0)
        {
            stream.Add(0);
        }
        for (var i = 0; i < stream.Count; i += 2)
        {
            (stream[i], stream[i + 1]) = (stream[i + 1], stream[i]);
        }
        var file = new List<byte> { (byte)palettes, 7 };
        file.AddRange(Word(width));
        file.AddRange(Word(height));
        file.AddRange(colours.SelectMany(colour => Word(colour)));
        file.AddRange(stream);
        return [.. file];
    }

    /// <summary>The compressed stream of pixels one to a byte, not byte-swapped (the Amiga animations', D-683): the largest code width, then the codes, to an even length.</summary>
    /// <param name="pixels">One colour number per pixel.</param>
    public static byte[] Stream(byte[] pixels)
    {
        Assert(!pixels.Contains(RepeatMark), "no byte that would need the run escape");
        var stream = new List<byte> { LargestWidth };
        stream.AddRange(Codes(pixels));
        if (stream.Count % 2 != 0)
        {
            stream.Add(0);
        }
        return [.. stream];
    }

    /// <summary>The pictures of one palette have two pixels to a byte, the left in the low half, each row starting on a new byte.</summary>
    private static byte[] Pack(byte[] pixels, int width, int height)
    {
        var rowBytes = (width + 1) / 2;
        var packed = new byte[rowBytes * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var (row, column) = (i / width, i % width);
            packed[row * rowBytes + column / 2] |= (byte)(pixels[i] << (column % 2 * 4));
        }
        return packed;
    }

    /// <summary>The bytes as codes: 9 bits each, lowest bit first; the table grows by one entry per code and the width with it, as the decoder's does.</summary>
    private static IEnumerable<byte> Codes(byte[] data)
    {
        var bits = new List<bool>();
        int entries = FirstEntry, width = 9;
        foreach (var value in data)
        {
            for (var bit = 0; bit < width; bit++)
            {
                bits.Add((value >> bit & 1) != 0);
            }
            entries++;
            if (entries == 1 << width)
            {
                width++;
            }
            if (width > LargestWidth)
            {
                (entries, width) = (FirstEntry, 9);
            }
        }
        for (var i = 0; i < bits.Count; i += 8)
        {
            var b = 0;
            for (var bit = 0; bit < 8 && i + bit < bits.Count; bit++)
            {
                b |= (bits[i + bit] ? 1 : 0) << bit;
            }
            yield return (byte)b;
        }
    }

    private static byte[] Word(int value)
    {
        var word = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(word, (ushort)value);
        return word;
    }

    private static void Assert(bool condition, string what)
    {
        if (!condition)
        {
            throw new ArgumentException("a picture needs " + what);
        }
    }
}
