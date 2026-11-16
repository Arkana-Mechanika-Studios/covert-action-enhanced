using CovertAction.Host.Sdl;
using CovertAction.Settings.Drawing;
using Item = CovertAction.Settings.LauncherScreen.Item;
using Part = CovertAction.Settings.LauncherScreen.Part;

namespace CovertAction.Settings.Tests;

/// <summary>
/// The Amiga music option (D-640) and the Amiga sound effects option (D-641): the settings keys, what the launcher offers for
/// them and saves, and that without the player's choice nothing is asked of the game (the defaults are the original's).
/// </summary>
public sealed class AmigaMusicSettingTests
{
    private const string Game = "C:/Game";

    /// <summary>A launcher's context with nothing on disk; the Amiga files are where <paramref name="amiga"/> says (null: none).</summary>
    private static LauncherContext Context(GameSettings? settings = null, ISoundTest? test = null, Func<string?, string?, AmigaStatus>? amiga = null)
    {
        settings ??= new GameSettings { GameFolder = Game };
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, settings);
        return new LauncherContext(settings, start, "0.1.0", LauncherReason.Asked, null, LauncherHost.FontBytes(),
            folder => folder.Replace('\\', '/').TrimEnd('/') == Game ? Game : null,
            () => [],
            folder => new RomStatus(folder ?? "C:/Program/MT32_ROMS", false, false),
            "C:/Users/player/AppData/Local/CovertAction/Saves",
            test,
            "# Covert Action 0.1.0\n## Getting started\n",
            "C:/Program/covertaction.settings.json",
            DescribeAmiga: amiga);
    }

    /// <summary>The host's answer: the files are in D:/Amiga and nowhere else.</summary>
    private static AmigaStatus Files(string? chosen, string? game) => new(chosen ?? "C:/Program/amiga/files", chosen == "D:/Amiga", null,
        chosen == "D:/Amiga" ? [] : ["thememusic.dat", "finalmusic.dat", "spy6music.dat"], chosen == "D:/Amiga",
        chosen == "D:/Amiga" ? [] : ["gamesnd.dat", "tacsnd.dat", "codesnd.dat", "bugsnd.dat", "chasesnd.dat"]);

    private sealed class Surface : ILauncherSurface
    {
        public (int Width, int Height)? DisplaySize => (1920, 1080);

        public void Present(ReadOnlySpan<uint> pixels, int width, int height)
        {
        }

        public IReadOnlyList<LauncherInput> Wait(TimeSpan? timeout) => [new CloseRequested()];

        public string? Clipboard() => null;

        public string? Picked { get; set; }

        public Func<string, string, string?>? PickFolder => (_, _) => Picked;
    }

    private sealed class Test : ISoundTest
    {
        public SoundTestRequest? Asked { get; private set; }

        public void Play(SoundTestRequest request) => (Asked, Playing) = (request, true);

        public void Stop() => Playing = false;

        public bool Playing { get; private set; }

        public string? Status => Playing ? "Playing" : null;
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

    private static void Click(LauncherScreen screen, Canvas canvas, Item item, Part part = Part.Row)
    {
        var area = screen.Area(item, part);
        var (x, y) = (area.X + area.Width / 2, area.Y + area.Height / 2);
        _ = screen.Handle(new PointerPressed(x, y)) ?? screen.Handle(new PointerReleased(x, y));
        screen.Render(canvas);
    }

    [Fact]
    public void TheOptionIsOffByDefaultAndTheTemplateAsksForNothing()
    {
        foreach (var settings in new[] { new GameSettings(), SettingsFile.Parse(SettingsFile.Template).Settings })
        {
            var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, settings);
            Assert.Equal("card", start.MusicCard);
            Assert.Null(start.AmigaFiles);
            Assert.False(start.AmigaOriginalOnly);
            Assert.Null(GameSettings.PartLetter(start.MusicCard));
        }
    }

    [Fact]
    public void TheKeysAreReadFromTheFile()
    {
        var file = SettingsFile.Parse("{ \"sound\": { \"music\": \"amiga\", \"amigaFiles\": \"D:/Amiga\", \"amigaOriginalOnly\": true } }");
        Assert.Empty(file.Problems);
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, file.Settings);
        Assert.Equal(("amiga", "D:/Amiga", true), (start.MusicCard, start.AmigaFiles, start.AmigaOriginalOnly));
        Assert.Equal('G', GameSettings.PartLetter(start.MusicCard));
    }

    [Fact]
    public void TheEffectsMayBeTheAmigaToo()
    {
        var file = SettingsFile.Parse("{ \"sound\": { \"effects\": \"amiga\", \"amigaFiles\": \"D:/Amiga\" } }");
        Assert.Empty(file.Problems);
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, file.Settings);
        Assert.Equal(("card", "amiga", "D:/Amiga"), (start.MusicCard, start.EffectsCard, start.AmigaFiles));
        Assert.Equal('G', GameSettings.PartLetter(start.EffectsCard));
        Assert.Null(GameSettings.PartLetter(start.MusicCard));
        // anything else is still refused
        var other = SettingsFile.Parse("{ \"sound\": { \"effects\": \"amigaa\" } }");
        Assert.Single(other.Problems);
        Assert.Contains("sound.effects", other.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheEffectsChoiceEndsWithTheAmigaAndSavesAndTestsWithTheSharedSubSetting()
    {
        var test = new Test();
        var (screen, model, canvas) = Open(Context(test: test, amiga: Files), new Surface { Picked = "D:/Amiga" });
        Click(screen, canvas, Item.TabAdvanced);
        Assert.Equal("Amiga (your own Amiga disks)", LauncherModel.Label(LauncherModel.EffectsChoices, "amiga"));
        Click(screen, canvas, Item.Effects, Part.Previous);
        Assert.Equal("amiga", model.EffectsCard);
        Assert.True(model.AmigaEffects && !model.AmigaMusic && model.Mixing);
        // no files yet: the music's and the effects' status both say so
        Assert.False(model.Amiga!.EffectsFound);
        Click(screen, canvas, Item.AmigaChange);
        Assert.True(model.Amiga!.EffectsFound);
        Click(screen, canvas, Item.AmigaOriginalOnly);

        // the music stays on the sound card, the Amiga's effects take the card for what they lack
        Click(screen, canvas, Item.MixTest);
        Assert.Equal(new SoundTestRequest("adlib", Game, null, "amiga", "adlib", "D:/Amiga", true), test.Asked);

        var changes = model.Changes();
        Assert.Equal(["sound.amigaFiles", "sound.amigaOriginalOnly", "sound.effects"], changes.Keys.Order());
        var saved = SettingsFile.Parse(SettingsFileEdits.Set(SettingsFile.Template, changes)).Settings;
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, saved);
        Assert.Equal(("card", "amiga", "D:/Amiga", true), (start.MusicCard, start.EffectsCard, start.AmigaFiles, start.AmigaOriginalOnly));
    }

    [Fact]
    public void AFolderWithOnlyTheSoundBanksIsTakenAsTheAmigaFolderToo()
    {
        var (screen, model, _) = Open(Context(amiga: (chosen, _) => new AmigaStatus(chosen ?? "C:/Program/amiga/files", false, null, ["thememusic.dat"], chosen == "D:/Banks", [])));
        screen.Handle(new PathDropped("D:/Banks"));
        Assert.Equal("D:/Banks", model.AmigaFiles);
    }

    [Fact]
    public void TheMusicChoiceEndsWithTheAmigaAndSavesWithItsFolderAndSubSetting()
    {
        var test = new Test();
        var (screen, model, canvas) = Open(Context(test: test, amiga: Files), new Surface { Picked = "D:/Amiga" });
        Click(screen, canvas, Item.TabAdvanced);
        Assert.True(screen.Shown(Item.AmigaFiles) && screen.Shown(Item.AmigaChange) && screen.Shown(Item.AmigaOriginalOnly));
        Assert.Equal("The sound card", LauncherModel.Label(LauncherModel.MusicChoices, model.MusicCard));

        // Previous from "the sound card" wraps to the last choice
        Click(screen, canvas, Item.Music, Part.Previous);
        Assert.Equal("amiga", model.MusicCard);
        Assert.True(model.AmigaMusic && model.Mixing);
        // no files yet: the status says what is missing and what plays
        Assert.False(model.Amiga!.Found);

        Click(screen, canvas, Item.AmigaChange);
        Assert.Equal("D:/Amiga", model.AmigaFiles);
        Assert.True(model.Amiga!.Found);
        Click(screen, canvas, Item.AmigaOriginalOnly);
        Assert.True(model.AmigaOriginalOnly);

        Click(screen, canvas, Item.MixTest);
        Assert.Equal(new SoundTestRequest("amiga", Game, null, "adlib", "adlib", "D:/Amiga", true), test.Asked);

        var changes = model.Changes();
        Assert.Equal(["sound.amigaFiles", "sound.amigaOriginalOnly", "sound.music"], changes.Keys.Order());
        var saved = SettingsFile.Parse(SettingsFileEdits.Set(SettingsFile.Template, changes)).Settings;
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, saved);
        Assert.Equal(("amiga", "D:/Amiga", true), (start.MusicCard, start.AmigaFiles, start.AmigaOriginalOnly));
    }

    [Fact]
    public void ADroppedFolderWithTheFilesIsTakenAsTheAmigaFolder()
    {
        var (screen, model, _) = Open(Context(amiga: Files));
        screen.Handle(new PathDropped("D:/Amiga"));
        Assert.Equal("D:/Amiga", model.AmigaFiles);
    }

    [Fact]
    public void TheMusicTestOfOtherChoicesIsAsBefore()
    {
        var test = new Test();
        var (screen, model, canvas) = Open(Context(test: test, amiga: Files));
        Click(screen, canvas, Item.TabAdvanced);
        Click(screen, canvas, Item.Music, Part.Next);
        Assert.Equal("none", model.MusicCard);
        Click(screen, canvas, Item.Effects, Part.Next);
        Click(screen, canvas, Item.Effects, Part.Next);
        Click(screen, canvas, Item.MixTest);
        Assert.Equal(new SoundTestRequest("none", Game, null, "speaker"), test.Asked);
    }

    [Fact]
    public void TheAmigaRowsHaveRoomAndTheirHelpIsShown()
    {
        var settings = new GameSettings { GameFolder = Game, MusicCard = "amiga", AmigaFiles = "D:/a very long folder name/" + new string('x', 200) };
        var (screen, _, canvas) = Open(Context(amiga: Files, settings: settings));
        Click(screen, canvas, Item.TabAdvanced);
        foreach (var item in new[] { Item.Music, Item.Effects, Item.AmigaFiles, Item.AmigaOriginalOnly })
        {
            var area = screen.Area(item);
            screen.Handle(new PointerMoved(area.X + 2, area.Y + 2));
            screen.Render(canvas);
            Assert.True(screen.DetailsShown.Contains("Amiga", StringComparison.Ordinal), $"{item}: [{screen.DetailsShown}]");
        }
        var panel = LauncherScreen.SettingsPanel;
        Assert.All(screen.Blocks, b => Assert.True(panel.Holds(b.Area), $"{b.What} at {b.Area} is outside the panel {panel}"));
    }

    [Fact]
    public void WithoutTheHostTellingTheFilesTheLauncherStillDraws()
    {
        var (screen, model, _) = Open(Context());
        Assert.Null(model.Amiga);
        Assert.False(screen.Shown(Item.AmigaFiles));
    }
}
