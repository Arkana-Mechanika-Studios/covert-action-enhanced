using CovertAction.Host.Sdl;
using CovertAction.Settings.Drawing;
using Item = CovertAction.Settings.LauncherScreen.Item;
using Part = CovertAction.Settings.LauncherScreen.Part;

namespace CovertAction.Settings.Tests;

/// <summary>
/// The two bug-fix options (docs/original-bugs.md; DIVERGENCES.md D-650): the settings keys fixes.originalBugs and
/// fixes.campaignBugs, their rows on the launcher's Advanced tab, and that both are off by default.
/// </summary>
public sealed class FixBugsSettingTests
{
    private const string Game = "C:/Game";

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
    public void BothOptionsAreOffByDefaultAndTheTemplateAsksForNothing()
    {
        foreach (var settings in new[] { new GameSettings(), SettingsFile.Parse(SettingsFile.Template).Settings })
        {
            var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, settings);
            Assert.False(start.FixOriginalBugs);
            Assert.False(start.FixCampaignBugs);
        }
        Assert.Empty(SettingsFile.Parse(SettingsFile.Template).Problems);
    }

    [Fact]
    public void TheKeysAreReadFromTheFileAndAWrongValueIsReported()
    {
        var file = SettingsFile.Parse("""{ "fixes": { "originalBugs": true, "campaignBugs": true } }""");
        Assert.Empty(file.Problems);
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, file.Settings);
        Assert.True(start.FixOriginalBugs);
        Assert.True(start.FixCampaignBugs);
        Assert.NotEmpty(SettingsFile.Parse("""{ "fixes": { "originalBugs": "yes" } }""").Problems);
        Assert.NotEmpty(SettingsFile.Parse("""{ "fixes": { "everything": true } }""").Problems);
    }

    [Fact]
    public void TheLauncherOffersBothRowsOnTheAdvancedTabAndSavesTheChanges()
    {
        var (screen, model, canvas) = Open(Context());
        Assert.False(screen.Shown(Item.FixOriginalBugs));
        Click(screen, canvas, Item.TabAdvanced);
        Assert.True(screen.Shown(Item.FixOriginalBugs));
        Assert.True(screen.Shown(Item.FixCampaignBugs));
        Assert.False(model.FixOriginalBugs);
        Assert.False(model.FixCampaignBugs);

        Click(screen, canvas, Item.FixOriginalBugs);
        Click(screen, canvas, Item.FixCampaignBugs);
        Assert.True(model.FixOriginalBugs);
        Assert.True(model.FixCampaignBugs);

        var changes = model.Changes();
        Assert.Equal(["fixes.campaignBugs", "fixes.originalBugs"], changes.Keys.Order());
        var saved = SettingsFile.Parse(SettingsFileEdits.Set(SettingsFile.Template, changes)).Settings;
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, saved);
        Assert.True(start.FixOriginalBugs);
        Assert.True(start.FixCampaignBugs);

        // switched off again, the changes are gone
        Click(screen, canvas, Item.FixOriginalBugs);
        Click(screen, canvas, Item.FixCampaignBugs);
        Assert.DoesNotContain(model.Changes().Keys, key => key.StartsWith("fixes.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRowsHaveHelpAndStayInsideThePanel()
    {
        var (screen, model, canvas) = Open(Context(new GameSettings { GameFolder = Game, FixOriginalBugs = true }));
        Click(screen, canvas, Item.TabAdvanced);
        Assert.True(model.FixOriginalBugs);
        var area = screen.Area(Item.FixOriginalBugs);
        screen.Handle(new PointerMoved(area.X + 2, area.Y + 2));
        screen.Render(canvas);
        Assert.Contains("bugs", screen.DetailsShown, StringComparison.Ordinal);
        var panel = LauncherScreen.SettingsPanel;
        Assert.All(screen.Blocks, b => Assert.True(panel.Holds(b.Area), $"{b.What} at {b.Area} is outside the panel {panel}"));
    }
}
