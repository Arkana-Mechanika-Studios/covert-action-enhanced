using CovertAction.Core.Electronics;

namespace CovertAction.Core.Tests.Electronics;

/// <summary>
/// The circuit rules of the electronics part on hand-made boards, with expected values worked out from the C of
/// build_circuit_board (BUG 00e8:0395) and bug_status_message (BUG 00e8:09cc). The truth tables are
/// built from the twelve chip operations, not taken from the game.
/// </summary>
public sealed class CircuitRulesTests
{
    /// <summary>The twelve chip operations: upper and lower output for upper input a, lower input b.</summary>
    private static readonly Func<int, int, (int Top, int Bottom)>[] Operations =
    [
        (a, b) => (a | b, 0),
        (a, b) => (0, a | b),
        (a, _) => (a, a),
        (_, b) => (b, b),
        (a, b) => (a, b),
        (a, b) => (b, a),
        (a, b) => (1 - a, b),
        (a, b) => (a, 1 - b),
        (a, b) => (1 - a, 1 - b),
        (a, b) => (b, 1 - a),
        (a, b) => (1 - b, a),
        (a, b) => (1 - b, 1 - a),
    ];

    private const int PassThrough = 4;
    private const int InvertBoth = 8;

    /// <summary>A board with the truth tables filled in, every chip of one type, straight wiring and the supply set.</summary>
    private static ElectronicsState Board(int chipType = PassThrough, int seed = 1)
    {
        var state = new ElectronicsState();
        state.Random.Srand(seed);
        for (var type = 0; type < ElectronicsState.ChipTypes; type++)
        {
            for (var lower = 0; lower < 2; lower++)
            {
                for (var upper = 0; upper < 2; upper++)
                {
                    var (top, bottom) = Operations[type](upper, lower);
                    state.TruthTables[type][2 * (2 * lower + upper)] = (short)top;
                    state.TruthTables[type][2 * (2 * lower + upper) + 1] = (short)bottom;
                }
            }
        }
        for (var column = 0; column < ElectronicsState.Columns; column++)
        {
            Array.Fill(state.BUGchip[column], (short)chipType);
            for (var line = 0; line < ElectronicsState.Lines; line++)
            {
                state.Connect[column][line] = (short)line;
            }
        }
        CircuitRules.ResetSignals(state);
        return state;
    }

    private static short[] Terminals(ElectronicsState state) => state.Pin[ElectronicsState.PinRows - 1];

    [Fact]
    public void TheSupplyPowersLinesZeroThreeFourSevenAndEight()
    {
        var state = new ElectronicsState();
        CircuitRules.ResetSignals(state);
        Assert.Equal(new short[] { 1, 0, 0, 1, 1, 0, 0, 1, 1, 0 }, state.Pin[0]);
        Assert.All(state.Pin.Skip(1), row => Assert.All(row, signal => Assert.Equal(ElectronicsState.NoSignal, signal)));
    }

    [Fact]
    public void ChipsThatPassTheirInputsOnLeaveTheSupplyAtTheTerminals()
    {
        var state = Board(PassThrough);
        CircuitRules.PropagateSignals(state, null);
        Assert.Equal(state.Pin[0], Terminals(state));
    }

    [Fact]
    public void SevenColumnsOfInvertersInvertTheSupply()
    {
        var state = Board(InvertBoth);
        CircuitRules.PropagateSignals(state, null);
        Assert.Equal(new short[] { 0, 1, 1, 0, 0, 1, 1, 0, 0, 1 }, Terminals(state));
    }

    [Fact]
    public void TheWiringCarriesAnOutputToTheLineItLeadsTo()
    {
        var state = Board(PassThrough);
        // column 0 crosses the lines of its first chip: 0 goes to 1 and 1 to 0
        state.Connect[0][0] = 1;
        state.Connect[0][1] = 0;
        CircuitRules.PropagateSignals(state, null);
        Assert.Equal(new short[] { 0, 1 }, state.Pin[2][..2]);
        Assert.Equal(new short[] { 0, 1, 0, 1, 1, 0, 0, 1, 1, 0 }, Terminals(state));
    }

    [Fact]
    public void EveryChangedOutputIsReportedWithTheCellItsWireStartsIn()
    {
        var state = Board(PassThrough);
        var reported = new List<(int, int, int)>();
        CircuitRules.PropagateSignals(state, (x, y, signal) => reported.Add((x, y, signal)));
        // every output was "not worked out" before: all 70 are reported, the first ones those of chip (0, 0)
        Assert.Equal(70, reported.Count);
        Assert.Equal((7, 1, 1), reported[0]);
        Assert.Equal((7, 2, 0), reported[1]);
        Assert.Equal((31, 18, 0), reported[^1]);

        // a second evaluation of the same board changes nothing
        reported.Clear();
        CircuitRules.PropagateSignals(state, (x, y, signal) => reported.Add((x, y, signal)));
        Assert.Empty(reported);
    }

    [Fact]
    public void TheLowerOutputIsWorkedOutAfterWhatTheReportOfTheUpperOneDid()
    {
        var state = Board(PassThrough);
        // the report of chip (0, 0)'s upper output changes that chip into one that copies the upper input to both lines
        CircuitRules.PropagateSignals(state, (x, y, _) =>
        {
            if ((x, y) == CircuitRules.OutputCell(0, 0, 0))
            {
                state.BUGchip[0][0] = 2;
            }
        });
        Assert.Equal(new short[] { 1, 1 }, state.Pin[1][..2]);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 6)]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    public void ABoardIsKeptWithExactlySixLessHalfTheDifficultyPhones(int difficulty, int phones)
    {
        var state = new ElectronicsState { Diff = (short)difficulty };
        // the even lines first, so that five phones make no powered pair and six make one (lines 0 and 1)
        int[] order = [0, 2, 4, 6, 8, 1, 3, 5, 7, 9];
        var terminals = Terminals(state);
        for (var count = 0; count <= ElectronicsState.Lines; count++)
        {
            Array.Clear(terminals);
            foreach (var line in order[..count])
            {
                terminals[line] = 1;
            }
            Assert.Equal(count == phones, CircuitRules.AssignTerminals(state));
        }
    }

    [Fact]
    public void PoweredTerminalsBecomePhonesAndTheOthersAlarms()
    {
        var state = new ElectronicsState { Diff = 0 };
        short[] signals = [1, 1, 0, 1, 1, 0, 1, 0, 0, 1];
        signals.CopyTo(Terminals(state), 0);
        Assert.True(CircuitRules.AssignTerminals(state));
        Assert.Equal(signals.Select(s => s == 0), state.TerminalFlags);
    }

    [Theory]
    [InlineData(3, 1, true)]     // one pair of powered neighbours: allowed up to 4 - 3 = 1
    [InlineData(3, 2, false)]
    [InlineData(1, 3, true)]
    public void TooManyPoweredPairsMakeTheBoardUnfair(int difficulty, int pairs, bool kept)
    {
        var state = new ElectronicsState { Diff = (short)difficulty };
        var terminals = Terminals(state);
        // pairs of neighbours both powered first, then single powered lines up to 5 phones
        var phones = 0;
        for (var pair = 0; pair < pairs; pair++)
        {
            terminals[2 * pair] = terminals[2 * pair + 1] = 1;
            phones += 2;
        }
        for (var pair = pairs; phones < 6 - difficulty / 2; pair++)
        {
            terminals[2 * pair] = 1;
            phones++;
        }
        Assert.Equal(kept, CircuitRules.AssignTerminals(state));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 7)]
    [InlineData(2, 99)]
    [InlineData(3, 1990)]
    public void TheWiringIsAPermutationWithTheRightNumberOfDiagonals(int difficulty, int seed)
    {
        var state = Board(seed: seed);
        state.Diff = (short)difficulty;
        CircuitRules.LayOutWires(state);
        var diagonals = 0;
        for (var column = 0; column < ElectronicsState.Columns - 1; column++)
        {
            Assert.Equal(Enumerable.Range(0, ElectronicsState.Lines), state.Connect[column].Select(l => (int)l).Order());
            for (var row = 1; row < ElectronicsState.Rows; row++)
            {
                // a diagonal joins the lower line of the chip above with the upper line of this one
                if (state.Connect[column][2 * row] == 2 * row - 1)
                {
                    diagonals++;
                    var (x, y) = CircuitRules.OutputCell(column, row, 0);
                    Assert.Equal((short)(Wire.North | Wire.West), state.Board[x][y]);
                    Assert.Equal((short)(Wire.East | Wire.South), state.Board[x + 1][y - 3]);
                }
            }
        }
        Assert.InRange(diagonals, 4 * difficulty, 4 * difficulty + 4);
        for (var column = 1; column < ElectronicsState.Columns - 1; column++)
        {
            var (x, y) = CircuitRules.OutputCell(column, ElectronicsState.Rows - 1, 1);
            Assert.True((state.Board[x][y] & (short)Wire.South) != 0);
            Assert.True((state.Board[x + 1][y] & (short)Wire.South) != 0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ChipsAreOfTheDifficultysRangeAndTheLastColumnHasNoSimpleGates(int difficulty)
    {
        var state = Board(seed: 5);
        state.Diff = (short)difficulty;
        for (var round = 0; round < 50; round++)
        {
            CircuitRules.PlaceRandomChips(state);
            Assert.All(state.BUGchip.SelectMany(c => c), chip => Assert.InRange(chip, difficulty, Math.Min(2 * difficulty + 5, 11)));
            Assert.All(state.BUGchip[ElectronicsState.Columns - 1], chip => Assert.True(chip >= 4));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TwiceTheDifficultyChipsOfTheFirstFiveColumnsAreFixedAndShown(int difficulty)
    {
        for (var seed = 1; seed < 40; seed++)
        {
            var state = Board(seed: seed);
            state.Diff = (short)difficulty;
            CircuitRules.PlaceRandomChips(state);
            CircuitRules.RevealChips(state);
            var before = state.BUGchip.Select(c => (short[])c.Clone()).ToArray();
            CircuitRules.LockChips(state);

            var fixedChips = 0;
            for (var column = 0; column < ElectronicsState.Columns; column++)
            {
                for (var row = 0; row < ElectronicsState.Rows; row++)
                {
                    var chip = state.BUGchip[column][row];
                    if ((chip & ElectronicsState.ChipFixed) == 0)
                    {
                        Assert.Equal(before[column][row], chip);
                        continue;
                    }
                    fixedChips++;
                    Assert.True(column < 5);
                    Assert.True((chip & ElectronicsState.ChipShown) != 0);
                    // the test is "word above 3": a hidden chip of type 0..3 is never fixed
                    Assert.True(before[column][row] > 3);
                }
            }
            Assert.Equal(2 * difficulty, fixedChips);
        }
    }

    [Fact]
    public void AtDifficultyZeroAndOneEveryChipIsShown()
    {
        var state = Board(seed: 3);
        state.Diff = 1;
        CircuitRules.RevealChips(state);
        Assert.All(state.BUGchip.SelectMany(c => c), chip => Assert.True((chip & ElectronicsState.ChipShown) != 0));
    }

    [Fact]
    public void AnOutputsWireStartsRightOfItsChip()
    {
        Assert.Equal((7, 1), CircuitRules.OutputCell(0, 0, 0));
        Assert.Equal((7, 2), CircuitRules.OutputCell(0, 0, 1));
        Assert.Equal((31, 18), CircuitRules.OutputCell(6, 4, 1));
    }

    [Theory]
    [InlineData(ElectronicsMode.PhoneTaps, 0, 6)]      // -(0 / 2 - 6)
    [InlineData(ElectronicsMode.PhoneTaps, 1, 6)]      // 1 / 2 rounds to 0
    [InlineData(ElectronicsMode.PhoneTaps, 3, 5)]
    [InlineData(ElectronicsMode.CarTracer, 3, 5)]      // the tracer: always five phones
    [InlineData(ElectronicsMode.CarTracer, 0, 5)]
    public void TheBoardIsDoneWhenAllItsPhonesOrFiveTracerPhonesAreDead(ElectronicsMode mode, int difficulty, int phones)
    {
        var state = new ElectronicsState { Mode = mode, Diff = (short)difficulty };
        Assert.Equal(phones, CircuitRules.PhoneTarget(state));
    }
}
