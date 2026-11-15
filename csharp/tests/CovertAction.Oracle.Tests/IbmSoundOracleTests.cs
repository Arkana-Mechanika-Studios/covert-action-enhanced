using System.Text.Json;
using CovertAction.Audio;
using CovertAction.Audio.Speaker;
using CovertAction.Content;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The PC-speaker driver (<see cref="IbmSoundDriver"/>) against the original's ISOUND.CA: tools/isound_vectors.py runs
/// the driver's own instructions through scenarios of calls and timer interrupts and records, per call and per
/// interrupt, what it does to the speaker and the timer (tone mode, divisor, speaker on/off, interrupt rate). The C#
/// makes the same calls and interrupts, with the sounds read from the same driver file, and must do the same, in the
/// same order, call by call and interrupt by interrupt. Skipped without the vectors (build/oracle/ISOUND/speaker.json)
/// or without the player's ISOUND.CA (COVERT_GAME_DIR).
/// </summary>
public sealed class IbmSoundOracleTests(ITestOutputHelper output)
{
    /// <summary>Writes what the driver does in the vectors' words.</summary>
    private sealed class Recorder : ISpeaker, ISoundTimer
    {
        public List<string> Events { get; } = [];

        public void StartTone() => Events.Add("tone");

        public void SetDivisor(ushort divisor) => Events.Add($"div {divisor:x4}");

        public void Connect() => Events.Add("on");

        public void Disconnect() => Events.Add("off");

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
    public void EveryCallAndInterruptDrivesTheSpeakerAsTheOriginal()
    {
        var vectors = Path.Combine(OracleVectors.Folder, "ISOUND", "speaker.json");
        var driverFile = Path.Combine(GameFolder, "ISOUND.CA");
        Skip.IfNot(File.Exists(vectors), $"no speaker vectors {vectors} (tools/isound_vectors.py)");
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
            // a fresh file: the teleprinter's sound changes as it plays
            var driver = new IbmSoundDriver(SpeakerDriverFile.Read(bytes), recorder, recorder);
            var opIndex = 0;
            foreach (var op in scenario.GetProperty("ops").EnumerateArray())
            {
                var kind = op.GetProperty("op").GetString();
                var args = op.GetProperty("args").EnumerateArray().Select(a => a.GetInt32()).ToArray();
                var expectedCall = Strings(op.GetProperty("events"));
                var expectedIrqs = op.GetProperty("irqs").EnumerateArray().Select(Strings).ToList();
                var where = $"{name}, op {opIndex} ({kind} {string.Join(' ', args)})";
                var waits = false;
                switch (kind)
                {
                    case "init": driver.Start(); calls++; break;
                    case "stop": driver.Stop(); calls++; break;
                    case "play": waits = driver.Play(args[0], args.Length > 1 ? args[1] : 0, 0); calls++; break;
                }
                Compare(where + " call", expectedCall, recorder.Take(), breaks);
                events += expectedCall.Count;
                var actualIrqs = new List<List<string>>();
                if (waits)
                {
                    // the original's waiting loop: an interrupt before each look at whether all is quiet
                    do
                    {
                        driver.TimerInterrupt();
                        actualIrqs.Add(recorder.Take());
                    }
                    while (!driver.IsQuiet && actualIrqs.Count <= expectedIrqs.Count);
                }
                else if (kind is "irq" or "until_quiet")
                {
                    foreach (var _ in expectedIrqs)
                    {
                        driver.TimerInterrupt();
                        actualIrqs.Add(recorder.Take());
                    }
                }
                if (actualIrqs.Count != expectedIrqs.Count)
                {
                    breaks.Add($"{where}: {actualIrqs.Count} interrupts, the original {expectedIrqs.Count}");
                }
                for (var i = 0; i < Math.Min(actualIrqs.Count, expectedIrqs.Count); i++)
                {
                    Compare($"{where} interrupt {i}", expectedIrqs[i], actualIrqs[i], breaks);
                    events += expectedIrqs[i].Count;
                }
                interrupts += expectedIrqs.Count;
                opIndex++;
            }
        }
        output.WriteLine($"{scenarios} scenarios, {calls} calls, {interrupts} interrupts, {events} speaker and timer events compared");
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

    /// <summary>The theme through the device's clock and the speaker voice: it sounds, and the stop silences it.</summary>
    [SkippableFact]
    public void TheThemeSoundsOnTheSpeakerUntilItIsStopped()
    {
        var driverFile = Path.Combine(GameFolder, "ISOUND.CA");
        Skip.IfNot(File.Exists(driverFile), $"no {driverFile}");
        const int rate = 44100;
        var driver = IbmSoundDriver.OnSpeaker(SpeakerDriverFile.Read(File.ReadAllBytes(driverFile)), rate, new StillClock());
        var buffer = new short[2 * rate];
        driver.PlaySound(2);
        driver.Render(buffer);
        Assert.True(buffer.Max(s => Math.Abs((int)s)) > 3000, "the theme is silent");
        driver.PlaySound(1);
        driver.Render(buffer);
        Assert.True(buffer.Skip(buffer.Length / 2).All(s => Math.Abs((int)s) < 50), "the stop leaves a sound");
    }
}
