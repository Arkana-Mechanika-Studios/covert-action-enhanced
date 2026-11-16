using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// One of the Amiga version's pictures (a .pix file of the player's own Amiga disks), decoded: its size, its own palette
/// of 12-bit colours and one colour number per pixel (D-642). Unlike the PC's pictures, which show their 16 colours
/// through whatever palette the game sets, every Amiga picture brings the colours it is to be seen in.
/// </summary>
/// <remarks>
/// The format (new code, outside the game; read from the Amiga files by looking at them): a head of big-endian values (the
/// number of palettes, 1 or 2; a format number, 6 or 7, that nothing here needs; the width and the height in two words);
/// then 16 colours for each palette, each a word <c>0x0RGB</c> of three 4-bit components; then the PC's picture stream
/// (the byte with the largest code width, then the compressed codes, <see cref="PictureFile"/>) with every 16-bit word
/// byte-swapped. With one palette the pixels are packed two to a byte as on the PC (colour numbers 0 to 15); with two they
/// are one to a byte (colour numbers 0 to 31).
/// </remarks>
public sealed class AmigaPictureFile
{
    private const int HeadBytes = 6;
    private const int ColoursPerPalette = 16;
    private const int LargestSide = 2048;

    private AmigaPictureFile(int width, int height, byte[] pixels, ushort[] colours)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Colours = colours;
    }

    /// <summary>The picture's width in pixels.</summary>
    public int Width { get; }

    /// <summary>The picture's height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels, row by row from the top: colour numbers into <see cref="Colours"/>.</summary>
    public byte[] Pixels { get; }

    /// <summary>The picture's colours: 16 or 32 words of the form <c>0x0RGB</c>, four bits each.</summary>
    public ushort[] Colours { get; }

    /// <summary>
    /// The top left part of the picture: a picture of the Amiga version that is a small picture on a full-screen canvas
    /// (the city pictures that are thumbnails on the PC) cut to the size the PC's has.
    /// </summary>
    /// <param name="width">The part's width in pixels; at most the picture's.</param>
    /// <param name="height">The part's height in pixels; at most the picture's.</param>
    /// <returns>The part, with the same colours.</returns>
    public AmigaPictureFile Crop(int width, int height)
    {
        if (width is <= 0 || height is <= 0 || width > Width || height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"{width} x {height} does not fit a picture of {Width} x {Height}");
        }
        var cropped = new byte[width * height];
        for (var row = 0; row < height; row++)
        {
            Pixels.AsSpan(row * Width, width).CopyTo(cropped.AsSpan(row * width));
        }
        return new AmigaPictureFile(width, height, cropped, Colours);
    }

    /// <summary>Decodes a picture.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <exception cref="InvalidDataException">The bytes are not a picture of the Amiga version, or the picture is damaged.</exception>
    public static AmigaPictureFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeadBytes)
        {
            throw new InvalidDataException("an Amiga picture starts with a head of six bytes");
        }
        int palettes = data[0];
        int width = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        if (palettes is not (1 or 2))
        {
            throw new InvalidDataException($"an Amiga picture with {palettes} palettes");
        }
        if (width is 0 or > LargestSide || height is 0 or > LargestSide)
        {
            throw new InvalidDataException($"a picture of {width} by {height} pixels");
        }
        var colourCount = ColoursPerPalette * palettes;
        var streamAt = HeadBytes + 2 * colourCount;
        if (data.Length < streamAt + 2)
        {
            throw new InvalidDataException("the picture ends inside its palette");
        }
        var colours = new ushort[colourCount];
        for (var i = 0; i < colourCount; i++)
        {
            colours[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(HeadBytes + 2 * i)..]);
        }

        // the stream is the PC's, its bytes swapped in pairs (a last single byte stands alone)
        var stream = data[streamAt..].ToArray();
        for (var i = 0; i + 1 < stream.Length; i += 2)
        {
            (stream[i], stream[i + 1]) = (stream[i + 1], stream[i]);
        }
        var pixels = PictureFile.DecodeStream(stream, width, height, twoToAByte: palettes == 1);
        if (pixels.Any(pixel => pixel >= colourCount))
        {
            throw new InvalidDataException("a pixel names a colour the picture does not have");
        }
        return new AmigaPictureFile(width, height, pixels, colours);
    }
}
