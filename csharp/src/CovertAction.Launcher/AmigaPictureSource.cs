using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Platform;
using CovertAction.Presentation;

namespace CovertAction.Launcher;

/// <summary>
/// The Amiga version's pictures for the game's picture loader (D-642; new code, outside the game): reads a picture of
/// <see cref="AmigaPictureList"/> from the player's own Amiga files when the game loads its PC twin, and gives its pixels in
/// colours of the display that are the picture's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>The palette.</b> The game draws everything of its own (text, lines, the cursor, the sprites' see-through colour, the
/// PC pictures) with colours 0 to 15 of the display, through the 16-colour palette it sets for each screen, and nothing it does
/// touches colour 128 and above. An Amiga picture is numbered 0 to 31 into a palette of its own, which is not the game's.
/// Here the picture's colours are given display colours from 128 up (<see cref="PictureColours"/>, one per distinct colour,
/// shared between pictures) and its pixels are written with those numbers. The game's colours stay as they are, so the
/// text and the lines drawn over a picture look as ever, and the picture shows in its own colours.
/// </para>
/// <para>
/// <b>Colour 0</b> of every Amiga picture is its background and see-through colour (the canvas around a city's thumbnail,
/// the cells between a sheet's parts): the game's code treats colour 0 the same way (a sprite's pixel 0 leaves the page as it
/// is; a thumbnail's width is measured up to the first 0). It stays display colour 0, which the game's palette shows as it
/// always did, so these tests of the game read the same pixels.
/// </para>
/// </remarks>
public sealed class AmigaPictureSource : IPictureReplacements, IAnimationReplacements
{
    private readonly PictureColours _colours;
    private readonly Func<string, byte[]?> _read;
    private readonly Action<string> _report;
    private readonly Dictionary<string, AmigaPictureFile?> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AmigaPanFile?> _animations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised each time a picture of the game is replaced, with the game's picture's name (for tests and tools that look at what showed).</summary>
    public event Action<string>? Replaced;

    /// <summary>Makes the source over the display colours and a way to read the Amiga files.</summary>
    /// <param name="colours">The display's colours 128 to 255, which the pictures' colours are given.</param>
    /// <param name="read">The bytes of a file of the Amiga disks by its name ("faces.pix"), or null when there is none.</param>
    /// <param name="report">Told, once for each picture, why a file could not be used; the game's own picture then shows.</param>
    public AmigaPictureSource(PictureColours colours, Func<string, byte[]?> read, Action<string> report)
    {
        _colours = colours;
        _read = read;
        _report = report;
    }

    /// <summary>
    /// The source for the Amiga files in a folder, or null when the folder has none of the pictures: the folder the player chose
    /// (as <see cref="AmigaFolderSearch.ResolvePictures"/> reads it), else the one found beside the program or the game.
    /// </summary>
    /// <param name="colours">The display's colours 128 to 255.</param>
    /// <param name="chosenFolder">The folder the player named in the settings; null or empty: search.</param>
    /// <param name="gameFolder">The game's folder, one of the places the search looks around.</param>
    /// <param name="report">Told that the files are missing (null: the console).</param>
    public static AmigaPictureSource? Open(PictureColours colours, string? chosenFolder, string gameFolder, Action<string>? report = null)
    {
        report ??= Console.Error.WriteLine;
        var folder = chosenFolder is { Length: > 0 } named ? AmigaFolderSearch.ResolvePictures(named) : AmigaFolderSearch.AutomaticPictures(gameFolder)?.Folder;
        var missing = AmigaFolderSearch.MissingPictures(folder);
        if (folder is null || missing.Count == AmigaPictureList.Entries.Count)
        {
            report(chosenFolder is { Length: > 0 }
                ? $"warning: the Amiga pictures need your Amiga files, but faces.pix is not in {chosenFolder}; the game's own pictures show instead."
                : "warning: the Amiga pictures need your Amiga files, but faces.pix is not beside the program or the game; the game's own pictures show instead.");
            return null;
        }
        if (missing.Count > 0)
        {
            report($"warning: the Amiga pictures: {missing.Count} of the {AmigaPictureList.Entries.Count} picture files are not around {folder} ({string.Join(", ", missing.Take(4))}{(missing.Count > 4 ? ", ..." : "")}); those pictures stay the game's own.");
        }
        return new AmigaPictureSource(colours, name => AmigaFolderSearch.BankFile(folder, name) is { } path ? File.ReadAllBytes(path) : null, report);
    }

    /// <inheritdoc/>
    public ReplacementPicture? Find(string name, int width, int height)
    {
        if (AmigaPictureList.Find(name) is not { } entry || Load(entry) is not { } picture)
        {
            return null;
        }
        if (entry.Arrange is not null && picture.Width == width && picture.Height == height)
        {
            // DIVERGENCE: D-685 whole regions of the Amiga's picture moved where the game's layout has them
            picture = entry.Arrange(picture);
        }
        if (picture.Width != width || picture.Height != height)
        {
            // a canvas bigger than the game's picture holds it at its top left (the thumbnails); any other size is another picture
            if (!entry.Crop || picture.Width < width || picture.Height < height)
            {
                return null;
            }
            picture = picture.Crop(width, height);
        }
        if (entry.WindowWidth > 0)
        {
            // DIVERGENCE: D-681 the picture of the PC's measured size on the PC's canvas; a picture too small for the window stays the PC's
            if (picture.Width < entry.WindowWidth || picture.Height < entry.WindowHeight)
            {
                return null;
            }
            picture = picture.OnCanvas(entry.WindowWidth, entry.WindowHeight, width, height, out _);
        }
        Replaced?.Invoke(entry.Name);
        return new ReplacementPicture(width, height, DisplayPixels(picture));
    }

    /// <summary>
    /// The picture's pixels as display colours: colour 0 stays 0; every other colour of the picture that is used gets its display
    /// colour from <see cref="PictureColours"/> (the numbers are given in the order the colours first appear in the picture).
    /// </summary>
    /// <param name="picture">The Amiga picture.</param>
    /// <returns>One display colour per pixel.</returns>
    public byte[] DisplayPixels(AmigaPictureFile picture)
    {
        var display = new int[picture.Colours.Length];
        Array.Fill(display, -1);
        display[0] = 0;
        var pixels = new byte[picture.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var colour = picture.Pixels[i];
            if (display[colour] < 0)
            {
                display[colour] = _colours.Entry(picture.Colours[colour]);
            }
            pixels[i] = (byte)display[colour];
        }
        return pixels;
    }

    /// <summary>
    /// The Amiga's version of an animation of <see cref="AmigaAnimationList"/> (D-683): the game's own animation, as read from
    /// its file, with the Amiga's images and background in their own display colours, and for an entry with
    /// <see cref="AmigaAnimationEntry.AmigaProgram"/> the Amiga's program too. The Amiga's file is used only when it is the
    /// same animation as the game's: the same size, pace, kind of background and images of the same sizes (the Amiga's may have
    /// more images when it brings its program), and, when the game's program stays, the Amiga's program translated is the game's
    /// own. Otherwise the game's animation plays.
    /// </summary>
    /// <param name="fileName">The animation's file name as the game asks for it.</param>
    /// <param name="own">The game's animation, as read.</param>
    /// <returns>The animation to play, or null for the game's own.</returns>
    public PanFile? Find(string fileName, PanFile own)
    {
        if (AmigaAnimationList.Find(fileName) is not { } entry || LoadAnimation(entry) is not { } amiga)
        {
            return null;
        }
        if (Mismatch(entry, amiga, own, out var program) is { } why)
        {
            _report($"warning: the Amiga animation {entry.File} is not the same animation as the game's {entry.Name} ({why}); the game's own plays instead.");
            return null;
        }
        var images = new PictureFile?[PanFile.ImagePlaces];
        for (var i = 0; i < images.Length; i++)
        {
            if (amiga.Images[i] is { } image && (own.Images[i] is not null || entry.AmigaProgram))
            {
                images[i] = PictureFile.FromPixels(image.Width, image.Height, DisplayPixels(image.Pixels, amiga.Colours));
            }
        }
        var background = amiga.Background is { } picture ? PictureFile.FromPixels(picture.Width, picture.Height, DisplayPixels(picture.Pixels, amiga.Colours)) : null;
        var colour = amiga.BackgroundKind == 2 ? DisplayColour(amiga.BackgroundColour, amiga.Colours) : 0;
        Replaced?.Invoke(entry.Name);
        return own.With(background, colour, images, program);
    }

    /// <summary>Why the Amiga's animation is not the game's (what differs), or null when it is the same; the Amiga's program, translated, when the entry brings it.</summary>
    private static string? Mismatch(AmigaAnimationEntry entry, AmigaPanFile amiga, PanFile own, out byte[]? program)
    {
        program = null;
        if (amiga.Width != own.Width || amiga.Height != own.Height || amiga.Delay != own.Delay || amiga.BackgroundKind != own.BackgroundKind)
        {
            return $"its size, pace or kind of background differs ({amiga.Width} x {amiga.Height}, pace {amiga.Delay}, background {amiga.BackgroundKind}; the game's {own.Width} x {own.Height}, {own.Delay}, {own.BackgroundKind})";
        }
        if (own.BackgroundKind == 1 && (amiga.Background is not { } background || own.BackgroundPicture is not { } ownBackground || (background.Width, background.Height) != (ownBackground.Width, ownBackground.Height)))
        {
            return "the size of its background picture differs";
        }
        for (var i = 0; i < PanFile.ImagePlaces; i++)
        {
            var image = amiga.Images[i];
            if (own.Images[i] is { } ownImage ? image is null || (image.Width, image.Height) != (ownImage.Width, ownImage.Height) : !entry.AmigaProgram && image is not null)
            {
                return $"image {i} differs";
            }
        }
        try
        {
            if (entry.AmigaProgram)
            {
                program = AmigaPanProgram.ToPcProgram(amiga.Program);
                return null;
            }
            // the game's program stays: the Amiga's has to be the same program (the same instructions, the same addresses)
            return AmigaPanProgram.SameInstructions(amiga.Program, own.Program) is { } difference ? $"its program is not the game's: {difference}" : null;
        }
        catch (InvalidDataException e)
        {
            return $"its program cannot be translated ({e.Message})";
        }
    }

    /// <summary>The display colour of a colour number of an animation: 0 stays 0 (see-through), the others are the animation's own colours as <see cref="PictureColours"/> gives them.</summary>
    private int DisplayColour(int colour, ushort[] colours) => colour == 0 ? 0 : _colours.Entry(colours[colour]);

    private byte[] DisplayPixels(byte[] pixels, ushort[] colours)
    {
        var display = new int[colours.Length];
        Array.Fill(display, -1);
        var result = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var colour = pixels[i];
            if (display[colour] < 0)
            {
                display[colour] = DisplayColour(colour, colours);
            }
            result[i] = (byte)display[colour];
        }
        return result;
    }

    private AmigaPanFile? LoadAnimation(AmigaAnimationEntry entry)
    {
        if (_animations.TryGetValue(entry.File, out var known))
        {
            return known;
        }
        AmigaPanFile? animation = null;
        try
        {
            if (_read(entry.File) is { } bytes)
            {
                animation = AmigaPanFile.Read(bytes);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _report($"warning: the Amiga animation {entry.File} could not be read ({e.Message}); the game's own {entry.Name} plays instead.");
        }
        _animations[entry.File] = animation;
        return animation;
    }

    private AmigaPictureFile? Load(AmigaPictureEntry entry)
    {
        if (_loaded.TryGetValue(entry.File, out var known))
        {
            return known;
        }
        AmigaPictureFile? picture = null;
        try
        {
            if (_read(entry.File) is { } bytes)
            {
                picture = AmigaPictureFile.Read(bytes);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _report($"warning: the Amiga picture {entry.File} could not be read ({e.Message}); the game's own {entry.Name} shows instead.");
        }
        _loaded[entry.File] = picture;
        return picture;
    }
}
