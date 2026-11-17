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

    /// <summary>The game's captured dossier script as the cut-content option loads it for a woman (D-660): its first instruction pushes 1 instead of -1.</summary>
    private static byte[] WomanProgram(byte[] program)
    {
        var woman = (byte[])program.Clone();
        Assert.Equal(new byte[] { 0x05, 0x00, 0xff, 0xff, 0x06, 0x00, 0x00 }, woman.AsSpan(0, 7).ToArray());
        (woman[2], woman[3]) = (1, 0);
        return woman;
    }

    [SkippableFact]
    public void TheCapturedDossierPlaysTheAmigasProgramWithTheSwitchForTheWomansPhotograph()
    {
        var folder = AmigaFolder();
        var game = FindGame();
        var own = PanFile.Read(game.Read("CAPTRD01.PAN"));
        var replacement = Source(new PictureColours(new byte[3 * 256]), folder).Find("captrd01.pan", own);
        Assert.NotNull(replacement);
        // the Amiga page has the PC's images and two more (the photographs 5 and 7: a stand-in face each)
        Assert.All(Enumerable.Range(0, PanFile.ImagePlaces).Where(i => own.Images[i] is not null), i => Assert.NotNull(replacement.Images[i]));
        Assert.All(new[] { 5, 7 }, i => Assert.NotNull(replacement.Images[i]));
        Assert.Null(own.Images[5]);
        // its script does not set the variable itself: with the variable the game sets the man's page and the woman's differ, with none neither photograph is drawn
        byte[][] pages = [Page(replacement, -1), Page(replacement, 1), Page(replacement, null)];
        Assert.False(pages[0].SequenceEqual(pages[1]), "the woman's photographs differ from the man's");
        Assert.False(pages[0].SequenceEqual(pages[2]));
        Assert.False(pages[1].SequenceEqual(pages[2]));
        // the game's own script begins by setting the variable (push -1, store in variable 0); the Amiga's does not
        Assert.True(own.Program.AsSpan(0, 7).SequenceEqual(new byte[] { 0x05, 0x00, 0xff, 0xff, 0x06, 0x00, 0x00 }));
        Assert.False(replacement.Program.AsSpan(0, 7).SequenceEqual(own.Program.AsSpan(0, 7)));
    }

    private static byte[] Page(PanFile file, int? variable)
    {
        var player = new PanPlayer(file);
        if (variable is { } value)
        {
            player.SetVariable(0, value);
        }
        for (var tick = 0; tick < 60; tick++)
        {
            Assert.True(player.Tick());
        }
        return (byte[])player.Frame.Clone();
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
            // the captured dossier is shown for a man (the game sets variable 0 to -1) and for a woman (1); the game's own script
            // sets the variable itself to the man's, and the woman's is what the option makes of it (the first push is 1)
            (string Name, int? Variable)[] variants = entry.AmigaProgram ? [("-man", -1), ("-woman", 1)] : [("", null)];
            foreach (var (variantName, variable) in variants)
            {
                var pcFile = variable == 1 ? own.With(null, 0, own.Images, WomanProgram(own.Program)) : own;
                var players = new[] { (Player: new PanPlayer(pcFile), Suffix: "-pc", Pc: true), (Player: new PanPlayer(replacement), Suffix: "", Pc: false) };
                if (variable is { } value)
                {
                    players[1].Player.SetVariable(0, value);
                }
                var total = 0;
                for (var player = new PanPlayer(pcFile); total < MostTicks && player.Tick(); total++)
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
                        File.WriteAllBytes(Path.Combine(screens!, $"anim-{entry.Name[..^4]}{variantName}-{frame:D3}{suffix}.png"), PngFile.Screen(screen, palette));
                    }
                }
            }
        }
    }
}
