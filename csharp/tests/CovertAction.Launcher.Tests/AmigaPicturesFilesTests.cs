using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Msc;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;
using CovertAction.Tests;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The Amiga pictures option (D-642) with the player's own Amiga files: every picture file read whole (skipped without
/// COVERT_AMIGA_DIR, the folder with the Amiga disks' folders) and every picture of the list checked against the game's twin
/// of it (also skipped without the game's files). Nothing of an Amiga file is kept in the repository.
/// </summary>
public sealed class AmigaPicturesFilesTests
{
    private const string FolderVariable = "COVERT_AMIGA_DIR";

    /// <summary>The least score of the edge comparison (1.00: every edge of the one picture is an edge of the other) for a picture to be on the list.</summary>
    private const double LeastLayoutScore = 0.95;

    /// <summary>The least score for a picture shown in a window (D-681): the Amiga's dithering differs from the PC's, so the edges agree less (1.00 for six of the nine, 0.48 for Miami whose PC sky is dithered and the Amiga's flat), and the composition was looked at.</summary>
    private const double LeastWindowScore = 0.45;

    /// <summary>The most pixels of a window's first row and column that may take a neighbour's colour so that the game measures the PC's size.</summary>
    private const int MostAdjustedPixels = 3;

    private static string AmigaFolder()
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        Skip.If(string.IsNullOrEmpty(folder) || !Directory.Exists(folder), $"no Amiga files: set {FolderVariable} to the folder with the Amiga disks' folders");
        return folder!;
    }

    private static GameFolder FindGame()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "CODE.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        Skip.If(folder is null || folder.Find("CODE.EXE") is null, "no game files: set COVERT_GAME_DIR to a folder with the original game");
        return folder!;
    }

    [SkippableFact]
    public void EveryPictureFileOfTheDisksIsRead()
    {
        var files = Directory.EnumerateFiles(AmigaFolder(), "*.pix", SearchOption.AllDirectories).Order().ToList();
        Skip.If(files.Count == 0, "no .pix files in the Amiga folder");
        foreach (var file in files)
        {
            var picture = AmigaPictureFile.Read(File.ReadAllBytes(file));
            Assert.Equal((320, 200), (picture.Width, picture.Height));
            Assert.Equal(picture.Width * picture.Height, picture.Pixels.Length);
            Assert.True(picture.Colours.Length is 16 or 32, file);
            Assert.All(picture.Colours, colour => Assert.True(colour <= 0xfff, file));
        }
    }

    [SkippableFact]
    public void TheFolderSearchFindsEveryPictureOfTheList()
    {
        var folder = AmigaFolder();
        var pictures = AmigaFolderSearch.ResolvePictures(folder);
        Assert.NotNull(pictures);
        Assert.Empty(AmigaFolderSearch.MissingPictures(pictures));
        // a folder named like a disk finds the others beside it
        var disk = Directory.EnumerateDirectories(folder, "disk*").Order().FirstOrDefault();
        if (disk is not null)
        {
            Assert.Empty(AmigaFolderSearch.MissingPictures(AmigaFolderSearch.ResolvePictures(disk)));
        }
    }

    [SkippableFact]
    public void EveryPictureOfTheListIsReplacedAndAllTheirColoursFitTheDisplayColours()
    {
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var colours = new PictureColours(new byte[3 * 256]);
        var told = new List<string>();
        var source = AmigaPictureSource.Open(colours, pictures, "", told.Add);
        Assert.NotNull(source);
        Assert.Empty(told);
        foreach (var entry in AmigaPictureList.Entries)
        {
            var amiga = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, entry.File)!));
            var size = entry.Crop ? (88, 56) : (amiga.Width, amiga.Height);
            var replacement = source.Find(entry.Name, size.Item1, size.Item2);
            Assert.True(replacement is not null, entry.Name);
            Assert.All(replacement.Value.Pixels, pixel => Assert.True(pixel == 0 || pixel >= PictureColours.First, entry.Name));
        }
        // the whole list needs fewer of the 128 display colours than there are (pictures share their colours); no colour was approximated
        Assert.True(colours.Count < PictureColours.Capacity, $"{colours.Count} display colours");
    }

    [SkippableFact]
    public void EveryPictureOfTheListHasTheLayoutOfTheGamesPictureAndTheGameMeasuresItAlike()
    {
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var game = FindGame();
        var cities = new CatArchive(game.Read("CITIES.CAT"));
        foreach (var entry in AmigaPictureList.Entries)
        {
            var own = PcPicture(game, cities, entry.Name);
            var amiga = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, entry.File)!));
            if (entry.Crop)
            {
                amiga = amiga.Crop(own.Width, own.Height);
            }
            if (entry.WindowWidth > 0)
            {
                // D-681: the picture of the PC's measured size on the PC's canvas; the few pixels the measure needed are the only ones changed
                Assert.Equal((entry.WindowWidth, entry.WindowHeight), Measure(own.Pixels, own.Width, own.Height));
                var window = amiga.OnCanvas(entry.WindowWidth, entry.WindowHeight, own.Width, own.Height, out var adjusted);
                Assert.True(adjusted <= MostAdjustedPixels, $"{entry.Name}: {adjusted} pixels of the measured lines adjusted");
                Assert.Equal(entry.WindowWidth * entry.WindowHeight - adjusted, Enumerable.Range(0, entry.WindowHeight).Sum(row => Enumerable.Range(0, entry.WindowWidth).Count(x => window.Pixels[row * own.Width + x] == amiga.Pixels[row * amiga.Width + x])));
                Assert.All(Enumerable.Range(0, own.Height).SelectMany(row => Enumerable.Range(0, own.Width).Where(x => x >= entry.WindowWidth || row >= entry.WindowHeight).Select(x => window.Pixels[row * own.Width + x])), pixel => Assert.Equal(0, pixel));
                var ownWindow = Cut(own.Pixels, own.Width, entry.WindowWidth, entry.WindowHeight);
                var windowScore = EdgeOverlap(ownWindow, Cut(window.Pixels, own.Width, entry.WindowWidth, entry.WindowHeight), entry.WindowWidth, entry.WindowHeight);
                Assert.True(windowScore >= LeastWindowScore, $"{entry.Name}: layout score {windowScore:0.00} in the window");
                amiga = window;
            }
            Assert.Equal((own.Width, own.Height), (amiga.Width, amiga.Height));
            if (!entry.Backdrop && entry.WindowWidth == 0)
            {
                var score = EdgeOverlap(own.Pixels, amiga.Pixels, own.Width, own.Height);
                Assert.True(score >= LeastLayoutScore, $"{entry.Name}: layout score {score:0.00}");
            }
            if (cities.Entries.Any(city => string.Equals(city.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                // game 00f7:3e38 load_picture_measure: the width along the first row and the height down the first column, up to the first colour 0
                Assert.Equal(Measure(own.Pixels, own.Width, own.Height), Measure(amiga.Pixels, amiga.Width, amiga.Height));
            }
        }
    }

    [SkippableFact]
    public void ThePicturesNotOnTheListAreOnesTheGameCouldNotUseAsTheyAre()
    {
        // the redrawn pictures (a different layout) really score below the line; the list is not a guess
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var game = FindGame();
        var cities = new CatArchive(game.Read("CITIES.CAT"));
        foreach (var name in new[] { "ad", "camera", "gender", "training", "sneakin", "wiretap", "geneva", "helsinki", "istanbul", "washingt" })
        {
            var own = PcPicture(game, cities, name + ".pic");
            var amiga = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, name + ".pix")!));
            Assert.True(EdgeOverlap(own.Pixels, amiga.Pixels, own.Width, own.Height) < LeastLayoutScore, name);
        }
    }

    /// <summary>Where the tool below writes the pictures (outside the repository); unset: none are written.</summary>
    private const string ScreensVariable = "COVERT_AMIGA_SCREENS_DIR";

    /// <summary>
    /// A tool for looking at the list: every picture of it, loaded by the game's own picture loader with the option on and written
    /// as <c>picture-&lt;name&gt;.png</c> (skipped unless <c>COVERT_AMIGA_SCREENS_DIR</c> is set). A city picture is shown as the
    /// campaign shows it, measured and put at the corner of an empty screen; the other pictures fill the screen. The recorded
    /// sessions show the pictures in the game's screens (<c>AmigaPictureScreensTests</c>), which meet only some of them.
    /// </summary>
    [SkippableFact]
    public void EveryPictureOfTheListIsWrittenAsAPngWhenAskedTo()
    {
        var screens = Environment.GetEnvironmentVariable(ScreensVariable);
        Skip.If(string.IsNullOrEmpty(screens), $"set {ScreensVariable} to write the pictures as PNG files");
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var game = FindGame();
        var cities = new CatArchive(game.Read("CITIES.CAT"));
        var graphics = new McgaGraphicsDriver(new FontSet(game.Read("FONTS.CV")), McgaDriverFile.Read(game.Read("MGRAPHIC.EXE")).DefaultColours);
        var link = new HostLink(graphics, new HeadlessHost([]));
        using var files = new GameFolderFiles(game);
        var source = AmigaPictureSource.Open(graphics.PictureColours, pictures, game.Path, _ => { });
        Assert.NotNull(source);
        var loader = new LibraryPictures(new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), files, source));
        var gameLoader = new LibraryPictures(new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), files));
        graphics.SubCode0208(0);
        Assert.True(loader.OpenArchive("CITIES.CAT"));
        Assert.True(gameLoader.OpenArchive("CITIES.CAT"));
        Directory.CreateDirectory(screens!);
        const int backPage = 1, cornerX = 0xf0, cornerY = 0x8c;
        // each picture twice: as the Amiga's replacement shows (picture-name.png) and as the game's own shows (picture-name-pc.png), for looking at side by side
        void Show(LibraryPictures pictureLoader, AmigaPictureEntry entry, string suffix)
        {
            graphics.FillPage(0, 0);
            graphics.FillPage(backPage, 0);
            if (cities.Entries.All(city => !string.Equals(city.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                pictureLoader.LoadPicture(0, entry.Name);
            }
            else
            {
                pictureLoader.LoadPicture(backPage, entry.Name);
                // game 00f7:3e38 and 00f7:3cfd: the picture is measured, and its measured part is put centred on the corner's place
                var (width, height) = Measure(graphics.Pages[backPage], McgaGraphicsDriver.Width, McgaGraphicsDriver.Height);
                for (var row = 0; row < height; row++)
                {
                    Array.Copy(graphics.Pages[backPage], row * McgaGraphicsDriver.Width, graphics.Pages[0], (cornerY - height / 2 + row) * McgaGraphicsDriver.Width + cornerX - width / 2, width);
                }
            }
            File.WriteAllBytes(Path.Combine(screens!, $"picture-{entry.Name[..^4]}{suffix}.png"), PngFile.Screen(graphics.Screen, graphics.Palette));
        }
        foreach (var entry in AmigaPictureList.Entries)
        {
            Show(loader, entry, "");
            Show(gameLoader, entry, "-pc");
        }
    }

    private static PictureFile PcPicture(GameFolder game, CatArchive cities, string name)
    {
        var city = cities.Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        return PictureFile.Read(city is not null ? cities.Contents(city) : game.Read(name.ToUpperInvariant()));
    }

    /// <summary>
    /// How much of the edges of one picture (a pixel that differs from its right or lower neighbour) the other has too: the
    /// edges in both over the edges in either. The colours do not matter, only where the picture changes, so a picture in
    /// the Amiga's colours is compared with the PC's.
    /// </summary>
    private static double EdgeOverlap(byte[] a, byte[] b, int width, int height)
    {
        int both = 0, either = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var edgeA = (x + 1 < width && a[i] != a[i + 1]) || (y + 1 < height && a[i] != a[i + width]);
                var edgeB = (x + 1 < width && b[i] != b[i + 1]) || (y + 1 < height && b[i] != b[i + width]);
                both += edgeA && edgeB ? 1 : 0;
                either += edgeA || edgeB ? 1 : 0;
            }
        }
        return either == 0 ? 1 : (double)both / either;
    }

    private static byte[] Cut(byte[] pixels, int width, int cutWidth, int cutHeight)
    {
        var cut = new byte[cutWidth * cutHeight];
        for (var row = 0; row < cutHeight; row++)
        {
            Array.Copy(pixels, row * width, cut, row * cutWidth, cutWidth);
        }
        return cut;
    }

    private const int WidestThumbnail = 0xa0, HighestThumbnail = 0x64;

    /// <summary>The picture's width and height as the campaign measures it (up to 160 and 100): along the first row and down the first column to the first pixel of colour 0.</summary>
    private static (int Width, int Height) Measure(byte[] pixels, int width, int height)
    {
        int x = 0, y = 0;
        while (x < width && x < WidestThumbnail && pixels[x] != 0)
        {
            x++;
        }
        while (y < height && y < HighestThumbnail && pixels[y * width] != 0)
        {
            y++;
        }
        return (x, y);
    }
}
