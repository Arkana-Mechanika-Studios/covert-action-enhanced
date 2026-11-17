using CovertAction.Content;
using CovertAction.Core.Chase;
using CovertAction.Core.Electronics;
using CovertAction.Core.Raid;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Msc;
using CovertAction.Persistence.Dgroup;
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
            if (!entry.Backdrop && !entry.Cells && entry.WindowWidth == 0)
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
        foreach (var name in new[] { "camera", "wiretap", "geneva", "helsinki", "istanbul" })
        {
            var own = PcPicture(game, cities, name + ".pic");
            var amiga = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, name + ".pix")!));
            Assert.True(EdgeOverlap(own.Pixels, amiga.Pixels, own.Width, own.Height) < LeastLayoutScore, name);
        }
    }

    /// <summary>
    /// D-685: the Amiga's choice-of-agent picture has the woman on the left and the man on the right, the PC's the reverse (Max
    /// on the left in the blue panel, Maxine on the right in the red one). The replacement is the Amiga's two halves exchanged:
    /// every pixel the Amiga's, in the Amiga's colour, and the woman (her red background) now on the right.
    /// </summary>
    [SkippableFact]
    public void TheAgentsPictureShowsTheManOnTheLeftAndTheWomanOnTheRightWithEveryPixelTheAmigas()
    {
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var game = FindGame();
        var cities = new CatArchive(game.Read("CITIES.CAT"));
        var own = PcPicture(game, cities, "gender.pic");
        var amiga = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, "gender.pix")!));
        Assert.Equal((own.Width, own.Height), (amiga.Width, amiga.Height));
        var colours = new PictureColours(new byte[3 * 256]);
        var source = AmigaPictureSource.Open(colours, pictures, "", _ => { });
        var shown = source!.Find("gender.pic", own.Width, own.Height)!.Value.Pixels;
        var expected = source.DisplayPixels(amiga.SwapHalves());
        Assert.Equal(expected, shown);
        var half = amiga.Width / 2;
        // the swap is exact: each row is the Amiga's right half then its left half, and no colour is new
        for (var row = 0; row < amiga.Height; row++)
        {
            var amigaRow = amiga.Pixels.AsSpan(row * amiga.Width, amiga.Width);
            var shownRow = shown.AsSpan(row * amiga.Width, amiga.Width);
            var direct = source.DisplayPixels(amiga);
            Assert.True(direct.AsSpan(row * amiga.Width + half, half).SequenceEqual(shownRow[..half]), $"row {row} left");
            Assert.True(direct.AsSpan(row * amiga.Width, half).SequenceEqual(shownRow[half..]), $"row {row} right");
            Assert.True(amigaRow.Length == shownRow.Length);
        }
        // the woman's panel is the one of the Amiga's red background (colour 0xf00): on the Amiga's left, on the shown picture's right
        var red = Array.IndexOf(amiga.Colours, (ushort)0xf00);
        Assert.True(red >= 0);
        int RedIn(byte[] pixels, int from) => Enumerable.Range(0, amiga.Height).Sum(row => Enumerable.Range(from, half).Count(x => pixels[row * amiga.Width + x] == red));
        Assert.True(RedIn(amiga.Pixels, 0) > 4 * RedIn(amiga.Pixels, half));
        Assert.True(RedIn(amiga.SwapHalves().Pixels, half) > 4 * RedIn(amiga.SwapHalves().Pixels, 0));
        // the PC's woman (the game's red, colour 4) is on its right
        Assert.True(Enumerable.Range(0, own.Height).Sum(row => Enumerable.Range(half, half).Count(x => own.Pixels[row * own.Width + x] == 4)) > 4 * Enumerable.Range(0, own.Height).Sum(row => Enumerable.Range(0, half).Count(x => own.Pixels[row * own.Width + x] == 4)));
    }

    /// <summary>
    /// D-686: the game cuts the workbench's 32 wire pieces (8 x 8, two rows of 16 from row 16) and 12 chips (16 x 16, side by side
    /// along the top) out of the top 32 rows of the sheet. The Amiga's cells lie one pixel to the right: moving the strip one
    /// pixel left puts the Amiga's cells in the game's rectangles, loses no drawn pixel of the strip, and agrees with the PC's
    /// cells in far more pixels than the unmoved sheet.
    /// </summary>
    [SkippableFact]
    public void TheWorkbenchCellsTheGameCutsAreTheAmigasCellsOnePixelToTheRightWithNoDrawnPixelLost()
    {
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var game = FindGame();
        var cities = new CatArchive(game.Read("CITIES.CAT"));
        var own = PcPicture(game, cities, "bugs.pic");
        var amiga = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, "bugs.pix")!));
        var source = AmigaPictureSource.Open(new PictureColours(new byte[3 * 256]), pictures, "", _ => { });
        var shown = source!.Find("bugs.pic", own.Width, own.Height)!.Value.Pixels;
        var plain = source.DisplayPixels(amiga);
        const int width = 320;
        var cells = new List<(int X, int Y, int Size)>();
        cells.AddRange(Enumerable.Range(0, 32).Select(i => (i % 16 * 8, 16 + i / 16 * 8, 8)));
        cells.AddRange(Enumerable.Range(0, 12).Select(i => (i * 16, 0, 16)));
        int Differences(byte[] pixels, bool pcColours)
        {
            // where the two pictures agree about "drawn or not" (the colours are the Amiga's own and are not compared with the PC's numbers)
            return cells.Sum(cell => Enumerable.Range(0, cell.Size).Sum(y => Enumerable.Range(0, cell.Size).Count(x =>
                (pixels[(cell.Y + y) * width + cell.X + x] == 0) != (own.Pixels[(cell.Y + y) * width + cell.X + x] == 0))));
        }
        // every game rectangle holds the Amiga's cell one pixel to the right
        foreach (var (x0, y0, size) in cells)
        {
            for (var y = 0; y < size; y++)
            {
                Assert.True(shown.AsSpan((y0 + y) * width + x0, size).SequenceEqual(plain.AsSpan((y0 + y) * width + x0 + 1, size)), $"cell at {x0}, {y0} row {y}");
            }
        }
        // the strip's first column (which the move drops) has no drawn pixel on these rows, and every other drawn pixel of the strip is shown once
        Assert.All(Enumerable.Range(0, 32), y => Assert.Equal(0, plain[y * width]));
        var drawnBefore = Enumerable.Range(0, 32).Sum(y => Enumerable.Range(1, 192).Count(x => plain[y * width + x] != 0));
        var drawnAfter = Enumerable.Range(0, 32).Sum(y => Enumerable.Range(0, 192).Count(x => shown[y * width + x] != 0));
        Assert.Equal(drawnBefore, drawnAfter);
        // nothing outside the strip changed
        for (var i = 0; i < shown.Length; i++)
        {
            if (i / width >= 32 || i % width >= 192)
            {
                Assert.Equal(plain[i], shown[i]);
            }
        }
        // the cells fit the PC's far better: the PC's cells and the Amiga's differ in the drawn pixels of the cells 216 + 362 times before the move
        var before = Differences(plain, false);
        var after = Differences(shown, false);
        Assert.True(after * 2 < before, $"{after} cell pixels differ from the PC's after the move, {before} before");
        // the drawn pixels of the chips are the same number as the PC's chips have, cell by cell (the art is the same art), except the last chip
        // and the last wire piece: the Amiga's board begins one column earlier than the PC's, so the last column of each of the two cells is the
        // board's edge (16 pixels of the Amiga's board in each, drawn by the Amiga there)
        int Drawn(byte[] pixels, int x0, int y0, int size) => Enumerable.Range(0, size).Sum(y => Enumerable.Range(0, size).Count(x => pixels[(y0 + y) * width + x0 + x] != 0));
        for (var i = 0; i < 11; i++)
        {
            Assert.Equal(Drawn(own.Pixels, i * 16, 0, 16), Drawn(shown, i * 16, 0, 16));
        }
        Assert.Equal(Drawn(own.Pixels, 176, 0, 16) + 16, Drawn(shown, 176, 0, 16));
        Assert.All(Enumerable.Range(0, 16), y => Assert.NotEqual(0, shown[y * width + 191]));
        Assert.All(Enumerable.Range(16, 16), y => Assert.NotEqual(0, shown[y * width + 127]));
    }

    /// <summary>The colour (0x0RGB, four bits each) the game's driver shows for a palette entry of a part's table (the table's value is an entry of the driver's 64 colours).</summary>
    private static int PaletteColour(byte[] driverColours, byte[] palette, int index) =>
        Enumerable.Range(0, 3).Aggregate(0, (colour, k) => colour << 4 | driverColours[3 * palette[index] + k] * 15 / 63);

    /// <summary>
    /// D-687: the sheets whose colours the game changes by number (the raid's flashes and status bar, the car offers' body colours,
    /// the workbench board's frames, the equipment screen's cursor) cannot be shown with the Amiga's pictures under the rule of
    /// D-685: the pixels the game changes would have to sit at the game's numbers (0 to 15), where they show the game's colour of
    /// that number, and every pixel has to keep the Amiga's colour. This reads the game's own palettes (the executables' data)
    /// and the two pictures and pins the facts that stop each sheet: an Amiga colour that serves two of the game's roles, or an
    /// Amiga colour that is not the colour of the number the game's role has.
    /// </summary>
    [SkippableFact]
    public void TheSheetsTheGameRecoloursByNumberCannotKeepTheGamesRolesAndTheAmigasColoursAtOnce()
    {
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var game = FindGame();
        var cities = new CatArchive(game.Read("CITIES.CAT"));
        var driver = McgaDriverFile.Read(game.Read("MGRAPHIC.EXE")).DefaultColours;
        var raid = new RaidState();
        TacDgroup.Layout.Load(Executable.Read(game.Read("TAC.EXE")).DataSegment(), raid);
        var bug = new ElectronicsState();
        BugDgroup.Layout.Load(Executable.Read(game.Read("BUG.EXE")).DataSegment(), bug);
        var chase = new ChaseState();
        ChaseDgroup.Layout.Load(Executable.Read(game.Read("CHASE.EXE")).DataSegment(), chase);

        (byte[] Pc, byte[] Amiga, ushort[] Colours) Both(string pc, string amiga)
        {
            var own = PcPicture(game, cities, pc + ".pic");
            var other = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, amiga + ".pix")!));
            Assert.Equal((own.Width, own.Height), (other.Width, other.Height));
            return (own.Pixels, other.Pixels, other.Colours);
        }
        // how many of the game's pixels of number "role" (in the rows from..to) the Amiga draws with colour number "amigaColour"
        static int Count(byte[] pc, byte[] amiga, int role, int amigaColour, int fromRow = 0, int toRow = 200) =>
            Enumerable.Range(fromRow * 320, (toRow - fromRow) * 320).Count(i => pc[i] == role && amiga[i] == amigaColour);
        static int Of(byte[] pixels, int colour, int fromRow = 0, int toRow = 200) =>
            Enumerable.Range(fromRow * 320, (toRow - fromRow) * 320).Count(i => pixels[i] == colour);

        // the raid's screen (equip1 for Maxine, equip1m for Max): the alarm turns the game's grey (7) red, the flashes and the room's
        // palette change number 3 (cyan, white in the room, yellow, black), the kit cursor recolours number 1 in the item boxes
        foreach (var name in new[] { "equip1", "equip1m" })
        {
            var (pc, amiga, colours) = Both(name, name);
            // the grey is clean: every pixel of number 7 is the Amiga's colour 30, which is the grey the game's number 7 shows
            Assert.Equal(Of(pc, 7), Count(pc, amiga, 7, 30));
            Assert.Equal(Of(pc, 7), Of(amiga, 30));
            Assert.Equal(PaletteColour(driver, raid.PaletteSets[0], 7), colours[30]);
            Assert.Equal(PaletteColour(driver, raid.PaletteSets[1], 7), colours[30]);
            // number 3 is not: its 22 pixels are the Amiga's colour 3, which also fills the 9,494 pixels the game draws with number 9
            // (and 6 of number 15), so colour 3 serves the flashing role and two quiet ones; and it is not the game's colour of 3
            Assert.Equal(22, Of(pc, 3));
            Assert.Equal(22, Count(pc, amiga, 3, 3));
            Assert.True(Count(pc, amiga, 9, 3) > 9000, name);
            Assert.NotEqual(colours[3], PaletteColour(driver, raid.PaletteSets[0], 3));
            Assert.NotEqual(colours[3], PaletteColour(driver, raid.PaletteSets[1], 3));
        }

        // the workbench's board: the fixed chips' frames recolour numbers 2 and 3 (the grid's cyan lines) to grey
        {
            var (pc, amiga, colours) = Both("board", "board");
            Assert.Equal(Of(pc, 2), Count(pc, amiga, 2, 2));
            Assert.Equal(PaletteColour(driver, bug.Palettes[4], 2), colours[2]);
            Assert.Equal(Of(pc, 3), Count(pc, amiga, 3, 3));
            Assert.True(Count(pc, amiga, 9, 3) > 200);
            Assert.NotEqual(colours[3], PaletteColour(driver, bug.Palettes[4], 3));
            // the wires (read as number 10 at the cell centres) are clean: the same pixels, the game's own green
            Assert.Equal(Of(pc, 10), Count(pc, amiga, 10, 10));
            Assert.Equal(PaletteColour(driver, bug.Palettes[4], 10), colours[10]);
        }

        // the car offers: the bodies are recoloured by level (numbers 11 and 3), but only the first offer of each level is; the others
        // show the game's own colours of those numbers (cyan) on the screen
        {
            var (pc, amiga, colours) = Both("cars", "cars");
            Assert.Equal(Of(pc, 11), Count(pc, amiga, 11, 11));
            Assert.Equal(Of(pc, 3), Count(pc, amiga, 3, 3));
            Assert.NotEqual(colours[11], PaletteColour(driver, chase.PaletteChase, 11));
            Assert.NotEqual(colours[3], PaletteColour(driver, chase.PaletteChase, 3));
            // and the highlight recolours the background (number 1) of a whole offer, where the Amiga draws 103 pixels of the game's number 5 in the colour of 1
            Assert.Equal(103, Count(pc, amiga, 5, 1));
        }

        // the raid's guards' sheets (the Amiga's are redrawn: no pixel-by-pixel roles): the alarm's red turns the game's grey (7), whose
        // Amiga colour 7 is another grey (999) that covers less than half of them
        foreach (var (pcName, amigaName) in new[] { ("guys2", "guysnew2"), ("guys3", "guysnew3") })
        {
            var (pc, amiga, colours) = Both(pcName, amigaName);
            Assert.NotEqual(colours[7], PaletteColour(driver, raid.PaletteSets[0], 7));
            Assert.True(Count(pc, amiga, 7, 7) * 2 < Of(pc, 7), pcName);
            Assert.Equal(0, Of(amiga, 30));     // the Amiga's own aaa is not used on these sheets
        }

        // the raid's sprite sheets: the status bar (rows 160 to 199 of the people's page) is recoloured by number: the disguise's number 13
        // (black in the raid's palette) and the gas's blue 9; the Amiga draws the first in white and the second in 48e, neither the colour of the number
        foreach (var (pcName, amigaName) in new[] { ("sprites", "sprites"), ("spritesf", "spritesf") })
        {
            var (pc, amiga, colours) = Both(pcName, amigaName);
            Assert.True(Count(pc, amiga, 13, 15, 160, 200) * 10 > Of(pc, 13, 160, 200) * 6, pcName);
            Assert.Equal(0xfff, colours[15]);
            Assert.NotEqual(colours[15], PaletteColour(driver, raid.PaletteSets[0], 13));
            Assert.True(Count(pc, amiga, 9, 3, 160, 200) * 10 > Of(pc, 9, 160, 200) * 8, pcName);
            Assert.NotEqual(colours[3], PaletteColour(driver, raid.PaletteSets[0], 9));
        }
    }

    /// <summary>
    /// D-688: Washington's Amiga picture is the same view of the Capitol as the PC's, in the Amiga's colours. Its colour 0 is not black
    /// (a pale violet, the canvas around the picture) but the game's colour 0 is what the campaign measures by, so the window must hold
    /// no pixel of the Amiga's colour 0 (it would show black); the game's measure is the PC's 130 x 48 on both, and what the window
    /// drops (two rows below) is not shown by the game.
    /// </summary>
    [SkippableFact]
    public void WashingtonsWindowIsTheAmigasViewWithNoPixelOfItsColourZeroAndTheGameMeasuresItAsThePcs()
    {
        var pictures = AmigaFolderSearch.ResolvePictures(AmigaFolder());
        Skip.If(pictures is null, "no Amiga picture files");
        var game = FindGame();
        var cities = new CatArchive(game.Read("CITIES.CAT"));
        var own = PcPicture(game, cities, "washingt.pic");
        var amiga = AmigaPictureFile.Read(File.ReadAllBytes(AmigaFolderSearch.BankFile(pictures!, "washingt.pix")!));
        var entry = AmigaPictureList.Find("washingt.pic");
        Assert.NotNull(entry);
        Assert.Equal((130, 48), (entry.WindowWidth, entry.WindowHeight));
        Assert.Equal((130, 48), Measure(own.Pixels, own.Width, own.Height));
        // the Amiga's own picture is measured the same (its first row and column), so no pixel of the first row or column is made up
        Assert.Equal((130, 48), Measure(amiga.Pixels, amiga.Width, amiga.Height));
        var window = amiga.OnCanvas(130, 48, own.Width, own.Height, out var adjusted);
        Assert.Equal(0, adjusted);
        // the Amiga's colour 0 is a pale violet: not one pixel of the window has it, so none is shown black
        Assert.NotEqual((ushort)0, amiga.Colours[0]);
        Assert.Equal(0, Enumerable.Range(0, 48).Sum(row => Enumerable.Range(0, 130).Count(x => window.Pixels[row * own.Width + x] == 0)));
        // every pixel of the window is the Amiga's, in the Amiga's place
        Assert.All(Enumerable.Range(0, 48), row => Assert.True(amiga.Pixels.AsSpan(row * amiga.Width, 130).SequenceEqual(window.Pixels.AsSpan(row * own.Width, 130)), $"row {row}"));
        var colours = new PictureColours(new byte[3 * 256]);
        var shown = AmigaPictureSource.Open(colours, pictures, "", _ => { })!.Find("washingt.pic", own.Width, own.Height)!.Value.Pixels;
        Assert.Equal(130 * 48, Enumerable.Range(0, 48).Sum(row => Enumerable.Range(0, 130).Count(x => shown[row * own.Width + x] >= PictureColours.First)));
        Assert.Equal((130, 48), Measure(shown, own.Width, own.Height));
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
