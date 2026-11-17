using CovertAction.Host.Sdl;
using CovertAction.Platform;
using CovertAction.Settings.Drawing;
using Item = CovertAction.Settings.LauncherScreen.Item;
using Part = CovertAction.Settings.LauncherScreen.Part;

namespace CovertAction.Settings.Tests;

/// <summary>The key remapping (D-671, input.keyMap) and the gamepad's extra buttons (D-672, input.padMap): the settings, the host's translation, the launcher's Controls tab.</summary>
public sealed class KeyRemapSettingTests
{
    private const string Game = "C:/Game";
    private const int ScancodeQ = 20, ScancodeUp = 82, ScancodeCapsLock = 57, ScancodeF11 = 68, ScancodeShift = 225, ScancodeA = 4;

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

        public Func<string, string, string?>? PickFolder => null;
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

    private static void Click(LauncherScreen screen, Canvas canvas, Item item, Part part = Part.Row)
    {
        var area = screen.Area(item, part);
        var (x, y) = (area.X + area.Width / 2, area.Y + area.Height / 2);
        _ = screen.Handle(new PointerPressed(x, y)) ?? screen.Handle(new PointerReleased(x, y));
        screen.Render(canvas);
    }

    private static StartSettings Start(string json) =>
        StartSettings.Resolve(CommandLine.Parse([]), _ => null, SettingsFile.Parse(json).Settings);

    [Fact]
    public void BothMapsAreEmptyByDefaultAndTheTemplateAsksForNothing()
    {
        Assert.Empty(StartSettings.Resolve(CommandLine.Parse([]), _ => null, new GameSettings()).KeyMap);
        var template = SettingsFile.Parse(SettingsFile.Template);
        Assert.Empty(template.Problems);
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, template.Settings);
        Assert.Empty(start.KeyMap);
        Assert.Empty(start.PadMap);
        Assert.True(new KeyRemap(start.KeyMap, start.PadMap).IsEmpty);
    }

    [Fact]
    public void TheListsAreReadFromTheFileInTheirCanonicalSpellingAndWrongPairsAreReported()
    {
        var file = SettingsFile.Parse("""{ "input": { "keyMap": ["q = up", "CapsLock=Esc", "F11=F1", "Shift=A", "Z=CapsLock", "nonsense"], "padMap": ["start=Esc", "A=Esc"] } }""");
        Assert.Equal(["Q=Up", "CapsLock=Esc", "F11=F1"], file.Settings.KeyMap);
        Assert.Equal(["Start=Esc"], file.Settings.PadMap);
        Assert.Equal(4, file.Problems.Count);
        Assert.NotEmpty(SettingsFile.Parse("""{ "input": { "keyMap": "Q=Up" } }""").Problems);
    }

    [Fact]
    public void AnEmptyMapChangesNoKey()
    {
        var none = KeyRemap.None;
        for (var scancode = 4; scancode < 110; scancode++)
        {
            foreach (var sym in new[] { 0, 'q', '5', ',' })
            {
                foreach (var mod in new[] { 0, 0x0001 })
                {
                    Assert.Equal(SdlKeyMap.ToGameKey(scancode, sym, mod), SdlKeyMap.ToGameKey(none, scancode, sym, mod));
                }
            }
        }
        Assert.Empty(none.PadPresses(_ => true));
    }

    [Fact]
    public void AMappedKeyArrivesAsTheOriginalsKeyAndOthersAreUntouched()
    {
        var start = Start("""{ "input": { "keyMap": ["Q=Up", "CapsLock=A", "F11=F1"] } }""");
        var remap = new KeyRemap(start.KeyMap, start.PadMap);
        Assert.False(remap.IsEmpty);
        Assert.Equal(SdlKeyMap.ToGameKey(ScancodeUp, 0, 0), SdlKeyMap.ToGameKey(remap, ScancodeQ, 'q', 0));
        Assert.Equal(HostKeys.Up | HostKeys.GreyArrow, SdlKeyMap.ToGameKey(remap, ScancodeQ, 'q', 0));
        Assert.Equal(HostKeys.ForCharacter('a'), SdlKeyMap.ToGameKey(remap, ScancodeCapsLock, 0, 0));
        Assert.Equal(HostKeys.ForCharacter('A'), SdlKeyMap.ToGameKey(remap, ScancodeCapsLock, 0, 0x0001));
        Assert.Equal(HostKeys.F1, SdlKeyMap.ToGameKey(remap, ScancodeF11, 0, 0));
        // a key that is not mapped is as it was (the key mapped to still works itself)
        Assert.Equal(SdlKeyMap.ToGameKey(ScancodeA, 'a', 0), SdlKeyMap.ToGameKey(remap, ScancodeA, 'a', 0));
        Assert.Equal(SdlKeyMap.ToGameKey(ScancodeUp, 0, 0), SdlKeyMap.ToGameKey(remap, ScancodeUp, 0, 0));
    }

    [Fact]
    public void APadButtonArrivesAsItsKeyOncePerPress()
    {
        var start = Start("""{ "input": { "padMap": ["Start=Esc", "LeftTrigger=F1", "RightShoulder=Space"] } }""");
        var remap = new KeyRemap(start.KeyMap, start.PadMap);
        var held = new HashSet<int>();
        Assert.Empty(remap.PadPresses(held.Contains));
        held.Add(6);
        var first = remap.PadPresses(held.Contains);
        Assert.Equal([HostKeys.Escape], first.Select(SdlKeyMap.PadGameKey));
        Assert.Empty(remap.PadPresses(held.Contains));               // held: no repeat
        held.Remove(6);
        held.Add(KeyBindings.TriggerBase + 4);
        held.Add(10);
        Assert.Equal(new[] { HostKeys.F1, HostKeys.Space }.Order(), remap.PadPresses(held.Contains).Select(SdlKeyMap.PadGameKey).Order());
        held.Remove(6);
        Assert.Empty(remap.PadPresses(held.Contains));
        held.Clear();
        remap.PadPresses(held.Contains);
        held.Add(6);
        Assert.Single(remap.PadPresses(held.Contains));              // pressed again
    }

    [Fact]
    public void TheMapsGoThroughTheSettingsFileAndBack()
    {
        var (_, model, _) = Open(Context());
        Assert.True(model.Bind(KeyBindings.Find("Up")!, ScancodeQ));
        model.CyclePadKey(KeyBindings.FindPad("Start")!, 1);
        var changes = model.Changes();
        Assert.Equal("""["Q=Up"]""", changes["input.keyMap"]!.ToJsonString());
        Assert.StartsWith("""["Start=""", changes["input.padMap"]!.ToJsonString(), StringComparison.Ordinal);

        var written = SettingsFileEdits.Set(SettingsFile.Template, changes);
        var read = SettingsFile.Parse(written);
        Assert.Empty(read.Problems);
        Assert.Equal(["Q=Up"], read.Settings.KeyMap);
        Assert.Equal(model.PadMap.Select(KeyBindings.Format), read.Settings.PadMap!);

        // reset: an empty list is written back
        var (_, again, _) = Open(Context(read.Settings));
        again.ResetKeys();
        again.ResetPad();
        var cleared = SettingsFile.Parse(SettingsFileEdits.Set(written, again.Changes()));
        Assert.Empty(cleared.Settings.KeyMap!);
        Assert.Empty(cleared.Settings.PadMap!);
    }

    [Fact]
    public void TheControlsTabBindsTheKeyPressedClearsAndResets()
    {
        var (screen, model, canvas) = Open(Context());
        Click(screen, canvas, Item.TabControls);
        Assert.True(screen.ControlsShown);
        Assert.Empty(model.Changes());
        Assert.Equal("Up arrow", model.ShownKey.Label);

        Click(screen, canvas, Item.KeyBind);
        Assert.True(screen.Capturing);
        screen.Handle(new KeyPressed(LauncherKey.Other, Scancode: ScancodeShift));        // a modifier is refused, still waiting
        Assert.True(screen.Capturing);
        screen.Handle(new KeyPressed(LauncherKey.Other, Scancode: ScancodeQ));
        Assert.False(screen.Capturing);
        Assert.Equal([("Q", "Up")], model.KeyMap);
        screen.Render(canvas);
        Assert.Contains("now sends", screen.Message, StringComparison.Ordinal);
        Assert.Equal("""["Q=Up"]""", model.Changes()["input.keyMap"]!.ToJsonString());

        // Esc while waiting cancels (and does not leave the launcher)
        Click(screen, canvas, Item.KeyBind);
        Assert.Null(screen.Handle(new KeyPressed(LauncherKey.Escape)));
        Assert.False(screen.Capturing);
        Assert.Single(model.KeyMap);

        Click(screen, canvas, Item.KeyClear);
        Assert.Empty(model.KeyMap);
        Assert.Empty(model.Changes());
        Click(screen, canvas, Item.KeyPick, Part.Next);
        Assert.Equal("Down arrow", model.ShownKey.Label);
        model.Bind(model.ShownKey, ScancodeQ);
        Click(screen, canvas, Item.KeysReset);
        Assert.Empty(model.KeyMap);
    }

    [Fact]
    public void TheControlsTabMapsAPadButtonAndKeepsInsideThePanel()
    {
        var (screen, model, canvas) = Open(Context());
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        Assert.True(screen.ControlsShown);
        screen.Render(canvas);
        var panel = LauncherScreen.SettingsPanel;
        Assert.All(screen.Blocks, b => Assert.True(panel.Holds(b.Area), $"{b.What} at {b.Area} is outside the panel {panel}"));
        Click(screen, canvas, Item.PadPick, Part.Next);
        Click(screen, canvas, Item.PadKey, Part.Next);
        Assert.Equal("Right shoulder", model.ShownPad.Label);
        Assert.Single(model.PadMap);
        Assert.Equal("Right shoulder", KeyBindings.FindPad(model.PadMap[0].From)!.Label);
        Click(screen, canvas, Item.PadReset);
        Assert.Empty(model.PadMap);
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        Assert.False(screen.ControlsShown);
        Assert.Equal(3, CommandLine.Parse(["--launcher-screenshot-tab", "controls"]).LauncherScreenshotTab);
        var context = Context();
        Assert.NotEqual(Launcher.Picture(context, tab: 2).Pixels.ToArray(), Launcher.Picture(context, tab: 3).Pixels.ToArray());
    }

    [Fact]
    public void StartingWithTheListsGivesTheLauncherThemAndKeepsThemWhenNothingChanges()
    {
        var settings = SettingsFile.Parse("""{ "gameFolder": "C:/Game", "input": { "keyMap": ["Q=Up"], "padMap": ["Start=Esc"] } }""").Settings;
        var (_, model, _) = Open(Context(settings));
        Assert.Equal([("Q", "Up")], model.KeyMap);
        Assert.Equal([("Start", "Esc")], model.PadMap);
        Assert.Empty(model.Changes());
        Assert.Equal(["Q=Up"], model.Settings().KeyMap!);
    }
}
