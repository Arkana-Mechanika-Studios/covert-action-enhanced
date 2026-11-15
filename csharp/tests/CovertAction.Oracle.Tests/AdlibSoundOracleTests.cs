using System.Text.Json;
using CovertAction.Audio;
using CovertAction.Audio.Adlib;
using CovertAction.Audio.Synthesis;
using CovertAction.Content;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The AdLib driver (<see cref="AdlibSoundDriver"/>) against the original's ASOUND.CA: tools/asound_vectors.py runs the
/// driver's own instructions through scenarios of calls and timer interrupts and records, per call and per
/// interrupt, every OPL register write, every status read and every change of the interrupt's rate, in order. The
/// C# makes the same calls and interrupts, with the instruments and tunes read from the same driver file, on a
/// recording card whose status is the synthesiser chip's, and must do the same. Skipped without the vectors
/// (build/oracle/ASOUND/opl.json) or without the player's ASOUND.CA (COVERT_GAME_DIR).
/// </summary>
public sealed class AdlibSoundOracleTests(ITestOutputHelper output)
{
    /// <summary>Writes what the driver does in the vectors' words; the status is the chip's.</summary>
    private sealed class Recorder : IOplChip, ISoundTimer
    {
        private readonly Opl3 _chip = new();

        public List<string> Events { get; } = [];

        public int Writes { get; private set; }

        public void Write(int register, byte value)
        {
            Events.Add($"{register:x2}={value:x2}");
            Writes++;
            _chip.Write(register, value);
        }

        public byte ReadStatus()
        {
            Events.Add("status");
            return _chip.Status;
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
    public void EveryCallAndInterruptDrivesTheCardAsTheOriginal()
    {
        var vectors = Path.Combine(OracleVectors.Folder, "ASOUND", "opl.json");
        var driverFile = Path.Combine(GameFolder, "ASOUND.CA");
        Skip.IfNot(File.Exists(vectors), $"no OPL vectors {vectors} (tools/asound_vectors.py)");
        Skip.IfNot(File.Exists(driverFile), $"no {driverFile}");
        var bytes = File.ReadAllBytes(driverFile);
        using var document = JsonDocument.Parse(File.ReadAllBytes(vectors));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            document.RootElement.GetProperty("driver_sha256").GetString());

        int scenarios = 0, calls = 0, interrupts = 0, events = 0, writes = 0;
        var breaks = new List<string>();
        foreach (var scenario in document.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            scenarios++;
            var name = scenario.GetProperty("name").GetString();
            var recorder = new Recorder();
            // a fresh file: the gunfire rewrites tune bytes as it plays
            var driver = new AdlibSoundDriver(AdlibDriverFile.Read(bytes), recorder, recorder);
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
                    case "init":
                        driver.Start();
                        recorder.Events.Add(driver.CardDetected ? "detected" : "not detected");
                        calls++;
                        break;
                    case "stop": driver.Stop(); calls++; break;
                    case "play":
                        Assert.False(driver.Play(args[0], args.ElementAtOrDefault(1), args.ElementAtOrDefault(2)), "the AdLib driver never waits");
                        calls++;
                        break;
                }
                Compare(where + " call", expectedCall, recorder.Take(), breaks);
                events += expectedCall.Count;
                for (var i = 0; i < expectedIrqs.Count; i++)
                {
                    driver.TimerInterrupt();
                    Compare($"{where} interrupt {i}", expectedIrqs[i], recorder.Take(), breaks);
                    events += expectedIrqs[i].Count;
                }
                interrupts += expectedIrqs.Count;
                opIndex++;
            }
            writes += recorder.Writes;
        }
        output.WriteLine($"{scenarios} scenarios, {calls} calls, {interrupts} interrupts, {events} events ({writes} OPL writes) compared");
        Assert.True(breaks.Count == 0, $"{breaks.Count} differences; the first:\n" + string.Join("\n", breaks.Take(10)));
    }

    private static List<string> Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static void Compare(string where, List<string> expected, List<string> actual, List<string> breaks)
    {
        if (expected.SequenceEqual(actual))
        {
            return;
        }
        var first = Enumerable.Range(0, Math.Min(expected.Count, actual.Count)).FirstOrDefault(i => expected[i] != actual[i], Math.Min(expected.Count, actual.Count));
        breaks.Add($"{where}: from event {first} of {expected.Count}/{actual.Count}: original [{string.Join(", ", expected.Skip(first).Take(8))}], C# [{string.Join(", ", actual.Skip(first).Take(8))}]");
    }

    private sealed class StillClock : CovertAction.Platform.IClock
    {
        public long Milliseconds => 0;

        public long UnixSeconds => 0;

        public void Sleep(int milliseconds)
        {
        }
    }

    /// <summary>The theme through the device's clock and the OPL synthesiser: it sounds, and the stop (command 0) silences it.</summary>
    [SkippableFact]
    public void TheThemeSoundsOnTheCardUntilItIsStopped()
    {
        var driverFile = Path.Combine(GameFolder, "ASOUND.CA");
        Skip.IfNot(File.Exists(driverFile), $"no {driverFile}");
        const int rate = 44100;
        var driver = AdlibSoundDriver.OnOpl(AdlibDriverFile.Read(File.ReadAllBytes(driverFile)), rate, new StillClock());
        var buffer = new short[2 * rate];
        driver.PlaySound(2);
        driver.Render(buffer);
        Assert.True(buffer.Max(s => Math.Abs((int)s)) > 3000, "the theme is silent");
        driver.PlaySound(0);
        driver.Render(buffer);
        // every register 0: the operators stand still at a frequency of 0, a constant level at most
        var tail = buffer.Skip(buffer.Length / 2).ToArray();
        Assert.True(tail.Max() - tail.Min() < 50, "the stop leaves a sound");
    }
}
