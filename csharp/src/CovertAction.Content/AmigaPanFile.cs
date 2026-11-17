using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// One of the Amiga version's animations (a .pan file of the player's own Amiga disks), read into its parts: the colours,
/// the picture or the colour it plays on, up to a few dozen images and the program that moves them (D-683). It is the PC's
/// animation (<see cref="PanFile"/>) in another container: the same small machine, with every field of the program a
/// big-endian word.
/// </summary>
/// <remarks>
/// <para>
/// The format (new code, outside the game; read from the Amiga files by comparing them with the PC's animations and their
/// disassembled programs): the mark <c>ANIMAM</c>; 32 colours as words <c>0x0RGB</c> of three 4-bit components (most files use
/// the first 16); the place on the screen (two words), the width and the height, and the pause between two frames in
/// ticks (a word); then the chunks <c>BGND</c>, the images <c>SPRH</c> and the program <c>AMPG</c>, big-endian throughout.
/// </para>
/// <para>
/// <c>BGND</c> is the background: kind 1 is a picture (a word of 0, its width and height, then the picture's compressed
/// stream); kind 2 is one colour (a word with the colour number, a word with the number of images that follow). A
/// <c>SPRH</c> is an image: its number, a word the program does not need, 5 words of 0, the width and the height, 4 words of
/// 0, a word that looks like a colour count and the stream. A stream is the one of the Amiga's pictures
/// (<see cref="AmigaPictureFile"/>: the byte of the largest code width, then the compressed codes) and not byte-swapped here;
/// the pixels are one to a byte, the colour number whole, also in the files that use only 16 colours. <c>AMPG</c> ends the
/// file: a word with the program's size in bytes and the program.
/// </para>
/// <para>
/// The program is the PC's (<see cref="PanPlayer"/>): the main program and the sprites' programs use the same operations in
/// the same order, but an operation is a word where the PC's is a byte, a Push is the word 5, a word that says whether the
/// value is a register's, and the value, and the targets of a jump or call of the main program are little-endian words
/// where all the other words are big-endian (the targets of the sprites' loops are big-endian). The addresses are offsets
/// from the start of the program.
/// </para>
/// </remarks>
public sealed class AmigaPanFile
{
    private const string Mark = "ANIMAM";
    private const int ColourCount = 32;
    private const int HeadBytes = 6 + 2 * ColourCount + 10;
    private const int ImagePlaces = 250;
    private const int LargestSide = 640;
    private const int ImageHeadBytes = 32;

    private AmigaPanFile(int x, int y, int width, int height, int delay, ushort[] colours, int backgroundKind, AmigaPanImage? background, int backgroundColour, AmigaPanImage?[] images, byte[] program)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Delay = delay;
        Colours = colours;
        BackgroundKind = backgroundKind;
        Background = background;
        BackgroundColour = backgroundColour;
        Images = images;
        Program = program;
    }

    /// <summary>The left edge the file gives for the animation on the screen.</summary>
    public int X { get; }

    /// <summary>The top edge the file gives.</summary>
    public int Y { get; }

    /// <summary>The animation's width in pixels.</summary>
    public int Width { get; }

    /// <summary>The animation's height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pause between two frames, in ticks (the PC's file has the same field).</summary>
    public int Delay { get; }

    /// <summary>The animation's 32 colours: words of the form <c>0x0RGB</c>, four bits each. Index 0 is the see-through colour of the images.</summary>
    public ushort[] Colours { get; }

    /// <summary>The kind of background: 1 a picture, 2 one colour.</summary>
    public int BackgroundKind { get; }

    /// <summary>The background picture of kind 1 (its own size, which can be less than the animation's); null for kind 2.</summary>
    public AmigaPanImage? Background { get; }

    /// <summary>The colour number of the background of kind 2 (0 for a picture).</summary>
    public int BackgroundColour { get; }

    /// <summary>The images by their numbers; null where the file has none.</summary>
    public AmigaPanImage?[] Images { get; }

    /// <summary>The program, in the Amiga's encoding (see the remarks of this type).</summary>
    public byte[] Program { get; }

    /// <summary>Reads an animation file.</summary>
    /// <param name="file">The whole file.</param>
    /// <exception cref="InvalidDataException">The file is not an animation this reader knows.</exception>
    public static AmigaPanFile Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeadBytes + 4 || !file[..Mark.Length].SequenceEqual("ANIMAM"u8))
        {
            throw new InvalidDataException("not an Amiga animation file (ANIMAM)");
        }
        try
        {
            var colours = new ushort[ColourCount];
            for (var i = 0; i < ColourCount; i++)
            {
                colours[i] = BinaryPrimitives.ReadUInt16BigEndian(file[(6 + 2 * i)..]);
            }
            var at = 6 + 2 * ColourCount;
            var x = (short)Word(file, ref at);
            var y = (short)Word(file, ref at);
            var width = Word(file, ref at);
            var height = Word(file, ref at);
            var delay = Word(file, ref at);
            if (width is 0 or > LargestSide || height is 0 or > LargestSide)
            {
                throw new InvalidDataException($"an animation of {width} by {height} pixels");
            }
            // the pixels are one to a byte, the colour number whole (the files of 16 colours too)
            if (!file[at..].StartsWith("BGND"u8))
            {
                throw new InvalidDataException("the background chunk (BGND) is missing");
            }
            at += 4;
            var kind = Word(file, ref at);
            AmigaPanImage? background = null;
            var backgroundColour = 0;
            switch (kind)
            {
                case 1:
                    Word(file, ref at);
                    var pictureWidth = Word(file, ref at);
                    var pictureHeight = Word(file, ref at);
                    var picture = PictureFile.DecodeStream(file[at..], pictureWidth, pictureHeight, false, out var used);
                    at = NextChunk(file, at + used);
                    background = new AmigaPanImage(pictureWidth, pictureHeight, picture);
                    break;
                case 2:
                    backgroundColour = Word(file, ref at);
                    Word(file, ref at);
                    break;
                default:
                    throw new InvalidDataException($"an unknown kind of background ({kind})");
            }
            var images = new AmigaPanImage?[ImagePlaces];
            while (file[at..].StartsWith("SPRH"u8))
            {
                var number = BinaryPrimitives.ReadUInt16BigEndian(file[(at + 4)..]);
                var imageWidth = BinaryPrimitives.ReadUInt16BigEndian(file[(at + 4 + 2 + 2 + 10)..]);
                var imageHeight = BinaryPrimitives.ReadUInt16BigEndian(file[(at + 4 + 2 + 2 + 10 + 2)..]);
                if (number >= ImagePlaces || imageWidth is 0 or > LargestSide || imageHeight is 0 or > LargestSide)
                {
                    throw new InvalidDataException($"image {number} of {imageWidth} by {imageHeight} pixels");
                }
                var pixels = PictureFile.DecodeStream(file[(at + ImageHeadBytes)..], imageWidth, imageHeight, false, out var length);
                images[number] = new AmigaPanImage(imageWidth, imageHeight, pixels);
                at = NextChunk(file, at + ImageHeadBytes + length);
            }
            if (!file[at..].StartsWith("AMPG"u8))
            {
                throw new InvalidDataException("the program chunk (AMPG) is missing");
            }
            at += 4;
            var programSize = Word(file, ref at);
            var program = file.Slice(at, programSize).ToArray();
            if (programSize == 0 || at + programSize != file.Length)
            {
                throw new InvalidDataException("the program does not end the file");
            }
            return new AmigaPanFile(x, y, width, height, delay, colours, kind, background, backgroundColour, images, program);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("the animation file ends too early", e);
        }
    }

    /// <summary>
    /// Where the next chunk starts: a stream ends a little after its last code (the decoder's count of bytes can be a word
    /// short), so the mark of the next chunk is looked for from there on, a few words ahead at most.
    /// </summary>
    private static int NextChunk(ReadOnlySpan<byte> file, int from)
    {
        const int slack = 16;
        for (var at = from; at <= from + slack && at + 4 <= file.Length; at += 2)
        {
            if (file[at..].StartsWith("SPRH"u8) || file[at..].StartsWith("AMPG"u8))
            {
                return at;
            }
        }
        throw new InvalidDataException("the next chunk (SPRH or AMPG) does not follow an image");
    }

    private static int Word(ReadOnlySpan<byte> file, ref int at)
    {
        at += 2;
        return BinaryPrimitives.ReadUInt16BigEndian(file[(at - 2)..]);
    }
}

/// <summary>An image of an Amiga animation: its size and one colour number per pixel (0 is see-through).</summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Pixels">The pixels, row by row from the top: numbers into <see cref="AmigaPanFile.Colours"/>.</param>
public sealed record AmigaPanImage(int Width, int Height, byte[] Pixels);
