using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Electronics;
using CovertAction.Core.Shared;
using CovertAction.Drivers;
using CovertAction.Persistence.Dgroup;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Electronics;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The electronics board's dormant fourth difficulty (docs/cut-content.md CC-16; DIVERGENCES.md D-662), a player option: the
/// original cuts the difficulty down to 3 and the option lets the hardest level reach 4 with an agent of the lowest rating.
/// A board of difficulty 4 is built with the part's own generator on the game's own data. Needs the player's game files
/// (skipped without them).
/// </summary>
public sealed class CutContentCircuitTests
{
    private static readonly OriginalBehaviour Off = OriginalBehaviour.ForPlayer(false, false);
    private static readonly OriginalBehaviour On = OriginalBehaviour.ForPlayer(false, false, new CutContentOptions(HardestCircuit: true));

    private sealed class NoKeys : IInputDriver
    {
        public bool NoKeyWaiting() => true;

        public int ReadKey() => 0;

        public int ReadKeyRaw() => 0;

        public void CalibrateJoystick()
        {
        }

        public int ReadJoystickAxes() => 0;

        public int ReadJoystickButtons() => 0xff;

        public bool Kbhit() => false;

        public int Getch() => 0;

        public bool NumLock() => true;

        public void SetNumLock(bool on)
        {
        }
    }

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "BUG.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    /// <summary>A freshly built wiretap board of the difficulty <paramref name="difficulty"/>, as the electronics part builds it.</summary>
    private static ElectronicsState BuiltBoard(GameFolder game, int difficulty, int seed)
    {
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(Executable.Read(game.Read("BUG.EXE")).DataSegment(), state);
        state.Mode = ElectronicsMode.PhoneTaps;
        state.Status = Options.Practice;
        state.Diff = (short)difficulty;
        state.TimeLeft = 100;
        state.Random.Srand(seed);
        var graphics = new McgaGraphicsDriver(new FontSet(game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]);
        var world = new Boundaries(graphics, new SilentSoundDriver(), new NoKeys(), new NoMouseDriver(), null!, null!);
        new ElectronicsWorkbench(state, new SharedState(), world, Off).BuildCircuitBoard();
        return state;
    }

    private static (int Shown, int Fixed, int HiddenFixed) Count(ElectronicsState state)
    {
        int shown = 0, locked = 0, hiddenLocked = 0;
        for (var column = 0; column < ElectronicsState.Columns; column++)
        {
            for (var row = 0; row < ElectronicsState.Rows; row++)
            {
                var chip = state.BUGchip[column][row];
                var isShown = (chip & ElectronicsState.ChipShown) != 0;
                var isFixed = (chip & ElectronicsState.ChipFixed) != 0;
                shown += isShown ? 1 : 0;
                locked += isFixed ? 1 : 0;
                hiddenLocked += isFixed && !isShown ? 1 : 0;
            }
        }
        return (shown, locked, hiddenLocked);
    }

    [Fact]
    public void TheOriginalCutsTheDifficultyDownToThreeAndTheOptionLetsTheHardestLevelReachFour()
    {
        // level, electronics rating (0 lowest): level - rating + 1, between 0 and the cap
        foreach (var (level, rating, original, option) in new[] { (3, 0, 3, 4), (3, 1, 3, 3), (3, 2, 2, 2), (2, 0, 3, 3), (2, 1, 2, 2), (0, 3, 0, 0), (1, 0, 2, 2) })
        {
            Assert.Equal(original, CircuitRules.BoardDifficulty(level, rating));
            Assert.Equal(original, CircuitRules.BoardDifficulty(level, rating, Off));
            Assert.Equal(option, CircuitRules.BoardDifficulty(level, rating, On));
        }
        Assert.False(new GameOptions().CutHardestCircuit);
        Assert.False(OriginalBehaviour.Faithful.CutContent.HardestCircuit);
        Assert.True(new GameOptions(CutHardestCircuit: true).Behaviour.CutContent.HardestCircuit);
    }

    [SkippableFact]
    public void ABoardOfDifficultyFourShowsOnlyTheLastColumnHidesEightFixedChipsAndCanBeWon()
    {
        var game = GameFiles();
        for (var seed = 1; seed <= 60; seed++)
        {
            var four = BuiltBoard(game, 4, seed);
            var (shown, locked, hiddenLocked) = Count(four);
            // only the last column's five chips are shown, and the eight fixed ones are as hidden as the rest
            Assert.Equal(ElectronicsState.Rows, shown);
            Assert.Equal((8, 8), (locked, hiddenLocked));
            // four phones to cut off, as the level asks, and the bounded search finds a way
            Assert.Equal(4, CircuitRules.PhoneTarget(four));
            Assert.True(CircuitSolver.CanBeWon(four), $"seed {seed}");
        }
    }

    [SkippableFact]
    public void ABoardOfDifficultyThreeIsTheOriginalsHardestAndShowsMore()
    {
        var game = GameFiles();
        var (shown, locked, hiddenLocked) = Count(BuiltBoard(game, 3, 1));
        Assert.True(shown > ElectronicsState.Rows, "difficulty 3 shows chips outside the last column");
        Assert.Equal(6, locked);
        Assert.Equal(0, hiddenLocked);
    }
}
