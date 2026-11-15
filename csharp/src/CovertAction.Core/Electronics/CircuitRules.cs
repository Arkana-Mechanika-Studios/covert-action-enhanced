using CovertAction.Core.Shared;

namespace CovertAction.Core.Electronics;

/// <summary>
/// The rules of the electronics minigame that need no screen: how a board is laid out and filled with chips, how the
/// signals run through it, and which terminals become phones and which alarms.
/// </summary>
/// <remarks>
/// The original works these rules out inside the functions that also draw the board (<c>build_circuit_board</c>,
/// BUG 00e8:0395, and <c>bug_status_message</c>, BUG 00e8:09cc); they are split off here as rules of their own, in
/// the order and with the random draws of the original, and those functions call them.
/// </remarks>
public static class CircuitRules
{
    /// <summary>The highest chip type.</summary>
    private const int HighestChipType = ElectronicsState.ChipTypes - 1;

    /// <summary>The last column of chips: its outputs are the terminals.</summary>
    private const int LastColumn = ElectronicsState.Columns - 1;

    /// <summary>The lowest chip type the last column may get: no plain OR gate (0, 1) and no line copied to both outputs (2, 3) next to the terminals.</summary>
    private const int LowestTypeOfLastColumn = 4;

    /// <summary>Board cells from one chip column to the next, and from one chip row to the next: chips are 32 pixels apart.</summary>
    private const int CellsPerChip = 4;

    /// <summary>The board cell column of the first chip column's outputs.</summary>
    private const int FirstOutputCellColumn = 7;

    /// <summary>The board cell row of the first chip row's upper output.</summary>
    private const int FirstOutputCellRow = 1;

    /// <summary>A wire straight across a cell.</summary>
    private const Wire Straight = Wire.West | Wire.East;

    /// <summary>
    /// The board as it starts: every signal not yet worked out, and the supply on the left. The supply powers lines
    /// 0, 3, 4, 7 and 8 (the pattern on, off, off, on, repeated).
    /// </summary>
    /// <param name="state">The board.</param>
    public static void ResetSignals(ElectronicsState state)
    {
        for (var line = 0; line < ElectronicsState.Lines; line++)
        {
            foreach (var row in state.Pin)
            {
                row[line] = ElectronicsState.NoSignal;
            }
            state.Pin[0][line] = (short)(line % 4 is 0 or 3 ? 1 : 0);
        }
    }

    /// <summary>
    /// Lays out the wires between the chip columns at random, until the number of diagonals (see below) lies between
    /// 4 * difficulty and 4 * difficulty + 4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After each chip, its two output lines either run straight on (two chances in three) or cross over each other.
    /// Each line of the wiring is drawn into the 2x2 board cells right of the chip's outputs, and
    /// <see cref="ElectronicsState.Connect"/> says where it leads. When two chips one above the other both run straight,
    /// a diagonal swaps the lower line of the upper chip with the upper line of the lower one (drawn in the cells above);
    /// only these diagonals are counted. One random draw per chip of columns 0..5, row by row, column by column; the
    /// whole layout is drawn again when the count is out of range.
    /// </para>
    /// <para>
    /// Then the two cells of the lowest junction between columns 1 and 2 up to 5 and 6 get a piece of wire going down
    /// (towards what the background picture has below the board).
    /// </para>
    /// </remarks>
    /// <param name="state">The board; its difficulty is read.</param>
    public static void LayOutWires(ElectronicsState state)
    {
        int diagonals;
        do
        {
            diagonals = 0;
            for (var column = 0; column < LastColumn; column++)
            {
                var aboveRunsStraight = false;
                for (var row = 0; row < ElectronicsState.Rows; row++)
                {
                    var (x, y) = OutputCell(column, row, 0);
                    var upper = 2 * row;
                    var lower = upper + 1;
                    var connect = state.Connect[column];
                    // random(3): 0 and 1 run straight, 2 crosses
                    if (GameLibrary.RandomBelow(state.Random, 3) < 2)
                    {
                        SetCells(state, x, y, Straight, Straight, Straight, Straight);
                        connect[upper] = (short)upper;
                        connect[lower] = (short)lower;
                        if (aboveRunsStraight)
                        {
                            diagonals++;
                            // the upper line comes down from the row above, the lower line of the row above goes up
                            // through the three cells above
                            SetCells(state, x, y, Wire.North | Wire.West, Wire.North | Wire.East);
                            SetCells(state, x, y - 1, Wire.NorthEast | Wire.South, Wire.South | Wire.NorthWest);
                            SetCells(state, x, y - 2, Wire.North | Wire.SouthEast, Wire.North | Wire.SouthWest);
                            SetCells(state, x, y - 3, Wire.South | Wire.West, Wire.East | Wire.South);
                            connect[upper] = (short)(upper - 1);
                            connect[upper - 1] = (short)upper;
                        }
                        aboveRunsStraight = true;
                    }
                    else
                    {
                        SetCells(state, x, y, Wire.SouthEast | Wire.West, Wire.East | Wire.SouthWest, Wire.NorthEast | Wire.West, Wire.East | Wire.NorthWest);
                        connect[upper] = (short)lower;
                        connect[lower] = (short)upper;
                        aboveRunsStraight = false;
                    }
                }
            }
        }
        while (diagonals < 4 * state.Diff || diagonals > 4 * state.Diff + 4);

        for (var column = 1; column < LastColumn; column++)
        {
            // the cells of the lower line of the lowest chip row
            var (x, y) = OutputCell(column, ElectronicsState.Rows - 1, 1);
            state.Board[x][y] |= (short)Wire.South;
            state.Board[x + 1][y] |= (short)Wire.South;
        }
    }

    /// <summary>
    /// A random chip type for the difficulty: from the difficulty up to twice the difficulty plus 5, at most 11
    /// (<c>random(diff + 6) + diff</c>). The harder the puzzle, the more of the inverting types 6..11. One random draw.
    /// </summary>
    /// <param name="state">The board; its difficulty and generator are used.</param>
    public static short RandomChipType(ElectronicsState state) =>
        (short)GameLibrary.Clamp(GameLibrary.RandomBelow(state.Random, state.Diff + 6) + state.Diff, 0, HighestChipType);

    /// <summary>
    /// Puts a chip of random type (<see cref="RandomChipType"/>) in every slot, column by column; a chip of the last
    /// column whose type is below 4 is drawn again. The chips have no marks.
    /// </summary>
    /// <param name="state">The board.</param>
    public static void PlaceRandomChips(ElectronicsState state)
    {
        for (var column = 0; column < ElectronicsState.Columns; column++)
        {
            for (var row = 0; row < ElectronicsState.Rows; row++)
            {
                short chip;
                do
                {
                    chip = state.BUGchip[column][row] = RandomChipType(state);
                }
                while (column == LastColumn && chip < LowestTypeOfLastColumn);
            }
        }
    }

    /// <summary>
    /// Works out the signals of the whole board from the supply to the terminals: chip by chip, column by column, each
    /// chip's two outputs from its truth table, each passed on to the line of the next column the wiring leads to.
    /// </summary>
    /// <remarks>
    /// Each output is stored, then <paramref name="changed"/> hears about it if its signal differs from before, then it
    /// is passed on. The upper output is passed on before the lower one is worked out, and the lower one is worked out
    /// from the chip and inputs as they are then: whatever <paramref name="changed"/> did to the board counts, as in
    /// the original (where it can run the clock and, at the last second, work out the whole circuit again).
    /// </remarks>
    /// <param name="state">The board: its chips, wiring and supply are read, its signals written.</param>
    /// <param name="changed">
    /// Called for every output whose signal changed, or null when nothing is to be drawn: the screen lets the current
    /// run along the wire from there (the original's <c>power_wire</c>), and may work the circuit out again itself.
    /// Its arguments are the board cell where the output's wire starts (column 0..39, row 0..24) and the new signal.
    /// </param>
    public static void PropagateSignals(ElectronicsState state, Action<int, int, int>? changed)
    {
        for (var column = 0; column < ElectronicsState.Columns; column++)
        {
            var outputs = state.Pin[2 * column + 1];
            for (var row = 0; row < ElectronicsState.Rows; row++)
            {
                for (var line = 0; line < 2; line++)
                {
                    var signal = 2 * row + line;
                    var before = outputs[signal];
                    outputs[signal] = ChipOutput(state, column, row, line);
                    if (changed is not null && before != outputs[signal])
                    {
                        var (x, y) = OutputCell(column, row, line);
                        changed(x, y, outputs[signal]);
                    }
                    if (column < LastColumn)
                    {
                        state.Pin[2 * column + 2][state.Connect[column][signal]] = outputs[signal];
                    }
                }
            }
        }
    }

    /// <summary>
    /// Decides, after a board has been worked out, which terminals are phones and which alarms: a terminal with current
    /// becomes a phone (the player must cut it off), one without becomes an alarm (it must stay off). Says whether the
    /// board is a fair puzzle: exactly 6 - difficulty / 2 phones, and at most 4 - difficulty pairs of neighbouring
    /// terminals (lines 0 and 1, 2 and 3, ...) that both have current (none at all is always fine).
    /// </summary>
    /// <param name="state">The board; its terminal flags are written.</param>
    /// <returns>Whether the board can be kept.</returns>
    public static bool AssignTerminals(ElectronicsState state)
    {
        var terminals = state.Pin[ElectronicsState.PinRows - 1];
        var powered = 0;
        var poweredInPairs = 0;   // each powered pair is counted once for each of its two lines
        for (var line = 0; line < ElectronicsState.Lines; line++)
        {
            powered += terminals[line];
            state.TerminalFlags[line] = terminals[line] == 0;
            var first = line & ~1;
            if (terminals[first] != 0 && terminals[first + 1] != 0)
            {
                poweredInPairs++;
            }
        }
        var pairs = poweredInPairs / 2;
        return powered == PhoneTapTarget(state) && !(poweredInPairs != 0 && pairs > 4 - state.Diff);
    }

    /// <summary>
    /// Shows the type of the chips the player may see: every chip of the last column, and each other chip when a
    /// random 1..3 reaches the difficulty (all of them at difficulty 0 and 1, two in three at 2, one in three at 3).
    /// One random draw per chip, the last column included.
    /// </summary>
    /// <param name="state">The board.</param>
    public static void RevealChips(ElectronicsState state)
    {
        for (var column = 0; column < ElectronicsState.Columns; column++)
        {
            for (var row = 0; row < ElectronicsState.Rows; row++)
            {
                // C: the draw comes first in the condition, so it is made for the last column too
                if (GameLibrary.RandomBelow(state.Random, 3) + 1 >= state.Diff || column == LastColumn)
                {
                    state.BUGchip[column][row] |= ElectronicsState.ChipShown;
                }
            }
        }
    }

    /// <summary>
    /// Fixes 2 * difficulty chips of columns 0..4 in place, picked at random (a column, then a row, out of the first
    /// six columns: a pick in column 5, or of a chip already fixed, or of a hidden chip of type 0..3, is drawn again). A
    /// fixed chip is also shown.
    /// </summary>
    /// <remarks>
    /// The loop has no way out other than finding enough chips; at difficulty 3 the board always has more than six
    /// candidates.
    /// </remarks>
    /// <param name="state">The board.</param>
    public static void LockChips(ElectronicsState state)
    {
        const int pickedColumns = LastColumn;   // random(6): the last column is never picked
        const int lockedColumnOnlyAtDifficulty4 = 5;
        var locked = 0;
        while (locked < 2 * state.Diff)
        {
            var column = GameLibrary.RandomBelow(state.Random, pickedColumns);
            var row = GameLibrary.RandomBelow(state.Random, ElectronicsState.Rows);
            var chip = state.BUGchip[column][row];
            // BUG: CA-25 "above 3" looks at the whole chip word, marks included: a shown chip passes whatever its
            // type, so only hidden chips of types 0..3 are spared
            // DORMANT: difficulty 4, under which column 5 could be fixed and a fixed chip would stay hidden; the
            // difficulty is limited to 0..3 when the part starts (bug_init)
            if (chip > 3 && (chip & ElectronicsState.ChipFixed) == 0 && (column != lockedColumnOnlyAtDifficulty4 || state.Diff == 4))
            {
                chip |= ElectronicsState.ChipFixed;
                if (state.Diff != 4)
                {
                    chip |= ElectronicsState.ChipShown;
                }
                state.BUGchip[column][row] = chip;
                locked++;
            }
        }
    }

    /// <summary>The board cell where output <paramref name="line"/> (0 upper, 1 lower) of a chip leaves it: right of the chip, in the 2x2 cells of the wiring to the next column.</summary>
    /// <param name="column">The chip's column, 0..6.</param>
    /// <param name="row">The chip's row, 0..4.</param>
    /// <param name="line">0 for the upper output, 1 for the lower.</param>
    public static (int Column, int Row) OutputCell(int column, int row, int line) =>
        (CellsPerChip * column + FirstOutputCellColumn, CellsPerChip * row + FirstOutputCellRow + line);

    /// <summary>
    /// The phones the player must have cut off at once to finish the board: all the phones of a wiretap board
    /// (6 less half the difficulty), 5 of the car tracer's (20 per cent each).
    /// </summary>
    /// <param name="state">The board; its mode and difficulty are read.</param>
    public static int PhoneTarget(ElectronicsState state)
    {
        const int tracerPhones = 5;
        return state.Mode == ElectronicsMode.PhoneTaps ? PhoneTapTarget(state) : tracerPhones;
    }

    /// <summary>The number of phones a wiretap board has: 6 less half the difficulty.</summary>
    // C: written -(diff / 2 - 6); the division rounds towards zero
    private static int PhoneTapTarget(ElectronicsState state) => -(state.Diff / 2 - 6);

    /// <summary>The signal a chip puts on one of its output lines, from its type and the two signals coming in.</summary>
    /// <exception cref="IndexOutOfRangeException">A chip type above 11 or an input not yet worked out: the original reads past its tables there.</exception>
    private static short ChipOutput(ElectronicsState state, int column, int row, int line)
    {
        var type = state.BUGchip[column][row] & ElectronicsState.ChipTypeMask;
        var inputs = state.Pin[2 * column];
        var upper = inputs[2 * row];
        var lower = inputs[2 * row + 1];
        return state.TruthTables[type][2 * (2 * lower + upper) + line];
    }

    /// <summary>Writes a row of two cells (left, right) and, if given, the row below them.</summary>
    private static void SetCells(ElectronicsState state, int x, int y, Wire left, Wire right, Wire? belowLeft = null, Wire? belowRight = null)
    {
        state.Board[x][y] = (short)left;
        state.Board[x + 1][y] = (short)right;
        if (belowLeft is { } bl && belowRight is { } br)
        {
            state.Board[x][y + 1] = (short)bl;
            state.Board[x + 1][y + 1] = (short)br;
        }
    }
}
