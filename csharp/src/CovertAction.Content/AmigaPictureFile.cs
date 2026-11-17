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

    /// <summary>
    /// The picture's top left part, <paramref name="windowWidth"/> by <paramref name="windowHeight"/> pixels (the size the game
    /// measures the PC's picture to be), on a canvas of colour 0 of the size of the game's picture (D-681). The game finds a
    /// city picture's size by walking the first row and the first column up to the first pixel of colour 0, so these two lines
    /// are the one place of the picture it reads: inside the window a pixel of colour 0 on them (a hole in the sky, a black
    /// edge) takes the colour of the nearest picture pixel of the line, so that the game measures the PC's size. Everything
    /// else of the window is the Amiga's pixels as they are.
    /// </summary>
    /// <param name="windowWidth">The window's width; at most the picture's.</param>
    /// <param name="windowHeight">The window's height; at most the picture's.</param>
    /// <param name="width">The canvas's width, that of the game's picture.</param>
    /// <param name="height">The canvas's height.</param>
    /// <param name="adjusted">How many pixels of the first row and column had to take a neighbour's colour.</param>
    /// <returns>The picture on its canvas, with the same colours.</returns>
    public AmigaPictureFile OnCanvas(int windowWidth, int windowHeight, int width, int height, out int adjusted)
    {
        if (windowWidth <= 0 || windowHeight <= 0 || windowWidth > Width || windowHeight > Height || windowWidth > width || windowHeight > height)
        {
            throw new ArgumentOutOfRangeException(nameof(windowWidth), $"a window of {windowWidth} x {windowHeight} does not fit a picture of {Width} x {Height} on a canvas of {width} x {height}");
        }
        var canvas = new byte[width * height];
        for (var row = 0; row < windowHeight; row++)
        {
            Pixels.AsSpan(row * Width, windowWidth).CopyTo(canvas.AsSpan(row * width));
        }
        adjusted = 0;
        FillHoles(canvas, 0, 1, windowWidth, ref adjusted);
        FillHoles(canvas, 0, width, windowHeight, ref adjusted);
        return new AmigaPictureFile(width, height, canvas, Colours);
    }

    /// <summary>
    /// The picture with its left half and its right half exchanged (D-685): every row's first <c>Width / 2</c> pixels and its last
    /// <c>Width / 2</c> pixels change places. A pure permutation of whole columns, so every pixel of the result is a pixel of the
    /// picture in its own colour, and the two pixels that meet at the middle were the picture's own ends, side by side in the
    /// picture's wrap.
    /// </summary>
    /// <returns>The picture with its halves exchanged, with the same colours.</returns>
    /// <exception cref="InvalidOperationException">The width is odd: there are no two halves.</exception>
    public AmigaPictureFile SwapHalves()
    {
        if (Width % 2 != 0)
        {
            throw new InvalidOperationException($"a picture {Width} pixels wide has no two halves");
        }
        var half = Width / 2;
        var swapped = new byte[Pixels.Length];
        for (var row = 0; row < Height; row++)
        {
            Pixels.AsSpan(row * Width, half).CopyTo(swapped.AsSpan(row * Width + half));
            Pixels.AsSpan(row * Width + half, half).CopyTo(swapped.AsSpan(row * Width));
        }
        return new AmigaPictureFile(Width, Height, swapped, Colours);
    }

    /// <summary>
    /// The picture with one rectangle of it moved (copied) to another place (D-686): the pixels of the rectangle at
    /// (<paramref name="sourceX"/>, <paramref name="sourceY"/>) are written at (<paramref name="targetX"/>, <paramref name="targetY"/>),
    /// whole and in their own colours; every pixel outside the target rectangle stays as it was. The rectangles may overlap
    /// (the source is read first).
    /// </summary>
    /// <param name="sourceX">Left edge of the rectangle to move.</param>
    /// <param name="sourceY">Top edge.</param>
    /// <param name="width">The rectangle's width.</param>
    /// <param name="height">The rectangle's height.</param>
    /// <param name="targetX">Left edge of the place it goes to.</param>
    /// <param name="targetY">Top edge.</param>
    /// <returns>The picture with the rectangle moved, with the same colours.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A rectangle does not lie wholly in the picture.</exception>
    public AmigaPictureFile MoveRegion(int sourceX, int sourceY, int width, int height, int targetX, int targetY)
    {
        if (width <= 0 || height <= 0 || sourceX < 0 || sourceY < 0 || targetX < 0 || targetY < 0
            || sourceX + width > Width || targetX + width > Width || sourceY + height > Height || targetY + height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"moving {width} x {height} from ({sourceX}, {sourceY}) to ({targetX}, {targetY}) does not fit a picture of {Width} x {Height}");
        }
        var moved = (byte[])Pixels.Clone();
        for (var row = 0; row < height; row++)
        {
            Pixels.AsSpan((sourceY + row) * Width + sourceX, width).CopyTo(moved.AsSpan((targetY + row) * Width + targetX));
        }
        return new AmigaPictureFile(Width, Height, moved, Colours);
    }

    /// <summary>Gives each pixel of colour 0 on a line of the canvas the colour of the nearest pixel of the line that has one.</summary>
    private static void FillHoles(byte[] canvas, int start, int step, int count, ref int adjusted)
    {
        var line = new byte[count];
        for (var i = 0; i < count; i++)
        {
            line[i] = canvas[start + i * step];
        }
        for (var i = 0; i < count; i++)
        {
            if (line[i] != 0)
            {
                continue;
            }
            for (var distance = 1; distance < count; distance++)
            {
                var neighbour = i - distance >= 0 && line[i - distance] != 0 ? line[i - distance] : i + distance < count ? line[i + distance] : (byte)0;
                if (neighbour != 0)
                {
                    canvas[start + i * step] = neighbour;
                    adjusted++;
                    break;
                }
            }
        }
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
