using CovertAction.Host.Sdl;
using CovertAction.Settings.Drawing;
using CovertAction.Tests.Shared;
using Item = CovertAction.Settings.LauncherScreen.Item;
using Part = CovertAction.Settings.LauncherScreen.Part;

namespace CovertAction.Settings.Tests;

/// <summary>
/// The launcher: what it shows and changes, that what it saves reads back as it was chosen, and its layout (nothing
/// overlaps, everything stays in its panel), as the Darklands Restoration Project's launcher tests check its own.
/// </summary>
public sealed class LauncherTests
{
    private const string Game = "C:/Game";

    /// <summary>A launcher's context with nothing on disk: the game in <see cref="Game"/> (only there), no ROMs, a fixed saves folder.</summary>
    private static LauncherContext Context(GameSettings? settings = null, string[]? args = null, Func<string, string?>? environment = null,
        LauncherReason reason = LauncherReason.Asked, string? problem = null, string notes = Notes, ISoundTest? test = null, bool roms = false)
    {
        settings ??= new GameSettings();
        var start = StartSettings.Resolve(CommandLine.Parse(args ?? []), environment ?? (_ => null), settings);
        return new LauncherContext(settings, start, "0.1.0", reason, problem, LauncherHost.FontBytes(),
            folder => folder.Replace('\\', '/').TrimEnd('/') == Game ? Game : null,
            () => [],
            folder => new RomStatus(folder ?? "C:/Program/MT32_ROMS", roms, false),
            "C:/Users/player/AppData/Local/CovertAction/Saves",
            test,
            notes,
            "C:/Program/covertaction.settings.json");
    }

    private const string Notes = """
        # Covert Action 0.1.0
        - **The game from the set-up screen on.** The set-up, the menus, the campaign.
        - **A launcher for the settings.** CovertAction Launcher.exe opens it.
        ## Getting started
        - not shown
        """;

    private sealed class Surface : ILauncherSurface
    {
        public (int Width, int Height)? DisplaySize => (1920, 1080);

        public void Present(ReadOnlySpan<uint> pixels, int width, int height)
        {
        }

        public IReadOnlyList<LauncherInput> Wait(TimeSpan? timeout) => [new CloseRequested()];

        public string? Clipboard { get; set; }

        string? ILauncherSurface.Clipboard() => Clipboard;

        public string? Picked { get; set; }

        public Func<string, string, string?>? PickFolder => (_, _) => Picked;
    }

    private sealed class Test : ISoundTest
    {
        public SoundTestRequest? Asked { get; private set; }

        public void Play(SoundTestRequest request) => (Asked, Playing) = (request, true);

        public void Stop() => Playing = false;

        public bool Playing { get; private set; }

        public string? Status => Playing ? "Playing the theme on the AdLib…" : null;
    }

    private static (LauncherScreen Screen, LauncherModel Model, Canvas Canvas) Open(LauncherContext context, Surface? surface = null)
    {
        surface ??= new Surface();
        var model = new LauncherModel(context, surface.DisplaySize);
        var screen = new LauncherScreen(model, context, surface);
        var canvas = new Canvas(LauncherScreen.DesignWidth, LauncherScreen.DesignHeight);
        screen.Render(canvas);
        return (screen, model, canvas);
    }

    private static LauncherChoice? Click(LauncherScreen screen, Canvas canvas, Item item, Part part = Part.Row)
    {
        var area = screen.Area(item, part);
        var (x, y) = (area.X + area.Width / 2, area.Y + area.Height / 2);
        var choice = screen.Handle(new PointerPressed(x, y)) ?? screen.Handle(new PointerReleased(x, y));
        screen.Render(canvas);
        return choice;
    }

    [Fact]
    public void WithoutTheGameItSaysSoAndPlayAsksForTheFolder()
    {
        var (screen, model, canvas) = Open(Context(reason: LauncherReason.GameNotFound));
        Assert.False(model.CanPlay);
        Assert.Equal(Item.GameFolder, screen.Focus);
        Assert.Contains("was not found", screen.Message, StringComparison.Ordinal);
        Assert.Null(Click(screen, canvas, Item.Play));
        Assert.Contains("Choose the folder", screen.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeTakesTheFolderFromTheDialogAndPlayThenSavesItWithTheOtherChanges()
    {
        var surface = new Surface { Picked = "C:\\Game" };
        var (screen, model, canvas) = Open(Context(), surface);
        Click(screen, canvas, Item.GameChange);
        Assert.True(model.CanPlay);
        Click(screen, canvas, Item.Window, Part.Next);
        Click(screen, canvas, Item.Fullscreen);
        Click(screen, canvas, Item.Scaling, Part.Previous);
        Click(screen, canvas, Item.Sound, Part.Next);
        Assert.Equal("mt32", model.SoundCard);
        Click(screen, canvas, Item.Controls);
        Assert.Equal("keyboard", model.Controls);
        Assert.Equal(LauncherChoice.Play, Click(screen, canvas, Item.Play));

        var changes = model.Changes(playing: true);
        Assert.Equal(["display.fullscreen", "display.resolution", "display.scale", "display.scaling", "gameFolder", "input.controls", "sound.card"], changes.Keys.Order());

        // saved into the template and read back, the run starts as the launcher showed it
        var saved = SettingsFile.Parse(SettingsFileEdits.Set(SettingsFile.Template, changes)).Settings;
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, saved);
        Assert.Equal(("C:/Game", 1280, 960, true, "smooth", "mt32", "keyboard"), (start.GameFolder, start.Width, start.Height, start.Fullscreen, start.Scaling, start.SoundCard, start.Controls));
        Assert.Equal(model.Settings().SoundCard, saved.SoundCard);
    }

    /// <summary>The controls row (the set-up's controls question of the original): keyboard and joystick by default, ‹ › go between the two answers.</summary>
    [Fact]
    public void TheControlsRowGoesBetweenTheJoystickAndTheKeyboardOnly()
    {
        var (screen, model, canvas) = Open(Context());
        Assert.True(screen.Shown(Item.Controls));
        Assert.Equal(("adlib", "joystick"), (model.SoundCard, model.Controls));
        Click(screen, canvas, Item.Controls, Part.Next);
        Assert.Equal("keyboard", model.Controls);
        Click(screen, canvas, Item.Controls, Part.Previous);
        Assert.Equal("joystick", model.Controls);
        Assert.Empty(model.Changes(playing: false));
    }

    [Fact]
    public void ARowTheCommandLineSetsIsShownButCannotBeChanged()
    {
        var (screen, model, _) = Open(Context(args: ["--sound", "adlib", "--scale", "2"]));
        Assert.Equal("adlib", model.SoundCard);
        // no ‹ › to click on a fixed row
        Assert.Throws<InvalidOperationException>(() => screen.Area(Item.Sound, Part.Next));
        screen.Handle(new KeyPressed(LauncherKey.Tab));
        while (screen.Focus != Item.Sound)
        {
            screen.Handle(new KeyPressed(LauncherKey.Down));
        }
        screen.Handle(new KeyPressed(LauncherKey.Right));
        Assert.Equal("adlib", model.SoundCard);
        Assert.Contains("--sound", screen.Message, StringComparison.Ordinal);
        Assert.Empty(model.Changes());
    }

    [Fact]
    public void TheKeyboardGoesThroughTheRowsIntoTheAdvancedTabAndEscapeLeaves()
    {
        var (screen, _, _) = Open(Context(settings: new GameSettings { GameFolder = Game }));
        Assert.Equal(Item.Play, screen.Focus);
        var seen = new List<Item>();
        for (var i = 0; i < 20; i++)
        {
            screen.Handle(new KeyPressed(LauncherKey.Tab));
            seen.Add(screen.Focus);
            if (screen.Focus == Item.KeyRepeat)
            {
                Assert.True(screen.AdvancedShown);
            }
        }
        Assert.Contains(Item.Roms, seen);
        Assert.Contains(Item.Saves, seen);
        Assert.Equal(LauncherChoice.Quit, screen.Handle(new KeyPressed(LauncherKey.Escape)));
    }

    [Fact]
    public void TestSoundPlaysTheChosenCardFromTheGameFolder()
    {
        var test = new Test();
        var (screen, model, canvas) = Open(Context(settings: new GameSettings { GameFolder = Game, SoundCard = "adlib" }, test: test));
        Assert.True(model.CanPlay);
        Click(screen, canvas, Item.Test);
        Assert.Equal(new SoundTestRequest("adlib", Game, null), test.Asked);
        Assert.True(screen.Animating);
        Click(screen, canvas, Item.Test);
        Assert.False(test.Playing);
    }

    [Fact]
    public void AFolderDroppedOnTheWindowGoesToTheRowItBelongsTo()
    {
        var (screen, model, _) = Open(Context(roms: true));
        screen.Handle(new PathDropped("C:/Game"));
        Assert.True(model.CanPlay);
        screen.Handle(new PathDropped("D:/Roms"));
        Assert.Equal("D:/Roms", model.Mt32Roms);
    }

    [Fact]
    public void TheMixOfTheSoundDriversIsOffByDefaultAndSavesAsChosenOnTheAdvancedTab()
    {
        var test = new Test();
        var (screen, model, canvas) = Open(Context(settings: new GameSettings { GameFolder = Game, SoundCard = "adlib" }, test: test));
        Assert.False(model.Mixing);
        Assert.False(screen.Shown(Item.Music));
        Click(screen, canvas, Item.TabAdvanced);
        Assert.True(screen.Shown(Item.Music) && screen.Shown(Item.Effects) && screen.Shown(Item.MixTest));
        Assert.DoesNotContain(screen.Blocks, b => b.What.Contains("mixing the sound drivers", StringComparison.Ordinal));

        // the music on the MT-32 (the list ends with Amiga, Tandy, then the MT-32 before it), the effects on the sound card (the AdLib): the test plays the two
        Click(screen, canvas, Item.Music, Part.Previous);
        Click(screen, canvas, Item.Music, Part.Previous);
        Click(screen, canvas, Item.Music, Part.Previous);
        Assert.Equal("mt32", model.MusicCard);
        Assert.True(model.Mixing);
        Click(screen, canvas, Item.MixTest);
        Assert.Equal(new SoundTestRequest("mt32", Game, null, "adlib"), test.Asked);
        Click(screen, canvas, Item.MixTest);
        Assert.False(test.Playing);
        Click(screen, canvas, Item.Effects, Part.Next);
        Click(screen, canvas, Item.Effects, Part.Next);
        Assert.Equal("speaker", model.EffectsCard);

        var changes = model.Changes();
        Assert.Equal(["sound.effects", "sound.music"], changes.Keys.Order());
        var saved = SettingsFile.Parse(SettingsFileEdits.Set(SettingsFile.Template, changes)).Settings;
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, saved);
        Assert.Equal(("mt32", "speaker"), (start.MusicCard, start.EffectsCard));
        Assert.Equal(('R', 'I'), (GameSettings.PartLetter(start.MusicCard), GameSettings.PartLetter(start.EffectsCard)));
        // the defaults: both parts on the sound card, which is no letter of its own
        var defaults = StartSettings.Resolve(CommandLine.Parse([]), _ => null, SettingsFile.Parse(SettingsFile.Template).Settings);
        Assert.Equal(("card", "card"), (defaults.MusicCard, defaults.EffectsCard));
        Assert.Null(GameSettings.PartLetter(defaults.MusicCard));
    }

    // ---- layout ----

    public static TheoryData<string> States => ["not found", "found", "mt32 without ROMs", "start failed", "advanced", "fixed", "many notes", "no notes", "hover"];

    private static (LauncherScreen Screen, Canvas Canvas) Draw(string state)
    {
        var longPath = "C:/" + string.Join("/", Enumerable.Repeat("a very long folder name", 8));
        var context = state switch
        {
            "not found" => Context(reason: LauncherReason.GameNotFound, settings: new GameSettings { GameFolder = longPath }),
            "found" => Context(settings: new GameSettings { GameFolder = Game }, test: new Test()),
            "mt32 without ROMs" => Context(settings: new GameSettings { GameFolder = Game, SoundCard = "mt32", Mt32Roms = longPath, SavesFolder = longPath }, test: new Test()),
            "start failed" => Context(reason: LauncherReason.StartFailed, problem: "the game files could not be read from " + longPath + ": " + string.Concat(Enumerable.Repeat("FONTS.CV is not a font file. ", 12))),
            "advanced" => Context(settings: new GameSettings { GameFolder = Game, Mt32Roms = longPath }),
            "fixed" => Context(args: [Game, "--scale", "5", "--sound", "tandy", "--controls", "keyboard", "--saves", longPath, "--mt32-roms", longPath, "--key-repeat", "windows"]),
            "many notes" => Context(notes: string.Concat(Enumerable.Range(1, 30).Select(i => $"- **Item {i} of a long list of changes, with a title that runs over a line.** And its detail, which runs on for a while too.\n"))),
            "no notes" => Context(notes: ""),
            _ => Context(settings: new GameSettings { GameFolder = Game, SoundCard = "adlib" }),
        };
        var (screen, _, canvas) = Open(context);
        if (state is "advanced")
        {
            screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        }
        if (state is "hover")
        {
            var area = screen.Area(Item.GameFolder);
            screen.Handle(new PointerMoved(area.X + 2, area.Y + 2));
        }
        screen.Render(canvas);
        return (screen, canvas);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void NothingOfTheSettingsOverlapsAndEverythingStaysInItsPanel(string state)
    {
        var (screen, _) = Draw(state);
        var panel = LauncherScreen.SettingsPanel;
        Assert.All(screen.Blocks, b => Assert.True(panel.Holds(b.Area), $"{b.What} at {b.Area} is outside the panel {panel}"));
        var blocks = screen.Blocks.ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            for (var j = i + 1; j < blocks.Count; j++)
            {
                Assert.False(blocks[i].Area.Intersects(blocks[j].Area), $"{blocks[i].What} {blocks[i].Area} overlaps {blocks[j].What} {blocks[j].Area}");
            }
        }
        // the details box shows what it has, cut to fit
        Assert.True(screen.DetailsShown.Split('\n').All(l => BitmapFont.Measure(l) <= panel.Width), screen.DetailsShown);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void NothingOfTheCardOverlapsAndEverythingStaysOnIt(string state)
    {
        var (screen, _) = Draw(state);
        var card = LauncherScreen.CardFrame.Inset(2);
        Assert.NotEmpty(screen.CardBlocks);
        Assert.All(screen.CardBlocks, b => Assert.True(card.Holds(b.Area), $"{b.What} at {b.Area} is off the card {card}"));
        var blocks = screen.CardBlocks.ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            for (var j = i + 1; j < blocks.Count; j++)
            {
                Assert.False(blocks[i].Area.Intersects(blocks[j].Area), $"{blocks[i].What} overlaps {blocks[j].What}");
            }
        }
        Assert.DoesNotContain(screen.CardBlocks, b => b.Area.Intersects(LauncherScreen.PanelFrame));
    }

    [Fact]
    public void TheCardTellsWhatIsNewAndTheSmallPrint()
    {
        var (screen, _) = Draw("found");
        Assert.Contains(screen.CardBlocks, b => b.What == "note The game from the set-up screen on");
        Assert.Contains(screen.CardBlocks, b => b.What == "note A launcher for the settings");
        Assert.DoesNotContain(screen.CardBlocks, b => b.What == "note not shown");
        Assert.Contains(screen.CardBlocks, b => b.What == "small print");
    }

    [Fact]
    public void ThePointerIsMappedIntoTheFittedPicture()
    {
        // a window twice the design's size, and one with bars at its sides (high DPI: twice the pixels of its coordinates)
        Assert.Equal((640, 360), SdlLauncherWindow.ToDesign(1280, 720, 2560, 1440, 2560, 1440, 1280, 720));
        Assert.Equal((0, 0), SdlLauncherWindow.ToDesign(160, 0, 1600, 720, 3200, 1440, 1280, 720));
        Assert.Equal((320, 0, 1280, 720), SdlLauncherWindow.Fit(1920, 720, 1280, 720));
    }

    /// <summary>
    /// The launcher's pictures for the eye (build/launcher): as the program draws it from the player's settings (the game
    /// from COVERT_GAME_DIR when set, behind it a picture of the player's own game), on both tabs, and without the game.
    /// </summary>
    [Fact]
    public void ThePicturesOfTheLauncherAreWrittenForTheEye()
    {
        var folder = Path.Combine(Repository.Root, "build", "launcher");
        Directory.CreateDirectory(folder);
        var notes = File.ReadAllText(Path.Combine(Repository.CSharp, "releases", "0.1.0", "release-notes.md"));
        var settings = new GameSettings();
        var start = StartSettings.Resolve(CommandLine.Parse([]), System.Environment.GetEnvironmentVariable, settings);
        var real = LauncherHost.Context(LauncherReason.Asked, null, settings, start, "C:/Games/CovertAction/covertaction.settings.json", new Test()) with { ReleaseNotes = notes, Version = "0.1.0" };
        foreach (var (name, context, advanced) in new[]
        {
            ("launcher-general", real, false),
            ("launcher-advanced", real, true),
            ("launcher-not-found", Context(reason: LauncherReason.GameNotFound, notes: notes), false),
        })
        {
            var canvas = Launcher.Picture(context, (1920, 1080), advanced);
            var path = Path.Combine(folder, name + ".png");
            Png.Write(path, canvas.Pixels, canvas.Width, canvas.Height);
            Assert.Equal(canvas.Pixels.Length, Png.Read(File.ReadAllBytes(path)) is var image ? image.Width * image.Height : 0);
        }
    }
}
