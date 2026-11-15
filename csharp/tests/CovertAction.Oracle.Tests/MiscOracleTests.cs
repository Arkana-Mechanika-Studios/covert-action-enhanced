using System.Text.Json;
using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Oracle.Tests.Harness;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The game's keyboard (<see cref="KeyboardDriver"/>) against the original's input driver MISC.EXE: for every
/// recorded entry, the keyboard holds what the PC's BIOS told the original, and the C# must give the same answer.
/// Skipped without vectors (build/oracle/MISC).
/// </summary>
public sealed class MiscOracleTests(ITestOutputHelper output)
{
    private const int NoKey = 0xffff;

    /// <summary>The cases of one entry, each with the BIOS's answer to the original and the entry's result in AX.</summary>
    private static List<(string Id, JsonElement Answer, int Result)> Cases(string entry)
    {
        var path = Path.Combine(OracleVectors.Folder, "MISC", entry + ".json");
        if (!File.Exists(path))
        {
            throw new SkipException($"no driver vectors {path} (tools/oracle/driver_vectors.py writes them from the player's game)");
        }
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(c =>
            (c.GetProperty("id").GetString()!, c.GetProperty("inputs")[0].Clone(), c.GetProperty("result").GetProperty("ax").GetInt32()))];
    }

    private static KeyboardDriver Keyboard(params int[] keys)
    {
        var graphics = new McgaGraphicsDriver(new FontSet([0, 0]), new byte[3 * McgaDriverFile.Colours]);
        return new KeyboardDriver(new HostLink(graphics, new HeadlessHost(keys)));
    }

    [SkippableFact]
    public void NoKeyWaitingAnswersAsTheOriginalDid()
    {
        var cases = Cases("_no_key_waiting");
        foreach (var (id, answer, result) in cases)
        {
            // the BIOS sets the zero flag when its key buffer is empty; otherwise AX is the key that waits
            var empty = answer.TryGetProperty("zf", out var zero) && zero.GetInt32() != 0;
            var keyboard = empty ? Keyboard() : Keyboard(answer.GetProperty("ax").GetInt32());
            Assert.True(keyboard.NoKeyWaiting() == (result == NoKey), $"_no_key_waiting {id}: the original returned {result:x4}");
        }
        output.WriteLine($"TALLY _no_key_waiting: {cases.Count} cases agree");
    }

    [SkippableFact]
    public void ReadKeyRawGivesTheKeyTheOriginalGot()
    {
        var cases = Cases("_read_key_raw");
        foreach (var (id, answer, result) in cases)
        {
            var key = Keyboard(answer.GetProperty("ax").GetInt32()).ReadKeyRaw();
            Assert.True(key == result, $"_read_key_raw {id}: the C# returns {key:x4}, the original {result:x4}");
        }
        output.WriteLine($"TALLY _read_key_raw: {cases.Count} cases agree");
    }
}
