using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Msc;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The Amiga pictures option (D-642) on pictures made in the tests (<see cref="AmigaPixBuilder"/>; no Amiga file is needed):
/// the reader, the display colours the pictures' own colours are given, the replacement of a game's picture on a page, and the
/// list of the pictures that may be replaced.
/// </summary>
public sealed class AmigaPicturesTests : IDisposable
{
    private readonly List<GameFolderFiles> _files = [];

    public void Dispose()
    {
        _files.ForEach(files => files.Dispose());
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Colours of the kind a picture has: black, a red, a green, a blue, then 12 more, each different (16 in all).</summary>
    private static ushort[] SixteenColours() =>
        [0x000, 0xf00, 0x0f0, 0x00f, 0x111, 0x222, 0x333, 0x444, 0x555, 0x666, 0x777, 0x888, 0x999, 0xaaa, 0xbbb, 0xccc];

    private static ushort[] ThirtyTwoColours() => [.. SixteenColours(), .. SixteenColours().Select(colour => (ushort)(colour ^ 0x00f))];

    // the reader

    [Fact]
    public void APictureOfTwoPalettesHasOneByteAPixelAndThirtyTwoColours()
    {
        byte[] pixels = [0, 1, 2, 3, 31, 17, 16, 5, 0, 0, 9, 30];
        var colours = ThirtyTwoColours();
        var picture = AmigaPictureFile.Read(AmigaPixBuilder.Make(4, 3, pixels, colours));
        Assert.Equal((4, 3), (picture.Width, picture.Height));
        Assert.Equal(pixels, picture.Pixels);
        Assert.Equal(colours, picture.Colours);
    }

    [Fact]
    public void APictureOfOnePaletteHasTwoPixelsToAByteAsThePcsDoAndSixteenColours()
    {
        // an odd width: the last pixel of each row has a byte to itself
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 6, 7];
        var picture = AmigaPictureFile.Read(AmigaPixBuilder.Make(5, 3, pixels, SixteenColours()));
        Assert.Equal(pixels, picture.Pixels);
        Assert.Equal(16, picture.Colours.Length);
    }

    [Fact]
    public void ALargerPictureThanOneRoundOfTheCodeTableReadsBack()
    {
        // more than 256 codes: the table starts over inside the picture
        var pixels = Enumerable.Range(0, 80 * 60).Select(i => (byte)(i * 7 % 32)).ToArray();
        var picture = AmigaPictureFile.Read(AmigaPixBuilder.Make(80, 60, pixels, ThirtyTwoColours()));
        Assert.Equal(pixels, picture.Pixels);
    }

    [Fact]
    public void ADamagedPictureIsRefusedWithItsReason()
    {
        var good = AmigaPixBuilder.Make(4, 3, new byte[12], ThirtyTwoColours());
        Assert.Throws<InvalidDataException>(() => AmigaPictureFile.Read(good[..5]));
        var threePalettes = (byte[])good.Clone();
        threePalettes[0] = 3;
        Assert.Contains("3 palettes", Assert.Throws<InvalidDataException>(() => AmigaPictureFile.Read(threePalettes)).Message);
        var noWidth = (byte[])good.Clone();
        noWidth[2] = noWidth[3] = 0;
        Assert.Throws<InvalidDataException>(() => AmigaPictureFile.Read(noWidth));
        Assert.Throws<InvalidDataException>(() => AmigaPictureFile.Read(good[..^6]));
    }

    [Fact]
    public void ACropKeepsTheTopLeftPartAndTheColours()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        var picture = AmigaPictureFile.Read(AmigaPixBuilder.Make(4, 3, pixels, ThirtyTwoColours()));
        var cropped = picture.Crop(2, 2);
        Assert.Equal((2, 2), (cropped.Width, cropped.Height));
        Assert.Equal(new byte[] { 1, 2, 5, 6 }, cropped.Pixels);
        Assert.Same(picture.Colours, cropped.Colours);
        Assert.Throws<ArgumentOutOfRangeException>(() => picture.Crop(5, 2));
    }

    [Fact]
    public void SwappingTheHalvesIsAPermutationOfWholeColumnsAndSwappingTwiceGivesTheOriginal()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        var picture = AmigaPictureFile.Read(AmigaPixBuilder.Make(4, 3, pixels, ThirtyTwoColours()));
        var swapped = picture.SwapHalves();
        Assert.Equal((4, 3), (swapped.Width, swapped.Height));
        Assert.Equal(new byte[] { 3, 4, 1, 2, 7, 8, 5, 6, 11, 12, 9, 10 }, swapped.Pixels);
        Assert.Same(picture.Colours, swapped.Colours);
        Assert.Equal(pixels, swapped.SwapHalves().Pixels);
        Assert.Throws<InvalidOperationException>(() => AmigaPictureFile.Read(AmigaPixBuilder.Make(3, 1, [1, 2, 3], ThirtyTwoColours())).SwapHalves());
    }

    [Fact]
    public void TheMenuPictureOfTheAgentsShowsTheAmigasHalvesExchangedAndOnlyWhenTheFileIsThere()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];
        var source = Source(colours, ("gender.pix", AmigaPixBuilder.Make(4, 2, pixels, ThirtyTwoColours())));
        var replacement = source.Find("gender.pic", 4, 2);
        Assert.NotNull(replacement);
        // the display colours are given in order of first appearance of the swapped picture: 3, 4, 1, 2, 7, 8, 5, 6
        Assert.Equal(new byte[] { 128, 129, 130, 131, 132, 133, 134, 135 }, replacement.Value.Pixels);
        Assert.Null(source.Find("gender.pic", 8, 2));
    }

    [Fact]
    public void MovingARegionCopiesItWholeAndLeavesEverythingOutsideTheTargetAsItWas()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        var picture = AmigaPictureFile.Read(AmigaPixBuilder.Make(4, 3, pixels, ThirtyTwoColours()));
        // the strip of columns 1..3 of the first two rows goes one place to the left (the rectangles overlap)
        var moved = picture.MoveRegion(1, 0, 3, 2, 0, 0);
        Assert.Equal(new byte[] { 2, 3, 4, 4, 6, 7, 8, 8, 9, 10, 11, 12 }, moved.Pixels);
        Assert.Same(picture.Colours, moved.Colours);
        Assert.Equal(pixels, picture.Pixels);     // the picture itself is not changed
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4 }, picture.MoveRegion(0, 0, 4, 1, 0, 2).Pixels.Take(8).Concat(picture.MoveRegion(0, 0, 4, 1, 0, 2).Pixels.Skip(8).Take(4)).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => picture.MoveRegion(1, 0, 4, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => picture.MoveRegion(0, 0, 2, 2, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => picture.MoveRegion(0, 0, 0, 2, 0, 0));
    }

    [Fact]
    public void TheWorkbenchSheetShowsTheAmigasStripMovedOnePixelLeftAndNothingElseChanges()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        // a sheet of 320 x 200 in which a pixel's right neighbour has another number
        var sheet = Enumerable.Range(0, 320 * 200).Select(i => (byte)(1 + (i % 320 + 2 * (i / 320)) % 9)).ToArray();
        var file = AmigaPixBuilder.Make(320, 200, sheet, ThirtyTwoColours());
        var source = Source(colours, ("bugs.pix", file));
        var shown = source.Find("bugs.pic", 320, 200)!.Value.Pixels;
        var plain = source.DisplayPixels(AmigaPictureFile.Read(file));
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                var moved = y < 32 && x < 192;
                Assert.Equal(plain[y * 320 + x + (moved ? 1 : 0)], shown[y * 320 + x]);
            }
        }
        Assert.Null(source.Find("bugs.pic", 160, 100));
    }

    // the replacement of a picture

    private static AmigaPictureSource Source(PictureColours colours, params (string File, byte[] Bytes)[] files)
    {
        var folder = files.ToDictionary(file => file.File, file => file.Bytes, StringComparer.OrdinalIgnoreCase);
        return new AmigaPictureSource(colours, name => folder.GetValueOrDefault(name), _ => { });
    }

    [Fact]
    public void ThePixelsOfAReplacementAreTheDisplayColoursOfThePicturesOwnColoursAndColourZeroStaysZero()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        byte[] pixels = [0, 3, 3, 0, 2, 2, 31, 0];
        var source = Source(colours, ("faces.pix", AmigaPixBuilder.Make(4, 2, pixels, ThirtyTwoColours())));
        var replacement = source.Find("FACES.PIC", 4, 2);
        Assert.NotNull(replacement);
        // colour 0 stays the game's colour 0 (what sprites and measures treat as "nothing"); 3, 2 and 31 get 128, 129, 130
        Assert.Equal(new byte[] { 0, 128, 128, 0, 129, 129, 130, 0 }, replacement.Value.Pixels);
        Assert.Equal(3, colours.Count);
    }

    [Fact]
    public void TwoPicturesThatShareAColourShareItsDisplayColour()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        var first = AmigaPixBuilder.Make(2, 1, [1, 2], ThirtyTwoColours());
        // the second picture's colour 7 is the first's colour 2 (0x0f0) in another place of another palette
        var palette = ThirtyTwoColours();
        palette[7] = 0x0f0;
        var second = AmigaPixBuilder.Make(2, 1, [7, 1], palette);
        var source = Source(colours, ("faces.pix", first), ("facesf.pix", second));
        var a = source.Find("faces.pic", 2, 1)!.Value.Pixels;
        var b = source.Find("facesf.pic", 2, 1)!.Value.Pixels;
        Assert.Equal(a[1], b[0]);     // the green
        Assert.Equal(a[0], b[1]);     // the red
    }

    [Fact]
    public void APictureNotOnTheListOrOfAnotherSizeOrWithoutItsFileStaysTheGames()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        var file = AmigaPixBuilder.Make(4, 2, new byte[8], ThirtyTwoColours());
        var source = Source(colours, ("faces.pix", file), ("camera.pix", file));
        Assert.Null(source.Find("camera.pic", 4, 2));     // redrawn on the Amiga: not on the list
        Assert.Null(source.Find("faces.pic", 8, 2));      // another size: not the same picture
        Assert.Null(source.Find("icons.pic", 4, 2));      // the file is not there
        Assert.NotNull(source.Find("faces.pic", 4, 2));
        Assert.Equal(0, colours.Count);                  // nothing was given a colour for them
    }

    [Fact]
    public void AThumbnailOnAFullCanvasIsCutToTheGamesSizeButOnlyWhereTheListSaysSo()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        var canvas = Enumerable.Range(0, 8 * 6).Select(i => (byte)(i % 8 < 4 && i / 8 < 3 ? 5 : 0)).ToArray();
        var file = AmigaPixBuilder.Make(8, 6, canvas, ThirtyTwoColours());
        var source = Source(colours, ("caracas.pix", file), ("vienna.pix", file));
        var cut = source.Find("caracas.pic", 4, 3);
        Assert.NotNull(cut);
        Assert.All(cut.Value.Pixels, pixel => Assert.Equal(128, pixel));
        Assert.Equal(4 * 3, cut.Value.Pixels.Length);
        Assert.Null(source.Find("vienna.pic", 4, 3));
        Assert.Null(source.Find("caracas.pic", 9, 3));
    }

    [Fact]
    public void AFileThatCannotBeReadIsToldOnceAndThePictureStaysTheGames()
    {
        var told = new List<string>();
        var colours = new PictureColours(new byte[3 * 256]);
        var source = new AmigaPictureSource(colours, _ => [1, 2, 3], told.Add);
        Assert.Null(source.Find("faces.pic", 4, 2));
        Assert.Null(source.Find("faces.pic", 4, 2));
        Assert.Single(told);
        Assert.Contains("faces.pix", told[0]);
    }

    // the loader

    private (LibraryPictures Pictures, McgaGraphicsDriver Graphics) Setup(GameFolder game, IPictureReplacementsFactory? replacements)
    {
        var graphics = new McgaGraphicsDriver(new FontSet(game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]);
        var link = new HostLink(graphics, new HeadlessHost([]));
        _files.Add(new GameFolderFiles(game));
        var world = new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), _files[^1],
            replacements?.Make(graphics.PictureColours));
        return (new LibraryPictures(world), graphics);
    }

    private interface IPictureReplacementsFactory
    {
        CovertAction.Platform.IPictureReplacements Make(PictureColours colours);
    }

    private sealed class FromFiles(params (string File, byte[] Bytes)[] files) : IPictureReplacementsFactory
    {
        public CovertAction.Platform.IPictureReplacements Make(PictureColours colours) => Source(colours, files);
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
        return folder ?? new GameFolder(Path.Combine(AppContext.BaseDirectory, "no-game"));
    }

    [SkippableFact]
    public void TheLoaderPutsTheReplacementOnThePageInPlaceOfTheGamesPixelsAndWithoutOneNothingChanges()
    {
        var game = FindGame();
        Skip.If(game.Find("FONTS.CV") is null, "no game files: set COVERT_GAME_DIR to a folder with the original game");
        var own = PictureFile.Read(game.Read("FACES.PIC"));
        // a made-up Amiga sheet of the game's size: the game's own pixel numbers 0..15 as the picture's colours, except 0
        var amiga = AmigaPixBuilder.Make(own.Width, own.Height, [.. own.Pixels.Select(p => (byte)(p == 0x09 ? 0x0a : p))], ThirtyTwoColours());

        var (plain, plainGraphics) = Setup(game, null);
        plain.LoadPictureSwapped("faces.pic", 1);
        Assert.True(plainGraphics.Pages[1].AsSpan(0, own.Width).SequenceEqual(own.Pixels.AsSpan(0, own.Width)));

        var (swapped, graphics) = Setup(game, new FromFiles(("faces.pix", amiga)));
        swapped.LoadPictureSwapped("faces.pic", 1);
        var page = graphics.Pages[1];
        for (var row = 0; row < own.Height; row++)
        {
            for (var column = 0; column < own.Width; column++)
            {
                var gamePixel = own.Pixels[row * own.Width + column];
                var shown = page[row * McgaGraphicsDriver.Width + column];
                // the game's colour 0 stays 0; every other colour of the sheet is one of the picture's own display colours
                Assert.True(gamePixel == 0 ? shown == 0 : shown >= PictureColours.First, $"pixel {column},{row}");
            }
        }
        Assert.True(graphics.PictureColours.Count is > 0 and <= 16);
        // the game's colours, 0 to 127, were not written
        Assert.All(graphics.Palette.AsSpan(0, 3 * PictureColours.First).ToArray(), value => Assert.Equal(0, value));
    }

    // where the files are

    private readonly string _root = Path.Combine(Path.GetTempPath(), "covert-amiga-pix-" + Guid.NewGuid().ToString("N"));

    private string Make(params string[] path)
    {
        var folder = Path.Combine([_root, .. path]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void WritePicture(string folder, string name) =>
        File.WriteAllBytes(Path.Combine(folder, name), AmigaPixBuilder.Make(4, 2, [1, 2, 3, 4, 5, 6, 7, 8], ThirtyTwoColours()));

    /// <summary>The disks as the Amiga's are laid out: faces and icons on disk 1, the rest of the list on disk 2, the street on disk 3.</summary>
    private string MakeDisks(params string[] below)
    {
        var disk1 = Make([.. below, "disk1"]);
        var disk2 = Make([.. below, "disk2"]);
        var disk3 = Make([.. below, "disk3"]);
        foreach (var entry in AmigaPictureList.Entries)
        {
            WritePicture(entry.File is "faces.pix" or "facesf.pix" or "icons.pix" ? disk1 : entry.File == "street.pix" ? disk3 : disk2, entry.File);
        }
        return disk1;
    }

    [Fact]
    public void ThePictureFilesAreFoundAroundTheFolderOfTheFirstDisk()
    {
        var program = Make("program");
        Assert.Null(AmigaFolderSearch.AutomaticPictures(null));
        var disk1 = MakeDisks("program", "amiga", "files");
        var found = AmigaFolderSearch.Candidates(null, program).Select(c => (Folder: AmigaFolderSearch.ResolvePictures(c.Folder), c.Source)).First(r => r.Folder is not null);
        Assert.Equal((disk1, "beside the program"), found);
        // the folder with the disks' folders, the first disk's folder and another disk's folder all lead to every file
        foreach (var named in new[] { Path.GetDirectoryName(disk1)!, disk1, Path.Combine(Path.GetDirectoryName(disk1)!, "disk3") })
        {
            Assert.Empty(AmigaFolderSearch.MissingPictures(AmigaFolderSearch.ResolvePictures(named)));
        }
        // a folder that holds the files itself, flat
        var flat = Make("flat");
        foreach (var entry in AmigaPictureList.Entries)
        {
            WritePicture(flat, entry.File);
        }
        Assert.Equal(flat, AmigaFolderSearch.ResolvePictures(flat));
        Assert.Null(AmigaFolderSearch.ResolvePictures(Make("empty")));
        Assert.Null(AmigaFolderSearch.ResolvePictures(Path.Combine(_root, "not-there")));
    }

    [Fact]
    public void TheMissingPictureFilesAreNamed()
    {
        var disk1 = MakeDisks("a");
        File.Delete(Path.Combine(Path.GetDirectoryName(disk1)!, "disk2", "europe.pix"));
        Assert.Equal(["europe.pix"], AmigaFolderSearch.MissingPictures(AmigaFolderSearch.ResolvePictures(disk1)));
        Assert.Equal(AmigaPictureList.Entries.Count, AmigaFolderSearch.MissingPictures(null).Count);
    }

    [Fact]
    public void WithoutTheFilesTheOptionIsToldAndTheGamesPicturesShowAndWithSomeOnlyThoseStay()
    {
        var told = new List<string>();
        Assert.Null(AmigaPictureSource.Open(new PictureColours(new byte[3 * 256]), Make("nothing"), Path.Combine(_root, "game"), told.Add));
        Assert.Contains("faces.pix is not in", Assert.Single(told));
        told.Clear();

        var disk1 = MakeDisks("some");
        File.Delete(Path.Combine(Path.GetDirectoryName(disk1)!, "disk2", "europe.pix"));
        var source = AmigaPictureSource.Open(new PictureColours(new byte[3 * 256]), Path.GetDirectoryName(disk1), Path.Combine(_root, "game"), told.Add);
        Assert.NotNull(source);
        Assert.Contains("europe.pix", Assert.Single(told));
        Assert.Null(source.Find("europe.pic", 4, 2));
        Assert.NotNull(source.Find("faces.pic", 4, 2));
    }

    // the list

    [Fact]
    public void TheListNamesEachPictureOnceWithItsAmigaFileAndAReason()
    {
        var entries = AmigaPictureList.Entries;
        Assert.Equal(entries.Count, entries.Select(entry => entry.Name.ToLowerInvariant()).Distinct().Count());
        Assert.All(entries, entry =>
        {
            Assert.EndsWith(".pic", entry.Name);
            Assert.Equal(Path.ChangeExtension(entry.Name, ".pix"), entry.File);
            Assert.True(entry.Reason.Length > 20, entry.Name);
        });
    }

    [Fact]
    public void ThePicturesTheGamesCodeReadsBackOrRecoloursByColourNumberAreNotOnTheList()
    {
        string[] stay = ["board", "cars", "equip1", "equip1m", "camera", "wiretap", "guys2", "guys3", "sprites", "spritesf",
            "geneva", "helsinki", "istanbul", "khartoum", "havana", "kingston"];
        Assert.All(stay, name => Assert.Null(AmigaPictureList.Find(name + ".pic")));
    }
}
