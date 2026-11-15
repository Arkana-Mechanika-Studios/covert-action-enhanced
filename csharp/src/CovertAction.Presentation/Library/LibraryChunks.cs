using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Library;

/// <summary>
/// Handing one table of the campaign on: the two routines through which every part writes and reads the twelve
/// tables, one call per table. They go to a save file, or, when the campaign is handed to the next part, into the
/// hand-off block, where a cursor says how far the tables have got.
/// </summary>
/// <remarks>
/// The hand-off block's tables are typed records in the rewrite (<see cref="SharedState.Campaign"/>), which the
/// caller fills or reads as a whole; for a hand-off these routines therefore only move the cursor on, as the
/// original does beside copying the bytes.
/// </remarks>
/// <param name="state">The part's place in the hand-off block.</param>
/// <param name="world">The drivers and the files.</param>
public sealed class LibraryChunks(IHandOffCursor state, Boundaries world)
{
    /// <summary>
    /// Hands one table on: into the open save file, or, for a hand-off, to the hand-off block (whose tables the
    /// caller has filled as records: here only the place in the block moves on).
    /// </summary>
    /// <param name="handle">The save file's number.</param>
    /// <param name="table">The table's bytes.</param>
    // orig: TAC 00e8:cb07 write_chunk (src/c/tac/tac_00e8_cb07.c) matched; also GAME 00f7:ae09, BUG 00e8:68d3, FINAL 0100:a62b; tests: HandOffOracleTests, GameOracleTests
    public void WriteChunk(int handle, ReadOnlySpan<byte> table)
    {
        if (state.SharedCursor != 0)
        {
            state.SharedCursor += (short)table.Length;
        }
        else
        {
            world.Files.Write(handle, table);
        }
    }

    /// <summary>
    /// Takes one table over: from the open save file, or, for a hand-off, from the hand-off block (whose tables the
    /// caller reads as records: here only the place in the block moves on, and <paramref name="table"/> stays as it is).
    /// </summary>
    /// <param name="handle">The save file's number.</param>
    /// <param name="table">Receives the table's bytes; a file that ends early leaves the rest as it was.</param>
    // orig: TAC 00e8:cb60 read_chunk (src/c/tac/tac_00e8_cb60.c) matched; also GAME 00f7:ae62, BUG 00e8:692c, FINAL 0100:a684; tests: HandOffOracleTests
    public void ReadChunk(int handle, Span<byte> table)
    {
        if (state.SharedCursor != 0)
        {
            state.SharedCursor += (short)table.Length;
        }
        else
        {
            world.Files.Read(handle, table);
        }
    }
}
