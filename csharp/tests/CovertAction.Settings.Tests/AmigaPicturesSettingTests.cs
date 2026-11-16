using CovertAction.Host.Sdl;
using CovertAction.Settings.Drawing;
using Item = CovertAction.Settings.LauncherScreen.Item;
using Part = CovertAction.Settings.LauncherScreen.Part;

namespace CovertAction.Settings.Tests;

/// <summary>
/// The Amiga pictures option (D-642): the settings key graphics.amiga, what the launcher offers for it and saves, and that
/// without the player's choice nothing is asked of the game (the default is the original's pictures).
/// </summary>
public sealed class AmigaPicturesSettingTests
{
    private const string Game = "C:/Game";

    private static LauncherContext Context(GameSettings? settings = null, Func<string?, string?, AmigaStatus>? amiga = null)
    {
        settings ??= new GameSettings { GameFolder = Game };
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, settings);
        return new LauncherContext(settings, start, "0.1.0", LauncherReason.Asked, null, LauncherHost.FontBytes(),
            folder => folder.Replace('\\', '/').TrimEnd('/') == Game ? Game : null,
            () => [],
            folder => new RomStatus(folder ?? "C:/Program/MT32_ROMS", false, false),
            "C:/Users/player/AppData/Local/CovertAction/Saves",
            null,
            "# Covert Action 0.1.0\n## Getting started\n",
            "C:/Program/covertaction.settings.json",
            DescribeAmiga: amiga);
    }

    /// <summary>The host's answer: the files are in D:/Amiga (all 32 pictures; the sound files too) and nowhere else.</summary>
    private static AmigaStatus Files(string? chosen, string? game)
    {
        var there = chosen == "D:/Amiga";
        return new AmigaStatus(chosen ?? "C:/Program/amiga/files", there, null, there ? [] : ["thememusic.dat"], there, there ? [] : ["gamesnd.dat"],
            there, there ? [] : ["faces.pix", "icons.pix"], 32, there ? null : "beside the program");
    }

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
            Assert.False(StartSettings.Resolve(CommandLine.Parse([]), _ => null, settings).AmigaPictures);
        }
        Assert.Empty(SettingsFile.Parse(SettingsFile.Template).Problems);
    }

    [Fact]
    public void TheKeyIsReadFromTheFileAndAWrongValueIsReported()
    {
        var on = SettingsFile.Parse("""{ "graphics": { "amiga": true } }""");
        Assert.Empty(on.Problems);
        Assert.True(StartSettings.Resolve(CommandLine.Parse([]), _ => null, on.Settings).AmigaPictures);
        Assert.NotEmpty(SettingsFile.Parse("""{ "graphics": { "amiga": "yes" } }""").Problems);
        Assert.NotEmpty(SettingsFile.Parse("""{ "graphics": { "colour": true } }""").Problems);
    }

    [Fact]
    public void AFileWithoutTheGraphicsSectionTakesTheKeyWhenTheLauncherSavesIt()
    {
        var saved = SettingsFileEdits.Set("""{ "sound": { "card": "adlib" } }""", new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["graphics.amiga"] = true });
        var file = SettingsFile.Parse(saved);
        Assert.Empty(file.Problems);
        Assert.True(file.Settings.AmigaPictures);
        Assert.Equal("adlib", file.Settings.SoundCard);
    }

    [Fact]
    public void TheLauncherOffersTheRowOnTheAdvancedTabAndSavesItsChangeWithTheFolder()
    {
        var (screen, model, canvas) = Open(Context(amiga: Files), new Surface { Picked = "D:/Amiga" });
        Assert.False(screen.Shown(Item.AmigaPictures));
        Click(screen, canvas, Item.TabAdvanced);
        Assert.True(screen.Shown(Item.AmigaPictures));
        Assert.False(model.AmigaPictures);

        Click(screen, canvas, Item.AmigaPictures);
        Assert.True(model.AmigaPictures);
        Click(screen, canvas, Item.AmigaChange);
        Assert.True(model.Amiga!.PicturesFound);

        var changes = model.Changes();
        Assert.Equal(["graphics.amiga", "sound.amigaFiles"], changes.Keys.Order());
        var saved = SettingsFile.Parse(SettingsFileEdits.Set(SettingsFile.Template, changes)).Settings;
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, saved);
        Assert.True(start.AmigaPictures);
        Assert.Equal("D:/Amiga", start.AmigaFiles);

        // switched off again, the change is gone
        Click(screen, canvas, Item.AmigaPictures);
        Assert.DoesNotContain("graphics.amiga", model.Changes().Keys);
    }

    [Fact]
    public void TheRowSaysWhichPictureFilesAreThereAndWhatShowsWithoutThem()
    {
        var settings = new GameSettings { GameFolder = Game, AmigaPictures = true };
        var (screen, model, canvas) = Open(Context(settings, Files));
        Click(screen, canvas, Item.TabAdvanced);
        // the files are not in the default place: the status is the one without them, and the option is on
        Assert.True(model.AmigaPictures);
        Assert.False(model.Amiga!.PicturesFound);
        Assert.Contains("faces.pix", model.Amiga.PicturesMissing!);
        var area = screen.Area(Item.AmigaPictures);
        screen.Handle(new PointerMoved(area.X + 2, area.Y + 2));
        screen.Render(canvas);
        Assert.Contains("Amiga", screen.DetailsShown, StringComparison.Ordinal);
        Assert.Contains("D-642", screen.DetailsShown, StringComparison.Ordinal);
        var panel = LauncherScreen.SettingsPanel;
        Assert.All(screen.Blocks, b => Assert.True(panel.Holds(b.Area), $"{b.What} at {b.Area} is outside the panel {panel}"));
    }

    [Fact]
    public void WithoutTheHostTellingTheFilesTheLauncherStillDraws()
    {
        var (screen, _, canvas) = Open(Context());
        Click(screen, canvas, Item.TabAdvanced);
        Assert.True(screen.Shown(Item.AmigaPictures));
    }
}
