using System.Reflection;
using CovertAction.Host.Headless;
using Xunit.Abstractions;
using CovertAction.Presentation.Raid;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence;
using CovertAction.Core.Raid;
using Xunit;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Electronics;
using CovertAction.Core.Final;
using CovertAction.Core.Shared;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Electronics;
using CovertAction.Presentation.Final;
using CovertAction.Presentation.Game;
using CovertAction.Presentation.Library;
using CovertAction.Drivers;
using CovertAction.Content;
using CovertAction.Launcher;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The fixes of the launcher's option "Fix the original's bugs" (docs/original-bugs.md CA-4, CA-16, CA-34, CA-40, CA-41,
/// CA-43; the Tandy driver's CA-55 is in the audio tests). Each bug is a pair: the original's behaviour is shown with
/// <see cref="OriginalBehaviour.Player"/> (what release 1.0 plays), the fixed behaviour with the option on. Hand-made
/// data in a temporary folder and recording stand-ins for the drivers: no game files are needed.
/// </summary>
public sealed class OriginalBugFixesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "covert-bugfix-" + Guid.NewGuid().ToString("N"));
    private readonly List<GameFolderFiles> _files = [];

    private readonly ITestOutputHelper output;

    public OriginalBugFixesTests(ITestOutputHelper output)
    {
        this.output = output;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        _files.ForEach(f => f.Dispose());
        Directory.Delete(_folder, true);
    }

    /// <summary>What the player has with the option off (release 1.0), and with it on.</summary>
    private static readonly OriginalBehaviour Off = OriginalBehaviour.ForPlayer(false, false), On = OriginalBehaviour.ForPlayer(true, false);

    private Boundaries World(IGraphicsDriver? graphics = null, IInputDriver? input = null)
    {
        var files = new GameFolderFiles(new GameFolder(_folder));
        _files.Add(files);
        return new Boundaries(graphics ?? Recorder<IGraphicsDriver>.Create(), new SilentSoundDriver(), input ?? new KeysWaiting(), new NoMouseDriver(), null!, files);
    }

    /// <summary>Answers every call of an interface with its default (nothing drawn, nothing read) and remembers the calls' names.</summary>
    public class Recorder<T> : DispatchProxy
        where T : class
    {
        public List<string> Calls { get; } = [];

        public static T Create() => Create<T, Recorder<T>>();

        public static (T Proxy, List<string> Calls) CreateRecording()
        {
            var proxy = Create<T, Recorder<T>>();
            return (proxy, ((Recorder<T>)(object)proxy).Calls);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            var type = targetMethod.ReturnType;
            return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
        }
    }

    /// <summary>A keyboard that always has the same key waiting, or a list of keys and then none.</summary>
    private sealed class KeysWaiting(params int[] keys) : IInputDriver
    {
        private int _next;

        public int Taken => _next;

        public bool NoKeyWaiting() => keys.Length == 0 ? false : _next >= keys.Length;

        public int ReadKeyRaw() => keys.Length == 0 ? 0x1c0d : keys[_next++];

        public void CalibrateJoystick()
        {
        }

        public int ReadJoystickAxes() => 0;

        public int ReadJoystickButtons() => 0xff;

        public bool Kbhit() => !NoKeyWaiting();

        public int Getch() => ReadKeyRaw() & 0xff;

        public bool NumLock() => true;

        public void SetNumLock(bool on)
        {
        }
    }

    /// <summary>A keyboard for a box that flushes the keys and then waits for one: empty at the first look, Return waiting from then on.</summary>
    private sealed class ReturnAfterFlush : IInputDriver
    {
        private int _looks;

        public bool NoKeyWaiting() => _looks++ == 0;

        public int ReadKeyRaw() => 0x1c0d;

        public void CalibrateJoystick()
        {
        }

        public int ReadJoystickAxes() => 0;

        public int ReadJoystickButtons() => 0xff;

        public bool Kbhit() => !NoKeyWaiting();

        public int Getch() => 0x0d;

        public bool NumLock() => true;

        public void SetNumLock(bool on)
        {
        }
    }

    // ---- CA-16 ----

    private string ReadClue(OriginalBehaviour behaviour, string tag)
    {
        File.WriteAllText(Path.Combine(_folder, "CLUES.TXT"), "*C0904 \r\n53Airline ticket traced to a courier.\r\n*C1101\r\n53Another entry.\r\n");
        var state = new CampaignState();
        var text = new LibraryText(state, state, World(), new LibraryOutput(state, World()), state.Random, behaviour);
        text.ReadClearedText("CLUES.TXT", tag, [], [], 0);
        return state.Txt.Text;
    }

    [Fact]
    public void Ca16TheBagmansClueIsNotFoundBecauseItsTagLineHasABlankAtTheEnd()
    {
        // the original compares the whole line: "*C0904 " is not "*C0904"
        Assert.DoesNotContain("Airline ticket", ReadClue(Off, "*C0904"), StringComparison.Ordinal);
        Assert.Contains("*C0904", ReadClue(Off, "*C0904"), StringComparison.Ordinal);
        Assert.Contains("Airline ticket traced", ReadClue(On, "*C0904"), StringComparison.Ordinal);
    }

    [Fact]
    public void Ca16TheFixChangesNoOtherTag()
    {
        Assert.Equal(ReadClue(Off, "*C1101"), ReadClue(On, "*C1101"));
        Assert.Contains("Another entry", ReadClue(On, "*C1101"), StringComparison.Ordinal);
        Assert.Equal(ReadClue(Off, "*C9999"), ReadClue(On, "*C9999"));
    }

    // ---- CA-34 ----

    private static (bool Skipped, bool KeyLeft) SkipAnimation(OriginalBehaviour behaviour, bool ignoreOne)
    {
        var input = new KeysWaiting(0x1c0d);
        var state = new FinalState();
        var world = new Boundaries(Recorder<IGraphicsDriver>.Create(), new SilentSoundDriver(), input, new NoMouseDriver(), null!, null!);
        var animation = new LibraryAnimation(state, state.Pan, world, new LibraryOutput(state, world), new LibraryInput(state, world), new LibraryPictures(world), behaviour);
        state.Pan.AnimationSteps = 3;
        state.Pan.IgnoreSkip = (short)(ignoreOne ? 1 : 0);
        return (animation.AnimationSkipPressed(), !input.NoKeyWaiting());
    }

    [Fact]
    public void Ca34TheKeyThatSkipsAnAnimationStaysInTheBuffer()
    {
        Assert.Equal((true, true), SkipAnimation(Off, ignoreOne: false));
        Assert.Equal((false, true), SkipAnimation(Off, ignoreOne: true));
    }

    [Fact]
    public void Ca34WithTheFixTheKeyIsTaken()
    {
        Assert.Equal((true, false), SkipAnimation(On, ignoreOne: false));
        Assert.Equal((false, false), SkipAnimation(On, ignoreOne: true));
    }

    // ---- CA-41 ----

    private (string Text, string Key) CaptureComment(OriginalBehaviour behaviour, params int[] variants)
    {
        File.WriteAllText(Path.Combine(_folder, "PROSE.DTA"), "*nice0\r\nVariant zero.\r\n*nice1\r\nVariant one.\r\n*nice2\r\nVariant two.\r\n");
        var state = new CampaignState { ProseFileName = "PROSE.DTA", ProseReplacementToken = "$RPLC", ProseNameToken = "$NAME", ProseCityToken = "$CITY", ProseOrgToken = "$ORG", ProseAgencyToken = "$AGENCY" };
        state.CaptureProseKey.Text = "*nice0";
        state.VariableNames[0] = "$VICTIM";
        var screens = new CampaignScreens(state, World(), null, behaviour);
        var text = "";
        foreach (var variant in variants)
        {
            state.Txt.Text = "";
            screens.LoadProseText(state.CaptureProseKey, 0x24, "", variant);
            text = state.Txt.Text;
        }
        return (text, state.CaptureProseKey.Text);
    }

    [Fact]
    public void Ca41EveryCaptureAfterTheFirstReadsTheFirstOnesVariant()
    {
        var (text, key) = CaptureComment(Off, 1, 2);
        Assert.Contains("Variant one", text, StringComparison.Ordinal);
        Assert.Equal("*nice1", key);
    }

    [Fact]
    public void Ca41WithTheFixEveryCaptureReadsItsOwnVariant()
    {
        var (text, key) = CaptureComment(On, 1, 2);
        Assert.Contains("Variant two", text, StringComparison.Ordinal);
        Assert.Equal("*nice0", key);
        Assert.Contains("Variant zero", CaptureComment(On, 2, 0).Text, StringComparison.Ordinal);
    }

    // ---- CA-43 ----

    private static string EditName(OriginalBehaviour behaviour, string name, params int[] keys)
    {
        const int delete = 0x5300, @return = 0x1c0d;
        var state = new FinalState();
        var world = new Boundaries(Recorder<IGraphicsDriver>.Create(), new SilentSoundDriver(), new KeysWaiting([.. keys, @return]), new NoMouseDriver(), null!, null!);
        var headquarters = new Headquarters(state, new SharedState(), world, behaviour);
        state.Txt.Text = name;
        headquarters.EditTextField(0x6a, 0x64, 15);
        _ = delete;
        return state.Txt.Text;
    }

    [Fact]
    public void Ca43DeleteTakesOutTheCharacterAndBlanksTheNext()
    {
        // the cursor stands on "A": it goes, and "B", which moves under the cursor, is blanked
        Assert.Equal(" CD", EditName(Off, "ABCD", 0x5300));
    }

    [Fact]
    public void Ca43WithTheFixDeleteTakesOutOneCharacter()
    {
        Assert.Equal("BCD", EditName(On, "ABCD", 0x5300));
        Assert.Equal("D", EditName(On, "ABCD", 0x5300, 0x5300, 0x5300));
    }

    // ---- CA-40 ----

    /// <summary>
    /// The number of phones the wiretap puzzle reports when the time runs out <paramref name="ticks"/> ticks into a call of the
    /// clock, on an empty board whose ten phones are all dead, the count standing at 0 before the last evaluation.
    /// </summary>
    private static int PhonesReported(OriginalBehaviour behaviour, int ticks)
    {
        var folder = GameFiles();
        var graphics = new McgaGraphicsDriver(new FontSet(folder.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]);
        var state = new ElectronicsState { Mode = ElectronicsMode.PhoneTaps, Status = Options.Practice, ClockStr = "00:00:01", TimeLeft = 1, PhonesOk = 0 };
        var world = new Boundaries(graphics, new SilentSoundDriver(), new ReturnAfterFlush(), new NoMouseDriver(), null!, null!);
        var workbench = new ElectronicsWorkbench(state, new SharedState(), world, behaviour);
        // the time runs out at the last tick of a second (every eighth tick)
        state.TickCount = (short)(8 - ticks);
        workbench.AdvanceTimer(ticks);
        Assert.Equal(1, state.DoneFlag);
        return state.ResultPhones;
    }

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "FONTS.CV")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    [SkippableFact]
    public void Ca40TheLastEvaluationRecountsOnlyWhenTheTimeEndsAfterTheFirstTickOfACall()
    {
        // the original gives bug_status_message no argument and it takes a stack word: the clock's loop counter.
        // At the first tick of a call the word is 0, the board is not drawn or counted, and the result keeps the old count
        Assert.Equal(0, PhonesReported(Off, 1));
        // at a later tick it is not 0 and the ten dead phones are counted
        Assert.Equal(10, PhonesReported(Off, 2));
    }

    [SkippableFact]
    public void Ca40WithTheFixTheLastEvaluationAlwaysCounts()
    {
        Assert.Equal(10, PhonesReported(On, 1));
        Assert.Equal(10, PhonesReported(On, 2));
    }

    // ---- CA-59: a wide piece of furniture over a wall ----

    private static RaidState TacState(GameFolder game)
    {
        var state = new RaidState();
        TacDgroup.Layout.Load(Executable.Read(game.Read("TAC.EXE")).DataSegment(), state);
        return state;
    }

    /// <summary>The cells of a furnished building where a wide piece's half stands on what was a wall or a corner before the furnishing.</summary>
    private static int WideHalvesOverWalls(GameFolder game, OriginalBehaviour behaviour, int seed, out int widePieces)
    {
        var state = TacState(game);
        Array.Fill(state.CellMap, RaidState.CellEmpty);
        state.Random.Srand(seed);
        do
        {
            state.Nroom = 0;
            BuildingRules.InitBuildingGrid(state);
            BuildingRules.AddWallSegments(state);
        }
        while (!BuildingRules.LabelRooms(state) || !BuildingRules.ConnectRooms(state, behaviour));
        var before = (byte[])state.CellMap.Clone();
        for (var room = 1; room <= state.Nroom; room++)
        {
            BuildingRules.FurnishRoom(state, room, behaviour);
        }
        var over = 0;
        widePieces = 0;
        for (var cell = 0; cell < before.Length; cell++)
        {
            var tile = state.CellMap[cell];
            if (tile >= 12 && tile < state.Ob.Length && (state.Ob[tile].Bits & TileFlags.Wide) != 0)
            {
                widePieces++;
                over += before[cell] != RaidState.CellEmpty ? 1 : 0;
            }
        }
        return over;
    }

    [SkippableFact]
    public void Ca59TheOtherHalfOfADeskOrCouchIsWrittenOverAWall()
    {
        var game = GameFiles();
        var over = Enumerable.Range(1, 60).Sum(seed => WideHalvesOverWalls(game, Off, seed, out _));
        Assert.True(over > 0, "no wide half over a wall in 60 buildings");
    }

    [SkippableFact]
    public void Ca59WithTheFixNoWideHalfLandsOnAWallAndPiecesAreStillPlaced()
    {
        var game = GameFiles();
        var pieces = 0;
        for (var seed = 1; seed <= 60; seed++)
        {
            Assert.Equal(0, WideHalvesOverWalls(game, On, seed, out var wide));
            pieces += wide;
        }
        Assert.True(pieces > 0);
    }

    // ---- CA-61: "Stasi" in the file room ----

    private static bool SearchOrgsFinds(OriginalBehaviour behaviour, string wanted)
    {
        var game = GameFiles();
        var state = new RaidState();
        state.Org[2].Sname = "Stasi";
        state.Org[2].Name = "Stassi";
        var graphics = new McgaGraphicsDriver(new FontSet(game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]);
        var world = new Boundaries(graphics, new SilentSoundDriver(), new ReturnAfterFlush(), new NoMouseDriver(), null!, null!);
        var raid = new BuildingRaid(state, new SharedState(), world, behaviour);
        return (bool)typeof(BuildingRaid).GetMethod("SearchOrgs", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(raid, [wanted])!;
    }

    [SkippableFact]
    public void Ca61TheCorrectSpellingOfStasiFindsNothingInTheFileRoom()
    {
        Assert.False(SearchOrgsFinds(Off, "STASI"));
        Assert.True(SearchOrgsFinds(Off, "STASS"));
    }

    [SkippableFact]
    public void Ca61WithTheFixTheShortKeyIsSearchedToo()
    {
        Assert.True(SearchOrgsFinds(On, "STASI"));
        Assert.True(SearchOrgsFinds(On, "STASS"));
        Assert.False(SearchOrgsFinds(On, "STASX"));
    }

    // ---- CA-64: a missing FAME.DTA ----

    private (string Saves, string Players) FameFolders()
    {
        var players = Path.Combine(_folder, "players");
        Directory.CreateDirectory(players);
        return (Path.Combine(_folder, "saves"), players);
    }

    private (Headquarters Hall, FinalState State) FameHall(OriginalBehaviour behaviour, string players, string saves, int score)
    {
        var files = new GameFolderFiles(new GameFolder(players), saves);
        _files.Add(files);
        var state = new FinalState();
        Array.Fill(state.FameFileNames, "fame.dta");
        state.FameTable[0].Score = (short)score;
        return (new Headquarters(state, new SharedState(), new Boundaries(null!, null!, null!, null!, null!, files), behaviour), state);
    }

    [Fact]
    public void Ca64WithoutAFameFileNoScoreIsSaved()
    {
        var (saves, players) = FameFolders();
        FameHall(Off, players, saves, 77).Hall.WriteFameFile();
        Assert.False(File.Exists(Path.Combine(saves, "FAME.DTA")));
        Assert.Empty(Directory.GetFiles(players));
    }

    [Fact]
    public void Ca64WithTheFixTheFileIsMadeInTheSavesFolderAndReadBack()
    {
        var (saves, players) = FameFolders();
        FameHall(On, players, saves, 77).Hall.WriteFameFile();
        Assert.True(new FileInfo(Path.Combine(saves, "FAME.DTA")).Length > 0);
        Assert.Empty(Directory.GetFiles(players));
        var (hall, state) = FameHall(On, players, saves, 0);
        hall.ReadFameFile();
        Assert.Equal(77, state.FameTable[0].Score);
    }

    [Fact]
    public void Ca64AnEmptyFameFileOfThePlayersFolderIsSavedToAsBefore()
    {
        var (saves, players) = FameFolders();
        File.WriteAllBytes(Path.Combine(players, "FAME.DTA"), []);
        // the game reads the Hall of Fame first, which takes the player's file into the saves folder
        void Save(OriginalBehaviour behaviour)
        {
            var (hall, state) = FameHall(behaviour, players, saves, 0);
            hall.ReadFameFile();
            state.FameTable[0].Score = 5;
            hall.WriteFameFile();
        }
        Save(Off);
        var length = new FileInfo(Path.Combine(saves, "FAME.DTA")).Length;
        File.Delete(Path.Combine(saves, "FAME.DTA"));
        Save(On);
        Assert.Equal(length, new FileInfo(Path.Combine(saves, "FAME.DTA")).Length);
        Assert.Equal(0, new FileInfo(Path.Combine(players, "FAME.DTA")).Length);
    }

    // ---- CA-62: a board that cannot be won ----

    /// <summary>A freshly built wiretap board of difficulty <paramref name="level"/>, as the electronics part builds it.</summary>
    private static ElectronicsState BuiltBoard(GameFolder game, OriginalBehaviour behaviour, int level, int seed)
    {
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(Executable.Read(game.Read("BUG.EXE")).DataSegment(), state);
        state.Mode = ElectronicsMode.PhoneTaps;
        state.Status = Options.Practice;
        state.Diff = (short)level;
        state.TimeLeft = 100;
        state.Random.Srand(seed);
        var graphics = new McgaGraphicsDriver(new FontSet(game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]);
        var world = new Boundaries(graphics, new SilentSoundDriver(), new ReturnAfterFlush(), new NoMouseDriver(), null!, null!);
        new ElectronicsWorkbench(state, new SharedState(), world, behaviour).BuildCircuitBoard();
        return state;
    }

    [SkippableFact]
    public void Ca62TheGeneratorBuildsBoardsTheBoundedSearchCannotWin()
    {
        var game = GameFiles();
        var hopeless = new List<int>();
        for (var seed = 1; seed <= 1500; seed++)
        {
            if (!CircuitSolver.CanBeWon(BuiltBoard(game, Off, 0, seed)))
            {
                hopeless.Add(seed);
            }
        }
        // the search is bounded, so a board it gives up on is not proven lost; the proven ones are in the register (CA-62)
        output.WriteLine($"TALLY CA-62: {hopeless.Count} of 1500 level-0 boards not won by the bounded search (option off): {string.Join(",", hopeless)}");
        Assert.NotEmpty(hopeless);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Ca62WithTheFixEveryBoardCanBeWon(int level)
    {
        var game = GameFiles();
        for (var seed = 1; seed <= 300; seed++)
        {
            Assert.True(CircuitSolver.CanBeWon(BuiltBoard(game, On, level, seed)), $"level {level} seed {seed}");
        }
    }

    // ---- CA-63: the keyboard repeat of the building raid ----

    private static List<bool> TypematicOfARaid(bool fix)
    {
        var host = new HeadlessHost(Enumerable.Repeat(HeadlessHost.NoKeyYet, 30), unixSeconds: 600_000_000);
        new Game(GameFiles(), host, null, new GameOptions(FixOriginalBugs: fix)).PlayRaidPractice(0);
        return host.TypematicSettings;
    }

    [SkippableFact]
    public void Ca63TheRaidKeepsThePcsKeyboardRepeat()
    {
        // the original's tac_init sends the setting to INT 10h: the host's repeat is never changed
        Assert.DoesNotContain(true, TypematicOfARaid(false));
    }

    [SkippableFact]
    public void Ca63WithTheFixTheRaidAsksForItsIntendedRepeatAndGivesItBack()
    {
        Assert.Equal([true, false], TypematicOfARaid(true));
    }
}
