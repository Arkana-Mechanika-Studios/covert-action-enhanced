using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// One of the game's animations (a .PAN file: the briefing room, a building's front, an arrest), read into its
/// parts: the picture it plays on, up to 250 small images, and the program that moves them.
/// </summary>
/// <remarks>
/// The format: the mark "PANI", 3, 1; a byte that says whether a palette follows, and then a byte 0, 16 colour
/// numbers and one more byte; two words for the place on the screen; the width and height less one; the delay
/// between frames; the kind of background (0 none, 1 a picture, 2 one colour); 250 words that say which images the
/// file has; those images, each a picture as in a .PIC file and padded to an even length; the program's size in
/// units of 16 bytes, and the program. The format follows the project's verified reference reader (after
/// CovertActionTools, MIT). <see cref="PanPlayer"/> plays it.
/// </remarks>
public sealed class PanFile
{
    /// <summary>The number of image places in a file.</summary>
    public const int ImagePlaces = 250;

    private const int PaletteColours = 16;
    private const int LargePaletteBlock = 0x306;
    private const int LargestWidth = 640;
    private const int LargestHeight = 480;

    private PanFile(int width, int height, int delay, byte[] palette, byte[] background, PictureFile?[] images, byte[] program)
    {
        Width = width;
        Height = height;
        Delay = delay;
        Palette = palette;
        Background = background;
        Images = images;
        Program = program;
    }

    /// <summary>The animation's width in pixels.</summary>
    public int Width { get; }

    /// <summary>The animation's height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pause between two frames, in the game's time units; 0 in the file means 1.</summary>
    public int Delay { get; }

    /// <summary>Which of the display's 16 colours each palette index shows while the animation plays; index 0 is always colour 0.</summary>
    public byte[] Palette { get; }

    /// <summary>The picture the animation plays on: one palette index per pixel, row by row.</summary>
    public byte[] Background { get; }

    /// <summary>The images by their numbers; null where the file has none.</summary>
    public PictureFile?[] Images { get; }

    /// <summary>The program that the player runs.</summary>
    public byte[] Program { get; }

    // ---- the file's parts as the game's own loader takes them (Presentation.Library.LibraryAnimation) ----

    /// <summary>Whether the file brings a palette block.</summary>
    public bool HasPalette { get; private init; }

    /// <summary>The kind of the palette block: 0 for 16 colour numbers and a border colour, 1 for none, 2 for a block of 0x306 bytes.</summary>
    public int PaletteKind { get; private init; }

    /// <summary>The palette block as it stands in the file (17 bytes of kind 0, 0x306 of kind 2), else empty.</summary>
    public byte[] PaletteBlock { get; private init; } = [];

    /// <summary>The left edge the file gives for the animation on the screen (the game's caller usually sets its own).</summary>
    public int X { get; private init; }

    /// <summary>The top edge the file gives.</summary>
    public int Y { get; private init; }

    /// <summary>The pause between two frames as the file has it, in ticks of the PC's clock (18.2 a second).</summary>
    public int FrameLength { get; private init; }

    /// <summary>The kind of background: 0 none, 1 a picture, 2 one colour.</summary>
    public int BackgroundKind { get; private init; }

    /// <summary>The background picture of kind 1, else null.</summary>
    public PictureFile? BackgroundPicture { get; private init; }

    /// <summary>The background colour of kind 2 (a word of the file).</summary>
    public int BackgroundColour { get; private init; }

    /// <summary>Reads an animation file.</summary>
    /// <param name="file">The whole file.</param>
    /// <exception cref="InvalidDataException">The file is not an animation this reader knows.</exception>
    public static PanFile Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 7 || !file[..5].SequenceEqual("PANI\u0003"u8) || file[5] != 1)
        {
            throw new InvalidDataException("not an animation file (PANI)");
        }
        try
        {
            var at = 6;
            var palette = new byte[PaletteColours];
            for (var i = 0; i < PaletteColours; i++)
            {
                palette[i] = (byte)i;
            }
            var hasPalette = file[at++] != 0;
            var paletteKind = 0;
            byte[] paletteBlock = [];
            if (hasPalette)
            {
                paletteKind = file[at++];
                var blockSize = paletteKind switch
                {
                    0 => PaletteColours + 1,
                    1 => 0,
                    2 => LargePaletteBlock,
                    _ => throw new InvalidDataException("an unknown kind of palette"),
                };
                paletteBlock = file.Slice(at, blockSize).ToArray();
                if (paletteKind == 0)
                {
                    file.Slice(at, PaletteColours).CopyTo(palette);
                }
                at += blockSize;
            }
            palette[0] = 0;
            // the place on the screen: the game's caller usually decides it
            var x = (short)Word(file, ref at);
            var y = (short)Word(file, ref at);
            var width = Word(file, ref at) + 1;
            var height = Word(file, ref at) + 1;
            if (width > LargestWidth || height > LargestHeight)
            {
                throw new InvalidDataException($"an animation of {width} by {height} pixels");
            }
            var delay = Word(file, ref at);
            var background = new byte[width * height];
            var backgroundKind = file[at++];
            PictureFile? backgroundPicture = null;
            var backgroundColour = 0;
            switch (backgroundKind)
            {
                case 0:
                    break;
                case 1:
                    backgroundPicture = Picture(file, ref at);
                    Paint(backgroundPicture, background, width, height);
                    break;
                case 2:
                    background.AsSpan().Fill(file[at]);
                    backgroundColour = Word(file, ref at);
                    break;
                default:
                    throw new InvalidDataException("an unknown kind of background");
            }
            var present = new bool[ImagePlaces];
            for (var i = 0; i < ImagePlaces; i++)
            {
                present[i] = Word(file, ref at) != 0;
            }
            var images = new PictureFile?[ImagePlaces];
            for (var i = 0; i < ImagePlaces; i++)
            {
                images[i] = present[i] ? Picture(file, ref at) : null;
            }
            var programSize = Word(file, ref at) * 16;
            if (programSize == 0)
            {
                throw new InvalidDataException("an animation without a program");
            }
            return new PanFile(width, height, delay, palette, background, images, file.Slice(at, programSize).ToArray())
            {
                HasPalette = hasPalette, PaletteKind = paletteKind, PaletteBlock = paletteBlock, X = x, Y = y, FrameLength = delay,
                BackgroundKind = backgroundKind, BackgroundPicture = backgroundPicture, BackgroundColour = backgroundColour,
            };
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("the animation file ends too early", e);
        }
    }

    private static int Word(ReadOnlySpan<byte> file, ref int at)
    {
        at += 2;
        return BinaryPrimitives.ReadUInt16LittleEndian(file[(at - 2)..]);
    }

    /// <summary>A picture inside the file; the next thing starts on an even byte behind it.</summary>
    private static PictureFile Picture(ReadOnlySpan<byte> file, ref int at)
    {
        var picture = PictureFile.Read(file[at..]);
        at += (picture.Length + 1) & ~1;
        return picture;
    }

    /// <summary>Puts a picture's pixels that are not see-through (colour 0) on a page, its corner in the page's corner.</summary>
    private static void Paint(PictureFile picture, byte[] page, int width, int height)
    {
        for (var y = 0; y < Math.Min(picture.Height, height); y++)
        {
            for (var x = 0; x < Math.Min(picture.Width, width); x++)
            {
                var colour = picture.Pixels[y * picture.Width + x];
                if (colour != 0)
                {
                    page[y * width + x] = colour;
                }
            }
        }
    }
}
