using CovertAction.Core.Shared;
using CovertAction.Persistence;
using CovertAction.Persistence.Layout;

namespace CovertAction.Oracle.Tests.Harness;

/// <summary>
/// A recorded case cannot be replayed in the typed model, whatever the generator's flag says: its memory holds a value
/// the state cannot hold, or it drives a routine beneath a boundary of the C# into overrunning its own tables. The
/// message says which.
/// </summary>
internal sealed class OutsideModelException(string message) : Exception(message);

/// <summary>How the cases of one function went: compared and equal, or left out and why.</summary>
/// <param name="Compared">Cases the C# reproduced: same result, same memory, same calls.</param>
/// <param name="OutsideModel">One line per case outside the typed model that was not held against the C#: the case, the generator flag, and what happened (see <see cref="OracleRun.ForEachCase"/>).</param>
internal readonly record struct OracleTally(int Compared, IReadOnlyList<string> OutsideModel);

/// <summary>
/// Runs an oracle test: for every recorded run of an original function, the test body loads the C# state from the
/// original's memory, calls the C# function, and hands the outcome back here to be compared with what the original
/// left. Without vectors (a checkout without the game-derived build/oracle folder) the test is reported as skipped
/// (the tests are <c>[SkippableFact]</c>s; a <see cref="SkipException"/> skips them while they run).
/// </summary>
internal static class OracleRun
{
    /// <summary>Runs <paramref name="body"/> for every recorded case of a function; skips the test when there is none.</summary>
    /// <remarks>
    /// The generator marks each case. A <em>realistic</em> case is a state of a real session, or a fuzzed state in
    /// which every changed variable stayed inside the range the typed model gives it: the C# must reproduce it,
    /// whatever the original does there, and any difference or exception fails the test. A case that is not
    /// realistic changed something the model takes for granted (a pointer, a byte that is no variable, a count beyond
    /// its table), and the original then reads or writes memory the typed state does not have. Such a case is run as
    /// well, because most of them still agree, but a difference or an exception in it only counts it as outside the
    /// model (ARCHITECTURE.md "Oracle test contract"). The same goes, on any case, for an
    /// <see cref="OutsideModelException"/>, which names a stated reason why the case cannot be replayed, and for a
    /// <see cref="Msc.UninitialisedReadException"/>: the original went on with stack garbage there.
    /// </remarks>
    /// <param name="module">The module in upper case, for example "CODE".</param>
    /// <param name="name">The function's name in symbols/names.txt without the leading underscore.</param>
    /// <param name="body">Replays one case and asserts on it.</param>
    /// <param name="folder">Another vector folder than the default (the harness's own tests use a temporary one).</param>
    /// <returns>How many cases were compared and how many lay outside the typed model.</returns>
    public static OracleTally ForEachCase(string module, string name, Action<OracleCase> body, string? folder = null)
    {
        var vectors = OracleVectors.ForFunction(module, name, folder);
        if (vectors.Count == 0)
        {
            throw new SkipException($"no oracle vectors for {module} {name} under {folder ?? OracleVectors.Folder} (tools/oracle writes them from the player's game)");
        }
        var compared = 0;
        var outside = new List<string>();
        foreach (var vector in vectors)
        {
            foreach (var oracleCase in vector.Cases)
            {
                try
                {
                    body(oracleCase);
                    compared++;
                }
                catch (SkipException)
                {
                    throw;
                }
                catch (Exception e) when (!oracleCase.Realistic || e is OutsideModelException or Msc.UninitialisedReadException)
                {
                    outside.Add($"{oracleCase.Id} ({(oracleCase.Realistic ? "realistic" : "not realistic")}; {oracleCase.Origin}): {e.GetType().Name}: {FirstLine(e.Message)}");
                }
                catch (Exception e)
                {
                    throw new Xunit.Sdk.XunitException($"{module} {name}, vector {vector.Label}, case {oracleCase.Id} ({oracleCase.Origin}): {e.GetType().Name}: {e.Message}", e);
                }
            }
        }
        if (compared == 0)
        {
            throw new SkipException($"{module} {name}: none of the {outside.Count} cases lies inside the typed model: {string.Join(" || ", outside)}");
        }
        return new OracleTally(compared, outside);
    }

    private static string FirstLine(string message) => message.Split((char)10)[0];

    /// <summary>
    /// Compares the memory image the C# leaves with the one the original left. The C# image is the image before the
    /// call with the state's fields stored over it, so every byte the state does not model is as it was before.
    /// </summary>
    /// <remarks>
    /// A difference in a modelled field fails, naming the field. A byte the original changed outside the modelled
    /// fields fails too when <paramref name="unmodelledChangesFail"/> is set: the function wrote a variable the C#
    /// state lacks. Bytes inside <paramref name="ignore"/> are not compared: the stack, which shares the data segment
    /// (the generator's stack mask), and ranges the test names with a reason (working data of routines the C# replaces
    /// by a boundary call).
    /// </remarks>
    /// <typeparam name="TState">The state type of the layout.</typeparam>
    /// <param name="layout">Where the state's fields lie in the image.</param>
    /// <param name="state">The C# state after the function ran.</param>
    /// <param name="before">The original's image before the call.</param>
    /// <param name="after">The original's image after the call.</param>
    /// <param name="ignore">Ranges not compared, start inclusive, end exclusive.</param>
    /// <param name="unmodelledChangesFail">Whether a change outside the modelled fields and the ignored ranges is a failure.</param>
    /// <summary>
    /// Compares the hand-off block a whole part leaves with the original's: its head byte for byte, and behind it the
    /// campaign's tables up to the cursor when the part handed them over (the cursor is 0 when it did not).
    /// </summary>
    /// <param name="oracleCase">The recorded case, with the block before and after.</param>
    /// <param name="shared">The block as the C# leaves it.</param>
    /// <param name="tablesEnd">The part's block cursor after its last write: where the tables end, or 0.</param>
    public static void AssertHandedOverBlock(OracleCase oracleCase, SharedState shared, int tablesEnd)
    {
        var (before, after) = (oracleCase.SharedIn!, oracleCase.SharedOut!);
        var actual = (byte[])before.Clone();
        SharedBlockCodec.Layout.Store(shared, actual);
        var head = Enumerable.Range(0, SharedState.TablesOffset).Where(i => actual[i] != after[i]).ToList();
        Assert.True(head.Count == 0, $"the head of the hand-off block differs from the original's in {head.Count} bytes, the first at +0x{head.FirstOrDefault():x}");
        if (tablesEnd != 0)
        {
            SharedBlockCodec.StoreCampaign(shared, actual);
            var differs = Enumerable.Range(SharedState.TablesOffset, tablesEnd - SharedState.TablesOffset).Where(i => actual[i] != after[i]).ToList();
            Assert.True(differs.Count == 0, $"the hand-off block's tables differ from the original's in {differs.Count} bytes, the first at +0x{differs.FirstOrDefault():x}");
        }
        else
        {
            Assert.True(before.AsSpan(SharedState.TablesOffset).SequenceEqual(after.AsSpan(SharedState.TablesOffset)), "the original changed the hand-off block's tables, the C# had none to hand over");
        }
    }

    /// <summary>
    /// The animation player's words that hold DOS segments and record addresses, which the C# holds as objects
    /// (D-13): pan_segment and script_segment (the loaded and the running program: equal words mean the same
    /// program) and animation_ptr (the player's own request or another one). Compared here when the original
    /// changed them, as AnimationOracleTests does.
    /// </summary>
    /// <returns>Their places, to leave out of the data segment's comparison.</returns>
    public static IReadOnlyList<(int Start, int End)> ComparePlayerWordsByHand(PanEngineLayout.Places at, Core.Animation.PanEngineState pan,
        byte[] before, byte[] after)
    {
        static int Word(byte[] memory, int address) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(memory.AsSpan(address));
        bool Changed(int address) => Word(after, address) != Word(before, address);
        if (Changed(at.PanSegment))
        {
            Assert.True(pan.PanSegment is not null, "pan_segment: the original loaded a program, the C# holds none");
        }
        if (Changed(at.ScriptSegment))
        {
            Assert.True(Word(after, at.ScriptSegment) == Word(after, at.PanSegment) && pan.ScriptSegment is not null
                && ReferenceEquals(pan.ScriptSegment, pan.PanSegment), "script_segment: the original made the loaded program the running one, the C# did not");
        }
        if (Changed(at.AnimationPtr))
        {
            var running = Word(after, at.AnimationPtr);
            Assert.True(pan.AnimationPtr is not null && ReferenceEquals(pan.AnimationPtr, pan.PanParams) == (running == at.PanParams),
                $"animation_ptr: the original left DS:{running:x4}, the C# has another request running");
        }
        return [(at.PanSegment, at.PanSegment + 2), (at.ScriptSegment, at.ScriptSegment + 2), (at.AnimationPtr, at.AnimationPtr + 2)];
    }

    public static void AssertImage<TState>(StateLayout<TState> layout, TState state, byte[] before, byte[] after,
        IReadOnlyList<(int Start, int End)> ignore, bool unmodelledChangesFail)
    {
        Assert.True(before.Length == after.Length, $"{layout.Name}: the images before and after differ in size");
        var actual = (byte[])before.Clone();
        layout.Store(state, actual);

        var differences = new List<string>();
        for (var offset = 0; offset < after.Length; offset++)
        {
            if (actual[offset] == after[offset] || ignore.Any(range => offset >= range.Start && offset < range.End))
            {
                continue;
            }
            if (layout.FieldAt(offset) is { } field)
            {
                differences.Add($"0x{offset:x4} {field.Name}: C# {actual[offset]:x2}, original {after[offset]:x2}");
            }
            else if (unmodelledChangesFail)
            {
                differences.Add($"0x{offset:x4} (not modelled): the original changed {before[offset]:x2} to {after[offset]:x2}");
            }
        }
        Assert.True(differences.Count == 0,
            $"{layout.Name} differs in {differences.Count} bytes:\n  " + string.Join("\n  ", differences.Take(Environment.GetEnvironmentVariable("ORACLE_DIFFS") is { } n ? int.Parse(n) : 12)));
    }
}
