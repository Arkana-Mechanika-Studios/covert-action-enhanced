using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Msc;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// Loading pictures through the game's file calls onto a page of the game's drawing code: every picture file of the
/// player's game, and the pictures of an archive, must arrive on the page as the verified decoder decodes them.
/// Skipped without the game's files.
/// </summary>
public sealed class LibraryPicturesTests : IDisposable
{
    private readonly GameFolder _game = FindGame();
    private readonly List<GameFolderFiles> _files = [];

    public void Dispose() => _files.ForEach(files => files.Dispose());

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

    private (LibraryPictures Pictures, McgaGraphicsDriver Graphics) Setup()
    {
        Skip.If(_game.Find("FONTS.CV") is null, "no game files: set COVERT_GAME_DIR to a folder with the original game");
        var graphics = new McgaGraphicsDriver(new FontSet(_game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]);
        var link = new HostLink(graphics, new HeadlessHost([]));
        _files.Add(new GameFolderFiles(_game));
        var world = new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), _files[^1]);
        return (new LibraryPictures(world), graphics);
    }

    /// <summary>What a picture must look like on an empty page, its corner in the page's corner.</summary>
    private static byte[] OnAPage(PictureFile picture)
    {
        var page = new byte[McgaGraphicsDriver.Width * McgaGraphicsDriver.Height];
        for (var row = 0; row < Math.Min(picture.Height, McgaGraphicsDriver.Height); row++)
        {
            picture.Pixels.AsSpan(row * picture.Width, picture.Width).CopyTo(page.AsSpan(row * McgaGraphicsDriver.Width));
        }
        return page;
    }

    [SkippableFact]
    public void EveryPictureFileOfTheGameLoadsOntoAPage()
    {
        var (pictures, graphics) = Setup();
        var loaded = 0;
        foreach (var file in Directory.EnumerateFiles(_game.Path).Where(f => f.EndsWith(".PIC", StringComparison.OrdinalIgnoreCase)).Order())
        {
            var name = Path.GetFileName(file);
            var picture = PictureFile.Read(File.ReadAllBytes(file));
            if (picture.Width > McgaGraphicsDriver.Width || picture.Height > McgaGraphicsDriver.Height || picture.Width % 2 != 0)
            {
                continue;   // (none of the game's is larger than a page or of odd width; a made-up one would be)
            }
            graphics.FillPage(1, 0);
            pictures.LoadPictureSwapped(name, 1);
            Assert.True(OnAPage(picture).AsSpan().SequenceEqual(graphics.Pages[1]), $"{name} is not on the page as decoded");
            loaded++;
        }
        Assert.True(loaded >= 20, $"only {loaded} pictures loaded");
        Assert.DoesNotContain(graphics.Pages[0], pixel => pixel != 0);     // the screen was not touched
    }

    [SkippableFact]
    public void APictureIsTakenFromTheOpenArchiveBeforeAFileOfItsOwn()
    {
        var (pictures, graphics) = Setup();
        var archiveFile = Directory.EnumerateFiles(_game.Path).Where(f => f.EndsWith(".CAT", StringComparison.OrdinalIgnoreCase)).Order().First();
        var archive = new CatArchive(File.ReadAllBytes(archiveFile));
        Assert.True(pictures.OpenArchive(Path.GetFileName(archiveFile)));
        foreach (var entry in archive.Entries.Take(5).Append(archive.Entries[^1]))
        {
            graphics.FillPage(2, 0);
            pictures.LoadPicture(2, entry.Name.ToLowerInvariant());     // names are found whatever their letter case
            Assert.True(OnAPage(PictureFile.Read(archive.Contents(entry))).AsSpan().SequenceEqual(graphics.Pages[2]), $"{entry.Name} of the archive is not on the page as decoded");
        }
        // a picture the archive does not have comes from its own file, and the archive stays open
        var own = Path.GetFileName(Directory.EnumerateFiles(_game.Path).Where(f => f.EndsWith(".PIC", StringComparison.OrdinalIgnoreCase)).Order().First());
        graphics.FillPage(2, 0);
        pictures.LoadPicture(2, own);
        Assert.True(OnAPage(PictureFile.Read(_game.Read(own))).AsSpan().SequenceEqual(graphics.Pages[2]));
        Assert.NotEqual(-1, pictures.ArchiveHandle);
    }

    /// <summary>
    /// What load_faces_pic cuts from a sheet and where it puts it, worked out from the original's C
    /// (FINAL 0100:7e77): the slot of the table, then the left edge, top edge, width and height on the sheet.
    /// </summary>
    private static IEnumerable<(int Slot, int X, int Y, int Width, int Height)> CutsOf(bool men)
    {
        for (var i = 0; i < 40; i++)
        {
            yield return ((men ? 0x28 : 0) + i, i % 8 * 32 + 1, i / 8 * 35 + 1, 30, 33);
        }
        for (var i = 0; i < 4; i++)
        {
            yield return ((men ? 0x68 : 0x70) + i, 257, 20 * i, 44, 18);
        }
        if (men)
        {
            for (var i = 0; i < 7; i++)
            {
                yield return (0x60 + i, 96 + 32 * i, 174, 32, 26);
            }
            yield return (0x6c, 73, 175, 21, 22);
            yield return (0x6d, 307, 0, 11, 8);
            yield return (0x6e, 307, 9, 12, 8);
            yield return (0x6f, 257, 160, 26, 14);
        }
    }

    private static void AssertSameImage(CovertAction.Core.Graphics.Image? expected, CovertAction.Core.Graphics.Image? actual, string what)
    {
        Assert.True(expected is not null && actual is not null, $"{what}: no image");
        Assert.True((expected.Width, expected.Height, expected.Rows.Count) == (actual.Width, actual.Height, actual.Rows.Count), $"{what}: another size");
        for (var row = 0; row < expected.Rows.Count; row++)
        {
            Assert.True(expected.Rows[row].Skip == actual.Rows[row].Skip && expected.Rows[row].Pixels.AsSpan().SequenceEqual(actual.Rows[row].Pixels), $"{what}: row {row} differs");
        }
    }

    [SkippableFact]
    public void TheSheetsOfFacesAreCutIntoTheirPlacesOfTheTable()
    {
        var (pictures, graphics) = Setup();
        var sprite = new CovertAction.Core.Graphics.Image?[0x78];
        pictures.LoadFacesPic(sprite, "facesf.pic", "faces.pic");

        var cut = new HashSet<int>();
        foreach (var (sheet, men) in new[] { ("facesf.pic", false), ("faces.pic", true) })
        {
            graphics.FillPage(2, 0);
            pictures.LoadPicture(2, sheet);
            foreach (var (slot, x, y, width, height) in CutsOf(men))
            {
                AssertSameImage(graphics.CutImage(2, x, y, width, height), sprite[slot], $"image 0x{slot:x} of {sheet}");
                cut.Add(slot);
            }
        }
        // 99 images; the rest of the table is not this routine's
        Assert.Equal(99, cut.Count);
        Assert.All(Enumerable.Range(0, sprite.Length).Where(slot => !cut.Contains(slot)), slot => Assert.Null(sprite[slot]));
        // the two sheets differ, so a part put into the other sheet's place would show
        Assert.False(sprite[0]!.Rows.SelectMany(r => r.Pixels).SequenceEqual(sprite[0x28]!.Rows.SelectMany(r => r.Pixels)));
        Assert.False(sprite[0x70]!.Rows.SelectMany(r => r.Pixels).SequenceEqual(sprite[0x68]!.Rows.SelectMany(r => r.Pixels)));
    }

    [SkippableFact]
    public void ThePartsPicturesAreTheFacesAndSixteenSmallPicturesFromTheMensSheet()
    {
        var (pictures, graphics) = Setup();
        var state = new CovertAction.Core.Electronics.ElectronicsState();
        state.Animation.BackdropCode = 7;
        var sprite = new CovertAction.Core.Graphics.Image?[0x78];
        pictures.LoadPictures(state, sprite, "facesf.pic", "faces.pic");

        // the men's sheet is still on the page: the small pictures as the C cuts them (FINAL 0100:7a8e)
        for (var i = 0; i < 16; i++)
        {
            AssertSameImage(graphics.CutImage(2, i % 8 * 9, i / 8 * 9 + 0xaf, 8, 8), sprite[0x50 + i], $"small picture {i}");
        }
        Assert.NotNull(sprite[0]);
        Assert.NotNull(sprite[0x6f]);
        Assert.Equal(-1, state.Animation.BackdropCode);
    }

    [SkippableFact]
    public void AMissingFileEndsTheProgramAndAMissingArchiveIsReported()
    {
        var (pictures, _) = Setup();
        Assert.False(pictures.OpenArchive("NOSUCH.CAT"));
        Assert.Throws<ProgramExitException>(() => pictures.LoadPicture(1, "NOSUCH.PIC"));
    }
}
