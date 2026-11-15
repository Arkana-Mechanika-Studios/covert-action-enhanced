using CovertAction.Content;
using CovertAction.Core.Shared;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Persistence;
using CovertAction.Persistence.Layout;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The mouse, a player option (docs/faithfulness/DIVERGENCES.md D-630 to D-633; csharp/docs/MOUSE-SUPPORT.md): off, the
/// game's world has no mouse; on, a scripted pointer session ends in the same state as the key session it stands for
/// (a menu path of the menus part, one of the campaign, the flight map by its list and by a city's dot). Whole sessions
/// need the player's game files (skipped without them).
/// </summary>
public sealed class MouseOptionTests
{
    private const sbyte FreshStart = 0x0b, PracticeChase = 0x0e, CampaignPart = 0, LeaveTheGame = -1;

    /// <summary>The colour of a box's highlight bar (LibraryMenus.BarColor).</summary>
    private const byte BarColour = 0xe;

    /// <summary>The flight map's list: its left edge and the top of the line of the first city (line 0; "stay here" is line -1).</summary>
    private const int TravelListX = 0xc0, TravelFirstCityY = 0x21, TravelLineHeight = 8;

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "GAME.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    // ---- the faithful-mode guard ----

    /// <summary>Without the option (the default, and every other test's and the session proof's game) the PC has no mouse.</summary>
    [SkippableFact]
    public void WithoutTheOptionTheGamesWorldHasNoMouse()
    {
        var files = GameFiles();
        Assert.IsType<NoMouseDriver>(new Game(files, new HeadlessHost([])).Mouse);
        Assert.IsType<NoMouseDriver>(new Game(files, new HeadlessHost([]), null, new GameOptions()).Mouse);
        Assert.False(new Game(files, new HeadlessHost([])).Mouse.Installed);
        // the option on a host without a pointer: no mouse either
        Assert.IsType<NoMouseDriver>(new Game(files, new NoPointerHost(), null, new GameOptions(Mouse: true)).Mouse);
        // and on: the host's pointer
        Assert.IsType<HostMouseDriver>(new Game(files, new HeadlessHost([]), null, new GameOptions(Mouse: true)).Mouse);
    }

    // ---- the menus part (FINAL): main menu, level, skill ----

    /// <summary>
    /// Practice, level 0, driving: by the keys; by clicks on the choices; by pointing and Return. The three end in the
    /// same hand-off block (the chase's practice is next).
    /// </summary>
    [SkippableFact]
    public void AMenuPathOfTheMenusPartByTheMouseEndsAsByTheKeys()
    {
        int[] wait = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];
        int[] keys = [.. wait, HostKeys.Space, .. wait, HostKeys.Down, .. wait, HostKeys.Down, .. wait, HostKeys.Enter, .. wait, HostKeys.Enter, .. wait, HostKeys.Down, .. wait, HostKeys.Enter, .. wait];
        var byKeys = RunMenus(_ => keys, mouse: false);
        var byKeysWithTheOption = RunMenus(_ => keys, mouse: true);

        IEnumerable<int> Clicks(ScreenWatch screen)
        {
            foreach (var entry in wait.Append(HostKeys.Space))
            {
                yield return entry;
            }
            foreach (var below in new[] { 2, 0, 1 })
            {
                foreach (var entry in ClickChoice(screen, below))
                {
                    yield return entry;
                }
            }
            foreach (var entry in wait)
            {
                yield return entry;
            }
        }
        var byClicks = RunMenus(Clicks, mouse: true);

        IEnumerable<int> Pointing(ScreenWatch screen)
        {
            foreach (var entry in wait.Append(HostKeys.Space))
            {
                yield return entry;
            }
            foreach (var below in new[] { 2, 0, 1 })
            {
                foreach (var entry in PointAtChoice(screen, below))
                {
                    yield return entry;
                }
                yield return HostKeys.Enter;
            }
            foreach (var entry in wait)
            {
                yield return entry;
            }
        }
        var byPointing = RunMenus(Pointing, mouse: true);

        Assert.Equal(PracticeChase, byKeys.Shared.NextModule);
        Assert.Equal(byKeys.Block, byKeysWithTheOption.Block);
        Assert.Equal(byKeys.Block, byClicks.Block);
        Assert.Equal(byKeys.Block, byPointing.Block);
    }

    /// <summary>A right click in a box is Esc: the main menu's Practice, then a right click on the level's box goes back to the main menu.</summary>
    [SkippableFact]
    public void ARightClickInABoxIsEsc()
    {
        int[] wait = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];
        int[] keys = [.. wait, HostKeys.Space, .. wait, HostKeys.Down, .. wait, HostKeys.Down, .. wait, HostKeys.Enter, .. wait, HostKeys.Escape, .. wait];
        int[] clicks = [.. wait, HostKeys.Space, .. wait, HostKeys.Down, .. wait, HostKeys.Down, .. wait, HostKeys.Enter, .. wait, .. HeadlessHost.Click(5, 5, button: 1), .. wait];
        var byKeys = RunMenus(_ => keys, mouse: false);
        var byClick = RunMenus(_ => clicks, mouse: true);
        Assert.Equal(byKeys.Block, byClick.Block);
        Assert.Equal(byKeys.Pixels, byClick.Pixels);
    }

    private static (SharedState Shared, byte[] Block, byte[] Pixels) RunMenus(Func<ScreenWatch, IEnumerable<int>> script, bool mouse)
    {
        Game game = null!;
        // the script is read as the game asks for it, so it can look at the game's screen as it is then
        HeadlessHost host = null!;
        host = new HeadlessHost(Lazy(() => script(new ScreenWatch(() => game.Graphics.Screen.ToArray(), () => host.PointerReads))), unixSeconds: 600_000_000);
        var shared = new SharedState { NextModule = FreshStart, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };
        game = new Game(GameFiles(), host, null, new GameOptions(Mouse: mouse));
        game.RunPart(shared);
        return (shared, Block(shared), host.Pixels.ToArray());
    }

    // ---- the campaign (GAME): the city screen, the flight map, the hotel ----

    /// <summary>
    /// The city screen's airport, the flight map's second city, the hotel, quit: by the keys, by clicks on the boxes'
    /// choices and on the flight map's list, and with the city's dot on the map clicked. All end in the same hand-off block.
    /// </summary>
    [SkippableFact]
    public void ACampaignWalkAndTheFlightMapByTheMouseEndAsByTheKeys()
    {
        int[] choose(int downs) => [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, .. Enumerable.Repeat(HostKeys.Down, downs), HostKeys.Enter];
        int[] keys = [.. choose(0), .. choose(2), .. choose(1), .. choose(8), .. choose(0)];
        var byKeys = RunCampaign(_ => keys, mouse: false);

        IEnumerable<int> ByList(ScreenWatch screen) => Walk(screen, HeadlessHost.Click(TravelListX + 16, TravelFirstCityY + TravelLineHeight + 4));
        var byList = RunCampaign(ByList, mouse: true);

        var folder = GameFiles();
        var secondCity = CampaignTests.CampaignUnderWay(folder).Campaign.Cities[1];
        IEnumerable<int> ByDot(ScreenWatch screen) => Walk(screen, HeadlessHost.Click(secondCity.GeoX, secondCity.GeoY));
        var byDot = RunCampaign(ByDot, mouse: true);

        Assert.Equal(1, byKeys.Shared.City);
        Assert.Equal(LeaveTheGame, byKeys.Shared.NextModule);
        Assert.Equal((byKeys.Shared.City, byKeys.Shared.NextModule, byKeys.Shared.Clock), (byList.Shared.City, byList.Shared.NextModule, byList.Shared.Clock));
        Assert.Equal((byKeys.Shared.City, byKeys.Shared.NextModule, byKeys.Shared.Clock), (byDot.Shared.City, byDot.Shared.NextModule, byDot.Shared.Clock));
        Assert.Equal(byKeys.Block, byList.Block);
        Assert.Equal(byKeys.Block, byDot.Block);
    }

    /// <summary>The walk of the campaign test with the boxes clicked and the flight map's entries given.</summary>
    private static IEnumerable<int> Walk(ScreenWatch screen, int[] flight)
    {
        foreach (var entry in ClickChoice(screen, 0))
        {
            yield return entry;     // the city screen: the airport
        }
        foreach (var entry in TravelMap(screen).Concat(flight))
        {
            yield return entry;     // the flight map
        }
        foreach (var entry in ClickChoice(screen, 1))
        {
            yield return entry;     // the second city: the hotel
        }
        foreach (var entry in ClickChoice(screen, 6))
        {
            yield return entry;     // the hotel: its last choice, quit the game (the keys' eight downs stop there)
        }
        yield return HeadlessHost.NoKeyYet;
        yield return HeadlessHost.NoKeyYet;
        yield return HostKeys.Enter;    // the farewell picture
    }

    private static (SharedState Shared, byte[] Block) RunCampaign(Func<ScreenWatch, IEnumerable<int>> script, bool mouse)
    {
        var folder = GameFiles();
        Game game = null!;
        HeadlessHost host = null!;
        host = new HeadlessHost(Lazy(() => script(new ScreenWatch(() => game.Graphics.Screen.ToArray(), () => host.PointerReads))), unixSeconds: 600_000_000);
        var shared = CampaignTests.CampaignUnderWay(folder);
        game = new Game(folder, host, null, new GameOptions(Mouse: mouse));
        game.RunPart(shared);
        return (shared, Block(shared));
    }

    // ---- helpers ----



    /// <summary>Moments without a key until the flight map is up: its "stay here" line highlighted (CityScreens.ChooseDestination).</summary>
    private static IEnumerable<int> TravelMap(ScreenWatch screen)
    {
        // the highlight turns the line's letters from light grey into its colour
        const int patience = 2000, stayHereY = TravelFirstCityY - TravelLineHeight, some = 10;
        const byte highlight = 0xb, light = 7;
        var reads = screen.Reads();
        var lookedBefore = false;
        for (var moment = 0; moment < patience; moment++)
        {
            yield return HeadlessHost.NoKeyYet;
            var now = screen.Read();
            var looked = screen.Reads() > reads && lookedBefore;
            lookedBefore = screen.Reads() > reads;
            reads = screen.Reads();
            int Count(int line, byte colour) => Enumerable.Range(stayHereY + line * TravelLineHeight, TravelLineHeight).Sum(y => now.AsSpan(y * 320 + TravelListX, 320 - TravelListX).Count(colour));
            // "stay here" highlighted, and the sixteen cities' lines below it in light grey
            if (looked && Count(0, highlight) >= some && Enumerable.Range(1, 16).All(line => Count(line, light) >= some && Count(line, highlight) == 0))
            {
                yield break;
            }
        }
        throw new InvalidOperationException("no flight map came up");
    }

    /// <summary>A click on the choice some lines below the highlighted one of the next box on the screen.</summary>
    private static IEnumerable<int> ClickChoice(ScreenWatch screen, int below)
    {
        foreach (var entry in NextBox(screen))
        {
            yield return entry;
        }
        var (x, y) = ChoiceBelowBar(screen.Read(), below)!.Value;
        yield return HeadlessHost.PointerMove(x, y);
        yield return HeadlessHost.PointerPress(0);
        yield return HeadlessHost.PointerRelease(0);
    }

    /// <summary>The pointer moved onto the choice some lines below the highlighted one of the next box (the key that takes it follows).</summary>
    private static IEnumerable<int> PointAtChoice(ScreenWatch screen, int below)
    {
        foreach (var entry in NextBox(screen))
        {
            yield return entry;
        }
        var (x, y) = ChoiceBelowBar(screen.Read(), below)!.Value;
        yield return HeadlessHost.PointerMove(x, y);
        yield return HeadlessHost.NoKeyYet;
    }

    /// <summary>
    /// Moments without a key until the next box waits: it looks at the mouse in every pass (after it has thrown away what
    /// came before it opened) and its highlight bar is on the game's screen.
    /// </summary>
    private static IEnumerable<int> NextBox(ScreenWatch screen)
    {
        const int patience = 2000;
        var reads = screen.Reads();
        var lookedBefore = false;
        for (var moment = 0; moment < patience; moment++)
        {
            yield return HeadlessHost.NoKeyYet;
            var now = screen.Read();
            // a box waiting: it looked at the mouse in the last two moments (the box answered last looks once more, when
            // it takes the click), and its highlight bar is on the screen
            var looked = screen.Reads() > reads;
            reads = screen.Reads();
            var waiting = looked && lookedBefore;
            lookedBefore = looked;
            if (waiting && ChoiceBelowBar(now, 0) is not null)
            {
                yield break;
            }
        }
        throw new InvalidOperationException("no box came up");
    }

    /// <summary>
    /// Where the choice some lines below the highlight bar is on the game's screen (what the host shows can be a frame
    /// behind): the bar is a band 9 rows high of the box's bar colour (LibraryMenus.RecolorChoice turns the box's colour
    /// under a choice's line into it; the letters keep theirs), whose top row has no letters; a box's lines are 8 pixels apart.
    /// </summary>
    private static (int X, int Y)? ChoiceBelowBar(byte[] screen, int below)
    {
        const int width = 320, height = 200, barHeight = 9, lineHeight = 8, span = 24;
        bool Bar(int x, int y) => x >= 0 && x < width && y >= 0 && y < height && screen[y * width + x] == BarColour;
        for (var top = 0; top + barHeight <= height; top++)
        {
            for (var x = 0; x + span <= width; x++)
            {
                // the bar's top left corner: its top row is (nearly) all of the bar's colour, and its left edge (before
                // the line's letters begin) runs down all nine rows
                if (!Bar(x, top) || Bar(x - 1, top) || Bar(x, top - 1)
                    || Enumerable.Range(x, span).Count(c => Bar(c, top)) < span - 2
                    || !Enumerable.Range(top, barHeight).All(y => Bar(x, y) && Bar(x + 1, y)))
                {
                    continue;
                }
                return (x + 6, top + 4 + below * lineHeight);
            }
        }
        return null;
    }

    /// <summary>An enumerable whose entries are made only when the first is asked for.</summary>
    private static IEnumerable<int> Lazy(Func<IEnumerable<int>> make)
    {
        foreach (var entry in make())
        {
            yield return entry;
        }
    }

    /// <summary>The hand-off block as the original's bytes, with the campaign tables.</summary>
    private static byte[] Block(SharedState shared)
    {
        var block = new byte[12000];
        SharedBlockCodec.Layout.Store(shared, block);
        SharedBlockCodec.StoreCampaign(shared, block);
        return block;
    }

    /// <summary>The game's screen as a script reads it, and how often the game has looked at the mouse.</summary>
    private sealed class ScreenWatch(Func<byte[]> read, Func<int> reads)
    {
        public byte[] Read() => read();

        /// <summary>How often the game has looked at the mouse (HeadlessHost.PointerReads).</summary>
        public int Reads() => reads();
    }

    /// <summary>A host without a pointer (a host that has no mouse).</summary>
    private sealed class NoPointerHost : IHost, IInput
    {
        private readonly HeadlessHost _inner = new([]);

        public IVideo Video => _inner.Video;

        public IInput Input => this;

        public IPcmOutput Audio => _inner.Audio;

        public IClock Clock => _inner.Clock;

        public bool QuitRequested => _inner.QuitRequested;

        public void Pump()
        {
        }

        public bool TryReadKey(out int key) => _inner.TryReadKey(out key);
    }
}
