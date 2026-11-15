using System.Text.Json;
using CovertAction.Audio;
using CovertAction.Audio.Tandy;
using CovertAction.Content;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The Tandy driver (<see cref="TandySoundDriver"/>) against the original's TSOUND.CA: tools/tsound_vectors.py runs the
/// driver's own instructions through scenarios of calls and timer interrupts and records, per call and per interrupt,
/// the bytes it writes to the sound chip's port and the timer's rate it sets. The C# makes the same calls and
/// interrupts, with the tunes read from the same driver file, and must write the same bytes in the same order. Skipped
/// without the vectors (build/oracle/TSOUND/psg.json) or without the player's TSOUND.CA (COVERT_GAME_DIR).
/// </summary>
public sealed class TandySoundOracleTests(ITestOutputHelper output)
{
    /// <summary>Writes what the driver does in the vectors' words.</summary>
    private sealed class Recorder : ISoundChip, ISoundTimer
    {
        public List<string> Events { get; } = [];

        public void Write(byte value)
        {
            if (Events.Count > 0 && Events[^1].StartsWith("psg ", StringComparison.Ordinal))
            {
                Events[^1] += $" {value:x2}";
            }
            else
            {
                Events.Add($"psg {value:x2}");
            }
        }

        public void SetInterruptDivisor(int divisor) => Events.Add($"timer {divisor:x4}");

        public List<string> Take()
        {
            var taken = Events.ToList();
            Events.Clear();
            return taken;
        }
    }

    private static string GameFolder =>
        Environment.GetEnvironmentVariable("COVERT_GAME_DIR") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Repository.Root, "game", "Covert Action", "Covert");

    [SkippableFact]
    public void EveryCallAndInterruptWritesTheChipAsTheOriginal()
    {
        var vectors = Path.Combine(OracleVectors.Folder, "TSOUND", "psg.json");
        var driverFile = Path.Combine(GameFolder, "TSOUND.CA");
        Skip.IfNot(File.Exists(vectors), $"no chip vectors {vectors} (tools/tsound_vectors.py)");
        Skip.IfNot(File.Exists(driverFile), $"no {driverFile}");
        var bytes = File.ReadAllBytes(driverFile);
        using var document = JsonDocument.Parse(File.ReadAllBytes(vectors));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            document.RootElement.GetProperty("driver_sha256").GetString());

        int scenarios = 0, calls = 0, interrupts = 0, events = 0;
        var breaks = new List<string>();
        foreach (var scenario in document.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            scenarios++;
            var name = scenario.GetProperty("name").GetString();
            var recorder = new Recorder();
            // a fresh copy of the data: the driver patches its tunes as it plays
            var driver = new TandySoundDriver(TandyDriverFile.Segment(bytes), recorder, recorder);
            var opIndex = 0;
            foreach (var op in scenario.GetProperty("ops").EnumerateArray())
            {
                var kind = op.GetProperty("op").GetString();
                var args = op.GetProperty("args").EnumerateArray().Select(a => a.GetInt32()).ToArray();
                var expectedCall = Strings(op.GetProperty("events"));
                var expectedIrqs = op.GetProperty("irqs").EnumerateArray().Select(Strings).ToList();
                var where = $"{name}, op {opIndex} ({kind} {string.Join(' ', args)})";
                switch (kind)
                {
                    case "init": driver.Start(); calls++; break;
                    case "stop": driver.Stop(); calls++; break;
                    case "play":
                        driver.Play(args[0], args.Length > 1 ? args[1] : 0, args.Length > 2 ? args[2] : 0);
                        calls++;
                        break;
                }
                Compare(where + " call", expectedCall, recorder.Take(), breaks);
                events += expectedCall.Count;
                var actualIrqs = new List<List<string>>();
                if (kind is "irq" or "until_quiet")
                {
                    foreach (var _ in expectedIrqs)
                    {
                        driver.TimerInterrupt();
                        actualIrqs.Add(recorder.Take());
                    }
                }
                for (var i = 0; i < expectedIrqs.Count; i++)
                {
                    Compare($"{where} interrupt {i}", expectedIrqs[i], actualIrqs[i], breaks);
                    events += expectedIrqs[i].Count;
                }
                interrupts += expectedIrqs.Count;
                if (kind == "until_quiet" && expectedIrqs.Count < (args.Length > 0 ? args[0] : 400))
                {
                    Assert.True(driver.IsQuiet, $"{where}: the original is quiet, the C# is not");
                }
                opIndex++;
            }
        }
        output.WriteLine($"{scenarios} scenarios, {calls} calls, {interrupts} interrupts, {events} chip and timer events compared");
        Assert.True(breaks.Count == 0, $"{breaks.Count} differences; the first:\n" + string.Join("\n", breaks.Take(10)));
    }

    private static List<string> Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static void Compare(string where, List<string> expected, List<string> actual, List<string> breaks)
    {
        if (!expected.SequenceEqual(actual))
        {
            breaks.Add($"{where}: original [{string.Join(", ", expected)}], C# [{string.Join(", ", actual)}]");
        }
    }

    private sealed class StillClock : CovertAction.Platform.IClock
    {
        public long Milliseconds => 0;

        public long UnixSeconds => 0;

        public void Sleep(int milliseconds)
        {
        }
    }

    /// <summary>The title through the device's clock and the chip voice: it sounds, and the stop silences it.</summary>
    [SkippableFact]
    public void TheTitleSoundsOnTheChipUntilItIsStopped()
    {
        var driverFile = Path.Combine(GameFolder, "TSOUND.CA");
        Skip.IfNot(File.Exists(driverFile), $"no {driverFile}");
        const int rate = 44100;
        var driver = TandySoundDriver.OnChip(File.ReadAllBytes(driverFile), rate, new StillClock());
        var buffer = new short[2 * rate];
        driver.PlaySound(2);
        driver.Render(buffer);
        Assert.True(buffer.Max(s => Math.Abs((int)s)) > 1000, "the title is silent");
        driver.PlaySound(0);
        driver.Render(buffer);
        Assert.True(buffer.Skip(buffer.Length / 2).All(s => Math.Abs((int)s) < 50), "the reset leaves a sound");
    }
}
