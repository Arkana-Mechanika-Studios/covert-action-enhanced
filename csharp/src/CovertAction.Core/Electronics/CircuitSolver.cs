namespace CovertAction.Core.Electronics;

/// <summary>
/// A bounded search for a winning series of swaps on a freshly built board (the player's option of CA-62, D-650). The
/// original never checks that a board can be won; a rare one cannot. The search is what a player does: swap the carried
/// chip into a free slot of columns 0..5, never lighting an alarm at any step, until enough phones are dead.
/// </summary>
/// <remarks>
/// It is best-first on the number of phones still live and gives up after a fixed number of boards looked at, so a board
/// it cannot settle counts as lost and is drawn again: cheap, and a board it rejects is never a board the original
/// would have been fairer with. It reads the board and changes nothing in it.
/// </remarks>
public static class CircuitSolver
{
    /// <summary>The boards a search looks at before it gives up.</summary>
    public const int NodeLimit = 4000;

    private const int Slots = (ElectronicsState.Columns - 1) * ElectronicsState.Rows;

    /// <summary>One position of the search: the chips of columns 0..5 (type, row by row within a column), the chip in hand.</summary>
    private sealed record Node(byte[] Chips, byte Carried);

    /// <summary>Whether a series of swaps is found that cuts off enough phones without ever lighting an alarm.</summary>
    /// <param name="state">The board as built: chips, fixed marks, wiring, supply on <c>Pin[0]</c>, terminal flags and the chip the player carries.</param>
    /// <param name="nodeLimit">The boards to look at at most.</param>
    /// <returns>True when a win is found within the limit (a board already won counts).</returns>
    public static bool CanBeWon(ElectronicsState state, int nodeLimit = NodeLimit)
    {
        var target = CircuitRules.PhoneTarget(state);
        var free = new bool[Slots];
        var start = new byte[Slots];
        for (var column = 0; column < ElectronicsState.Columns - 1; column++)
        {
            for (var row = 0; row < ElectronicsState.Rows; row++)
            {
                var chip = state.BUGchip[column][row];
                var slot = column * ElectronicsState.Rows + row;
                start[slot] = (byte)(chip & ElectronicsState.ChipTypeMask);
                free[slot] = (chip & ElectronicsState.ChipFixed) == 0;
            }
        }
        var queue = new PriorityQueue<Node, int>();
        var seen = new HashSet<(ulong, ulong)>();
        var first = new Node(start, (byte)(state.CarriedChip & ElectronicsState.ChipTypeMask));
        seen.Add(Key(first));
        queue.Enqueue(first, 0);
        var looked = 0;
        while (queue.Count > 0 && looked++ < nodeLimit)
        {
            var node = queue.Dequeue();
            for (var slot = 0; slot < Slots; slot++)
            {
                if (!free[slot] || node.Chips[slot] == node.Carried)
                {
                    continue;
                }
                var chips = (byte[])node.Chips.Clone();
                chips[slot] = node.Carried;
                var next = new Node(chips, node.Chips[slot]);
                if (!seen.Add(Key(next)))
                {
                    continue;
                }
                var (deadPhones, livePhones, liveAlarms) = Evaluate(state, next);
                if (liveAlarms != 0)
                {
                    continue;
                }
                if (deadPhones >= target)
                {
                    return true;
                }
                queue.Enqueue(next, livePhones);
            }
        }
        return target <= 0;
    }

    /// <summary>The phones dead, the phones live and the alarms live at the terminals for an arrangement.</summary>
    private static (int DeadPhones, int LivePhones, int LiveAlarms) Evaluate(ElectronicsState state, Node node)
    {
        Span<short> signal = stackalloc short[ElectronicsState.Lines];
        Span<short> next = stackalloc short[ElectronicsState.Lines];
        for (var line = 0; line < ElectronicsState.Lines; line++)
        {
            signal[line] = state.Pin[0][line];
        }
        for (var column = 0; column < ElectronicsState.Columns; column++)
        {
            for (var row = 0; row < ElectronicsState.Rows; row++)
            {
                var type = column < ElectronicsState.Columns - 1
                    ? node.Chips[column * ElectronicsState.Rows + row]
                    : state.BUGchip[column][row] & ElectronicsState.ChipTypeMask;
                var table = state.TruthTables[type];
                var index = 2 * (2 * signal[2 * row + 1] + signal[2 * row]);
                for (var line = 0; line < 2; line++)
                {
                    var output = table[index + line];
                    if (column < ElectronicsState.Columns - 1)
                    {
                        next[state.Connect[column][2 * row + line]] = output;
                    }
                    else
                    {
                        next[2 * row + line] = output;
                    }
                }
            }
            next.CopyTo(signal);
        }
        int dead = 0, live = 0, alarms = 0;
        for (var line = 0; line < ElectronicsState.Lines; line++)
        {
            if (state.TerminalFlags[line])
            {
                alarms += signal[line] != 0 ? 1 : 0;
            }
            else if (signal[line] != 0)
            {
                live++;
            }
            else
            {
                dead++;
            }
        }
        return (dead, live, alarms);
    }

    /// <summary>A key of an arrangement: the 31 chip types, four bits each, over two 64-bit words.</summary>
    private static (ulong, ulong) Key(Node node)
    {
        ulong low = node.Carried, high = 0;
        for (var slot = 0; slot < Slots; slot++)
        {
            if (slot < 15)
            {
                low = low << 4 | node.Chips[slot];
            }
            else
            {
                high = high << 4 | node.Chips[slot];
            }
        }
        return (low, high);
    }
}
