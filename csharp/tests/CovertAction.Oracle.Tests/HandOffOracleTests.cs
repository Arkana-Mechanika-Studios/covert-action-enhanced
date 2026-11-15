using System.Text;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Electronics;
using CovertAction.Presentation.Final;
using CovertAction.Presentation.Library;
using CovertAction.Presentation.Raid;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The library's hand-off routines (<see cref="LibraryHandOff"/>, <see cref="LibraryChunks"/>) against the recorded
/// runs of every program that carries a copy: the menus part (FINAL), the building raid (TAC) and the electronics
/// part (BUG), and for the table routines the campaign part (GAME) too. Each copy is called as the original was, from
/// the data segment and the hand-off block it had; the data segment and the hand-off block afterwards and every
/// file call (the name opened, every byte written) must be the original's. Skipped without vectors.
/// </summary>
public sealed class HandOffOracleTests(ITestOutputHelper output)
{
    /// <summary>The linear address of the BIOS keyboard flags, and their Num Lock bit (the raid's message box looks at it).</summary>
    private const int KeyboardFlags = 0x417, NumLockBit = 0x20;

    /// <summary>One recorded case set up for replay on a program's state.</summary>
    /// <param name="World">The recorded outside world.</param>
    /// <param name="Shared">The hand-off block as the case had it.</param>
    /// <param name="Cursor">The state's place in the hand-off block.</param>
    /// <param name="HandOff">The library's routines on the program's state; null for the campaign part, which has other code for them.</param>
    /// <param name="Chunks">The table routines on the program's state.</param>
    /// <param name="AssertImage">Compares the data segment the C# leaves with the original's, but for the ranges given (compared by hand).</param>
    private sealed record Replay(RecordingBoundaries World, SharedState Shared, IHandOffCursor Cursor, LibraryHandOff? HandOff, LibraryChunks Chunks,
        Action<IEnumerable<(int Start, int End)>> AssertImage);

    /// <summary>Sets a case up: the program's state from its data segment, the hand-off block, the recorded calls.</summary>
    /// <param name="messageBox">The program's own message box for the "File Error" text; null for a program without the env-state routines.</param>
    private static Replay Begin<TState>(OracleCase oracleCase, StateLayout<TState> layout, IReadOnlyList<(int Start, int End)> scratch,
        Func<TState, ImageRegister> images, Func<TState, SharedState, Boundaries, Action<TextBuffer, int, int>>? messageBox)
        where TState : class, new()
    {
        if (oracleCase.SharedIn is not { } block)
        {
            throw new OutsideModelException("the case has no hand-off block");
        }
        if (layout.OutsideModel(oracleCase.DgroupIn) is { Count: > 0 } reasons)
        {
            throw new OutsideModelException(string.Join("; ", reasons.Take(4)));
        }
        var state = new TState();
        layout.Load(oracleCase.DgroupIn, state);
        var shared = new SharedState();
        SharedBlockCodec.Layout.Load(block, shared);
        try
        {
            SharedBlockCodec.LoadCampaign(block, shared);
        }
        catch (ArgumentException)
        {
            // counts that do not fit the block: a routine that takes the tables over says so itself
        }
        var recorded = new RecordingBoundaries(oracleCase.Calls, o => o is byte[] bytes ? layout.AddressOf(state, bytes) : layout.AddressOf(state, o), images(state));
        if (oracleCase.FarIn.TryGetValue(KeyboardFlags, out var flags))
        {
            recorded.NumLockOn = (flags[0] & NumLockBit) != 0;
        }
        var world = new Boundaries(recorded, recorded, recorded, recorded, recorded, recorded);
        var copy = state as ICampaignCopy;
        var cursor = copy is null ? (IHandOffCursor)state : copy.HandOff;
        return new Replay(recorded, shared, cursor,
            copy is null || messageBox is null ? null : new LibraryHandOff(copy, shared, world, messageBox(state, shared, world)),
            new LibraryChunks(cursor, world),
            byHand => OracleRun.AssertImage(layout, state, oracleCase.DgroupIn, oracleCase.DgroupOut, [.. oracleCase.Ignore, .. scratch, .. byHand], oracleCase.IgnoreDeclared));
    }

    /// <summary>The far pointer GAME's table routines move bytes through: working memory of one call.</summary>
    // orig: GAME DS:55ae (the twin of TAC DS:7364 shared_ptr)
    private static readonly (int Start, int End) GameChunkPointer = (0x55ae, 0x55b2);

    /// <summary>How a case of each program is set up.</summary>
    private static readonly Dictionary<string, Func<OracleCase, Replay>> Parts = new()
    {
        ["FINAL"] = c => Begin(c, FinalDgroup.Layout, [.. LibraryLayout.FinalScratch, .. CampaignCopyLayout.FinalScratch], s => s.Images,
            (state, shared, world) =>
            {
                var part = new Headquarters(state, shared, world, OriginalBehaviour.Faithful);
                return (text, x, y) => part.MenuChoice(text, x, y);
            }),
        ["TAC"] = c => Begin(c, TacDgroup.Layout, [.. TacDgroup.Scratch, .. LibraryLayout.TacScratch, .. CampaignCopyLayout.TacScratch], s => s.Images,
            (state, shared, world) =>
            {
                var part = new BuildingRaid(state, shared, world, OriginalBehaviour.Faithful);
                return (text, x, y) => part.ShowMessageBox(text, x, y);
            }),
        ["BUG"] = c => Begin(c, BugDgroup.Layout, [.. BugDgroup.Scratch, .. CampaignCopyLayout.BugScratch], s => s.Images,
            (state, shared, world) =>
            {
                var part = new ElectronicsWorkbench(state, shared, world, OriginalBehaviour.Faithful);
                return (text, x, y) => part.MessageBox(text.Text, x, y);
            }),
        ["GAME"] = c => Begin<CampaignState>(c, GameDgroup.Layout, [.. LibraryLayout.GameScratch, GameChunkPointer], s => s.Images, null),
    };

    /// <summary>The programs with a copy of write_env_state and read_env_state.</summary>
    public static IEnumerable<object[]> EnvStateCopies => [["FINAL"], ["TAC"], ["BUG"]];

    /// <summary>The copies of write_chunk, by the name each program gives it.</summary>
    public static IEnumerable<object[]> WriteChunkCopies => [["FINAL", "write_state_block"], ["TAC", "write_chunk"], ["BUG", "write_chunk"], ["GAME", "write_table"]];

    /// <summary>The copies of read_chunk, by the name each program gives it.</summary>
    public static IEnumerable<object[]> ReadChunkCopies => [["FINAL", "read_state_block"], ["TAC", "read_chunk"], ["BUG", "read_chunk"], ["GAME", "read_table"]];

    private static string TextAt(byte[] dgroup, int address)
    {
        var end = Array.IndexOf(dgroup, (byte)0, address);
        return Encoding.Latin1.GetString(dgroup, address, (end < 0 ? dgroup.Length : end) - address);
    }

    private void Report(string module, string name, OracleTally tally)
    {
        output.WriteLine($"TALLY {module} {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {module} {name} {line}");
        }
    }

    /// <summary>
    /// Compares the hand-off block the C# leaves with the original's: its head byte for byte, and behind it either
    /// the tables the C# handed over (up to <paramref name="tablesEnd"/>) or, when none were, the original's own
    /// unchanged bytes.
    /// </summary>
    private static void AssertSharedBlock(OracleCase oracleCase, SharedState shared, int? tablesEnd)
    {
        var (before, after) = (oracleCase.SharedIn!, oracleCase.SharedOut!);
        var actual = (byte[])before.Clone();
        SharedBlockCodec.Layout.Store(shared, actual);
        var head = Enumerable.Range(0, SharedState.TablesOffset).Where(i => actual[i] != after[i]).ToList();
        Assert.True(head.Count == 0, $"the head of the hand-off block differs from the original's in {head.Count} bytes, the first at +0x{head.FirstOrDefault():x}");
        if (tablesEnd is { } end)
        {
            SharedBlockCodec.StoreCampaign(shared, actual);
            Assert.True(end == SharedState.TablesOffset + CampaignTablesCodec.Size(shared.Campaign.Cast.Count, shared.Campaign.Events.Count), "the tables end elsewhere than the cursor says");
            var differs = Enumerable.Range(SharedState.TablesOffset, end - SharedState.TablesOffset).Where(i => actual[i] != after[i]).ToList();
            Assert.True(differs.Count == 0, $"the hand-off block's tables differ from the original's in {differs.Count} bytes, the first at +0x{differs.FirstOrDefault():x}");
        }
        else
        {
            Assert.True(before.AsSpan(SharedState.TablesOffset).SequenceEqual(after.AsSpan(SharedState.TablesOffset)), "the original changed the hand-off block's tables, the C# had none to hand over");
        }
    }

    [SkippableTheory]
    [MemberData(nameof(EnvStateCopies))]
    public void WriteEnvStateIsTheOriginals(string module)
    {
        var tally = OracleRun.ForEachCase(module, "write_env_state", c =>
        {
            var replay = Parts[module](c);
            replay.HandOff!.WriteEnvState(TextAt(c.DgroupIn, (ushort)c.Args[0]));
            replay.World.AssertAllCallsMade();
            replay.AssertImage([]);
            AssertSharedBlock(c, replay.Shared, replay.Cursor.SharedCursor == 0 ? null : (ushort)replay.Cursor.SharedCursor);
        });
        Report(module, "write_env_state", tally);
    }

    [SkippableTheory]
    [MemberData(nameof(EnvStateCopies))]
    public void ReadEnvStateIsTheOriginals(string module)
    {
        var tally = OracleRun.ForEachCase(module, "read_env_state", c =>
        {
            var replay = Parts[module](c);
            replay.HandOff!.ReadEnvState(TextAt(c.DgroupIn, (ushort)c.Args[0]));
            replay.World.AssertAllCallsMade();
            replay.AssertImage([]);
            AssertSharedBlock(c, replay.Shared, null);
        });
        Report(module, "read_env_state", tally);
    }

    /// <summary>The bytes a table routine is handed: its second argument is their address, its third their number.</summary>
    private static (int Address, int Length) TableOf(OracleCase oracleCase)
    {
        var (address, length) = ((ushort)oracleCase.Args[1], (ushort)oracleCase.Args[2]);
        if (address + length > oracleCase.DgroupIn.Length)
        {
            throw new OutsideModelException("a table that runs past the end of the data segment");
        }
        return (address, length);
    }

    [SkippableTheory]
    [MemberData(nameof(WriteChunkCopies))]
    public void WriteChunkIsTheOriginals(string module, string name)
    {
        var tally = OracleRun.ForEachCase(module, name, c =>
        {
            var replay = Parts[module](c);
            var (address, length) = TableOf(c);
            var before = (ushort)replay.Cursor.SharedCursor;
            replay.Chunks.WriteChunk(c.Args[0], c.DgroupIn.AsSpan(address, length));
            replay.World.AssertAllCallsMade();
            replay.AssertImage([]);
            if (before != 0)
            {
                // the typed hand-off is filled by the caller as records; here the original's copy is checked by itself
                Assert.True(before + length <= SharedState.Size, "the table runs past the hand-off block (docs/original-bugs.md CA-6)");
                Assert.True(c.SharedOut.AsSpan(before, length).SequenceEqual(c.DgroupIn.AsSpan(address, length)), "the original put other bytes into the hand-off block");
                Assert.True((ushort)replay.Cursor.SharedCursor == before + length, "the place in the hand-off block did not move on by the table's length");
            }
            else
            {
                Assert.True(c.SharedIn.AsSpan().SequenceEqual(c.SharedOut), "a table written to a file changed the hand-off block in the original");
            }
        });
        Report(module, name, tally);
    }

    [SkippableTheory]
    [MemberData(nameof(ReadChunkCopies))]
    public void ReadChunkIsTheOriginals(string module, string name)
    {
        var tally = OracleRun.ForEachCase(module, name, c =>
        {
            var replay = Parts[module](c);
            var (address, length) = TableOf(c);
            var before = (ushort)replay.Cursor.SharedCursor;
            var table = c.DgroupIn.AsSpan(address, length).ToArray();
            replay.Chunks.ReadChunk(c.Args[0], table);
            replay.World.AssertAllCallsMade();
            // the table itself is the caller's: compared here, by hand
            replay.AssertImage([(address, address + length)]);
            if (before != 0)
            {
                // the typed hand-off is read by the caller as records; here the original's copy is checked by itself
                Assert.True(before + length <= SharedState.Size, "the table runs past the hand-off block");
                Assert.True(c.DgroupOut.AsSpan(address, length).SequenceEqual(c.SharedIn.AsSpan(before, length)), "the original took other bytes out of the hand-off block");
                Assert.True((ushort)replay.Cursor.SharedCursor == before + length, "the place in the hand-off block did not move on by the table's length");
            }
            else
            {
                Assert.True(c.DgroupOut.AsSpan(address, length).SequenceEqual(table), "the bytes read from the file are not the original's");
            }
            Assert.True(c.SharedIn.AsSpan().SequenceEqual(c.SharedOut), "reading a table changed the hand-off block in the original");
        });
        Report(module, name, tally);
    }
}
