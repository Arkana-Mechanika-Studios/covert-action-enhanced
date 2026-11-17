using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Tests;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The Amiga animations (D-683, D-684) with the player's own Amiga files: every animation file read whole (skipped without
/// COVERT_AMIGA_DIR, the folder with the Amiga disks' folders) and every animation of the list checked against the game's twin
/// of it (also skipped without the game's files). Nothing of an Amiga file is kept in the repository.
/// </summary>
public sealed class AmigaAnimationsFilesTests
{
    private const string FolderVariable = "COVERT_AMIGA_DIR";

    /// <summary>The most ticks played when comparing two animations (a few play for ever).</summary>
    private const int MostTicks = 800;

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

    private static AmigaPictureSource Source(PictureColours colours, string folder) =>
        new(colours, name => AmigaFolderSearch.BankFile(AmigaFolderSearch.ResolvePictures(folder) ?? folder, name) is { } path ? File.ReadAllBytes(path) : null, message => throw new InvalidOperationException(message));

    [SkippableFact]
    public void EveryAnimationFileOfTheDisksIsRead()
    {
        var files = Directory.EnumerateFiles(AmigaFolder(), "*.*", SearchOption.AllDirectories).Where(file => file.EndsWith(".pan", StringComparison.OrdinalIgnoreCase)).Order().ToList();
        Skip.If(files.Count == 0, "no .pan files in the Amiga folder");
        foreach (var file in files)
        {
            var pan = AmigaPanFile.Read(File.ReadAllBytes(file));
            Assert.Equal(PanFile.ImagePlaces, pan.Images.Length);
            Assert.Contains(pan.BackgroundKind, new[] { 1, 2 });
            // the program is in the machine's form and its translation into the PC's succeeds for all of them
            Assert.NotEmpty(AmigaPanProgram.ToPcProgram(pan.Program));
            Assert.All(pan.Images.OfType<AmigaPanImage>(), image => Assert.All(image.Pixels, pixel => Assert.True(pixel < 32, file)));
        }
    }

    [SkippableFact]
    public void EveryAnimationOfTheListIsTheGamesOwnWithTheAmigasPixelsAndPlaysAsLongAndAsAudiblyAsTheGames()
    {
        var folder = AmigaFolder();
        var game = FindGame();
        foreach (var entry in AmigaAnimationList.Entries.Where(entry => !entry.AmigaProgram))
        {
            var own = PanFile.Read(game.Read(entry.Name.ToUpperInvariant()));
            var colours = new PictureColours(new byte[3 * 256]);
            var replacement = Source(colours, folder).Find(entry.Name, own);
            Assert.True(replacement is not null, entry.Name);
            // what the game reads of the file is the file's: the same header, palette, place and pace, the same program
            Assert.Equal((own.Width, own.Height, own.Delay, own.FrameLength, own.X, own.Y), (replacement.Width, replacement.Height, replacement.Delay, replacement.FrameLength, replacement.X, replacement.Y));
            Assert.Equal(own.PaletteBlock, replacement.PaletteBlock);
            Assert.Equal(own.Program, replacement.Program);
            for (var i = 0; i < PanFile.ImagePlaces; i++)
            {
                Assert.Equal(own.Images[i] is null, replacement.Images[i] is null);
                if (replacement.Images[i] is { } image)
                {
                    Assert.All(image.Pixels, pixel => Assert.True(pixel == 0 || pixel >= PictureColours.First, entry.Name));
                }
            }
            // played, the two have the same length and ask for the same sounds in the same frames
            var pc = new PanPlayer(own);
            var amiga = new PanPlayer(replacement);
            for (var tick = 0; tick < MostTicks; tick++)
            {
                var pcFrame = pc.Tick();
                var amigaFrame = amiga.Tick();
                Assert.True(pcFrame == amigaFrame, $"{entry.Name}: frame {tick}");
                Assert.True(pc.SoundRequests == amiga.SoundRequests, $"{entry.Name}: sounds at frame {tick}");
                if (!pcFrame)
                {
                    break;
                }
            }
        }
    }

    [SkippableFact]
    public void AnAmigaAnimationOfAnotherSizeThanTheGamesIsNotPlayed()
    {
        var folder = AmigaFolder();
        var game = FindGame();
        // the Amiga's office and headquarters scenes are not on the list; asked by a name of the list with the wrong game file they would be refused
        var own = PanFile.Read(game.Read("HQ.PAN"));
        var told = new List<string>();
        var source = new AmigaPictureSource(new PictureColours(new byte[3 * 256]), name => AmigaFolderSearch.BankFile(AmigaFolderSearch.ResolvePictures(folder) ?? folder, name == "injail.pan" ? "hq.pan" : name) is { } path ? File.ReadAllBytes(path) : null, told.Add);
        Assert.Null(source.Find("injail.pan", own));
        Assert.Single(told);
        Assert.Contains("not the same animation", told[0]);
    }

    /// <summary>Where the tool below writes the frames (outside the repository); unset: none are written.</summary>
    private const string ScreensVariable = "COVERT_AMIGA_SCREENS_DIR";

    /// <summary>
    /// A tool for looking at the list: a few frames of every animation of it, the game's own and the Amiga's, written as
    /// <c>anim-&lt;name&gt;-&lt;frame&gt;.png</c> and <c>anim-&lt;name&gt;-&lt;frame&gt;-pc.png</c> (skipped unless
    /// <c>COVERT_AMIGA_SCREENS_DIR</c> is set). The frame is put at the top left of an empty screen, the game's in the colours
    /// its own palette block gives, the Amiga's in its own display colours.
    /// </summary>
    [SkippableFact]
    public void FramesOfEveryAnimationAreWrittenAsPngFilesWhenAskedTo()
    {
        var screens = Environment.GetEnvironmentVariable(ScreensVariable);
        Skip.If(string.IsNullOrEmpty(screens), $"set {ScreensVariable} to write the frames as PNG files");
        var folder = AmigaFolder();
        var game = FindGame();
        Directory.CreateDirectory(screens!);
        var defaults = McgaDriverFile.Read(game.Read("MGRAPHIC.EXE")).DefaultColours;
        foreach (var entry in AmigaAnimationList.Entries)
        {
            var palette = new byte[3 * 256];
            defaults.AsSpan(0, Math.Min(defaults.Length, palette.Length)).CopyTo(palette);
            var colours = new PictureColours(palette);
            var source = Source(colours, folder);
            var own = PanFile.Read(game.Read(entry.Name.ToUpperInvariant()));
            var replacement = source.Find(entry.Name, own);
            Assert.NotNull(replacement);
            var lengths = new List<int>();
            var players = new[] { (Player: new PanPlayer(own), Suffix: "-pc", Pc: true), (Player: new PanPlayer(replacement), Suffix: "", Pc: false) };
            var total = 0;
            for (var player = new PanPlayer(own); total < MostTicks && player.Tick(); total++)
            {
            }
            var frames = new[] { 0, 1, 3, 6, 10, 20, 40, 80, 160, 320, 640 }.Where(frame => frame < total).ToList();
            foreach (var (player, suffix, isPc) in players)
            {
                var tick = 0;
                var screen = new byte[320 * 200];
                foreach (var frame in frames)
                {
                    while (tick <= frame && player.Tick())
                    {
                        tick++;
                    }
                    Array.Clear(screen);
                    for (var y = 0; y < Math.Min(own.Height, 200); y++)
                    {
                        for (var x = 0; x < Math.Min(own.Width, 320); x++)
                        {
                            var pixel = player.Frame[y * own.Width + x];
                            screen[y * 320 + x] = isPc ? own.Palette[pixel] : pixel;
                        }
                    }
                    File.WriteAllBytes(Path.Combine(screens!, $"anim-{entry.Name[..^4]}-{frame:D3}{suffix}.png"), PngFile.Screen(screen, palette));
                }
            }
            lengths.Add(total);
        }
    }
}
