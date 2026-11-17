using CovertAction.Host.Sdl;
using CovertAction.Settings.Drawing;
using Item = CovertAction.Settings.LauncherScreen.Item;
using Part = CovertAction.Settings.LauncherScreen.Part;

namespace CovertAction.Settings.Tests;

/// <summary>
/// The cut-content options (docs/cut-content.md; DIVERGENCES.md D-660 onward): the settings keys cutContent.*, their rows on
/// the launcher's "Cut content" tab, and that all are off by default.
/// </summary>
public sealed class CutContentSettingTests
{
    private const string Game = "C:/Game";

    private static readonly (Item Row, string Key, Func<StartSettings, bool> Start, Func<LauncherModel, bool> Model)[] Options =
    [
        (Item.CutCapturedDossier, "capturedDossier", s => s.CutCapturedDossier, m => m.CutCapturedDossier),
        (Item.CutStandaloneChase, "standaloneChase", s => s.CutStandaloneChase, m => m.CutStandaloneChase),
        (Item.CutHardestCircuit, "hardestCircuit", s => s.CutHardestCircuit, m => m.CutHardestCircuit),
        (Item.CutFillerTexts, "fillerTexts", s => s.CutFillerTexts, m => m.CutFillerTexts),
        (Item.CutChaseGrounds, "chaseGrounds", s => s.CutChaseGrounds, m => m.CutChaseGrounds),
        (Item.CutHomeRooms, "homeRooms", s => s.CutHomeRooms, m => m.CutHomeRooms),
    ];

    private static LauncherContext Context(GameSettings? settings = null)
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
            "C:/Program/covertaction.settings.json");
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

    private static (LauncherScreen Screen, LauncherModel Model, Canvas Canvas) Open(LauncherContext context)
    {
        var surface = new Surface();
        var model = new LauncherModel(context, surface.DisplaySize);
        var screen = new LauncherScreen(model, context, surface);
        var canvas = new Canvas(LauncherScreen.DesignWidth, LauncherScreen.DesignHeight);
        screen.Render(canvas);
        return (screen, model, canvas);
    }

    private static void Click(LauncherScreen screen, Canvas canvas, Item item)
    {
        var area = screen.Area(item, Part.Row);
        var (x, y) = (area.X + area.Width / 2, area.Y + area.Height / 2);
        _ = screen.Handle(new PointerPressed(x, y)) ?? screen.Handle(new PointerReleased(x, y));
        screen.Render(canvas);
    }

    [Fact]
    public void AllOptionsAreOffByDefaultAndTheTemplateAsksForNothing()
    {
        foreach (var settings in new[] { new GameSettings(), SettingsFile.Parse(SettingsFile.Template).Settings })
        {
            var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, settings);
            Assert.All(Options, o => Assert.False(o.Start(start), o.Key));
        }
        Assert.Empty(SettingsFile.Parse(SettingsFile.Template).Problems);
    }

    [Fact]
    public void TheKeysAreReadFromTheFileAndAWrongValueIsReported()
    {
        foreach (var option in Options)
        {
            var file = SettingsFile.Parse($$"""{ "cutContent": { "{{option.Key}}": true } }""");
            Assert.Empty(file.Problems);
            Assert.True(option.Start(StartSettings.Resolve(CommandLine.Parse([]), _ => null, file.Settings)), option.Key);
            Assert.NotEmpty(SettingsFile.Parse($$"""{ "cutContent": { "{{option.Key}}": "yes" } }""").Problems);
        }
        Assert.NotEmpty(SettingsFile.Parse("""{ "cutContent": { "everything": true } }""").Problems);
    }

    [Fact]
    public void TheLauncherOffersTheRowsOnTheCutContentTabAndSavesTheChanges()
    {
        var (screen, model, canvas) = Open(Context());
        Assert.All(Options, o => Assert.False(screen.Shown(o.Row)));
        Click(screen, canvas, Item.TabCutContent);
        Assert.True(screen.CutContentShown);
        Assert.False(screen.AdvancedShown);
        Assert.All(Options, o =>
        {
            Assert.True(screen.Shown(o.Row), o.Key);
            Assert.False(o.Model(model), o.Key);
        });

        foreach (var option in Options)
        {
            Click(screen, canvas, option.Row);
            Assert.True(option.Model(model), option.Key);
        }
        var changes = model.Changes();
        Assert.Equal(Options.Select(o => "cutContent." + o.Key).Order(), changes.Keys.Order());
        var saved = SettingsFile.Parse(SettingsFileEdits.Set(SettingsFile.Template, changes)).Settings;
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, saved);
        Assert.All(Options, o => Assert.True(o.Start(start), o.Key));

        // switched off again, the changes are gone
        foreach (var option in Options)
        {
            Click(screen, canvas, option.Row);
        }
        Assert.DoesNotContain(model.Changes().Keys, key => key.StartsWith("cutContent.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTabsFollowEachOtherWithCtrlTabAndTheRowsHaveHelpAndStayInsideThePanel()
    {
        var (screen, model, canvas) = Open(Context(new GameSettings { GameFolder = Game, CutCapturedDossier = true }));
        Assert.True(model.CutCapturedDossier);
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        Assert.True(screen.AdvancedShown);
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        Assert.True(screen.CutContentShown);
        screen.Render(canvas);
        foreach (var option in Options)
        {
            var area = screen.Area(option.Row);
            screen.Handle(new PointerMoved(area.X + 2, area.Y + 2));
            screen.Render(canvas);
            Assert.Contains("original", screen.DetailsShown, StringComparison.Ordinal);
            var panel = LauncherScreen.SettingsPanel;
            Assert.All(screen.Blocks, b => Assert.True(panel.Holds(b.Area), $"{b.What} at {b.Area} is outside the panel {panel}"));
        }
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        Assert.True(screen.ControlsShown);
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        Assert.False(screen.ControlsShown);
        Assert.False(screen.CutContentShown);
        Assert.False(screen.AdvancedShown);
    }

    [Fact]
    public void TheScreenshotOptionPicksTheTabAndThePictureOfTheCutContentTabDiffersFromTheOthers()
    {
        Assert.Equal(0, CommandLine.Parse(["--launcher-screenshot", "a.png"]).LauncherScreenshotTab);
        Assert.Equal(2, CommandLine.Parse(["--launcher-screenshot", "a.png", "--launcher-screenshot-tab", "cut"]).LauncherScreenshotTab);
        Assert.Equal(1, CommandLine.Parse(["--launcher-screenshot-tab", "Advanced"]).LauncherScreenshotTab);
        Assert.NotNull(CommandLine.Parse(["--launcher-screenshot-tab", "other"]).Error);
        var context = Context();
        var general = Launcher.Picture(context, tab: 0).Pixels.ToArray();
        var advanced = Launcher.Picture(context, tab: 1).Pixels.ToArray();
        var cut = Launcher.Picture(context, tab: 2).Pixels.ToArray();
        Assert.NotEqual(general, cut);
        Assert.NotEqual(advanced, cut);
        Assert.Equal(advanced, Launcher.Picture(context, advanced: true).Pixels.ToArray());
    }
}
