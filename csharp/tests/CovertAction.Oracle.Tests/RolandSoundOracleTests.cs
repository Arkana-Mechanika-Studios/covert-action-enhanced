using System.Text;
using System.Text.Json;
using CovertAction.Audio;
using CovertAction.Audio.Roland;
using CovertAction.Content;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The Roland MT-32 driver (<see cref="RolandSoundDriver"/>) against the original's RSOUND.CA: tools/rsound_vectors.py
/// runs the driver's own instructions through scenarios of calls and timer interrupts against a ready MPU-401 and
/// records, per call and per interrupt (one interrupt is one tick), every MIDI byte the MT-32 receives (the start-up's
/// system-exclusive messages included), every command to the MPU and every change of the interrupt's rate, in order.
/// The C# makes the same calls and interrupts, with the tunes and messages read from the same driver file, on a
/// recording MPU that acknowledges every command, and must send the same. Skipped without the vectors
/// (build/oracle/RSOUND/midi.json) or without the player's RSOUND.CA (COVERT_GAME_DIR).
/// </summary>
public sealed class RolandSoundOracleTests(ITestOutputHelper output)
{
    /// <summary>Writes what the driver does in the vectors' words; MIDI bytes in a row make one event.</summary>
    private sealed class Recorder : IMpu401, ISoundTimer
    {
        private readonly List<string> _events = [];
        private readonly StringBuilder _midi = new();

        public int Bytes { get; private set; }

        public bool Command(byte command)
        {
            Flush();
            _events.Add($"command {command:x2}");
            return true;
        }

        public void Send(byte value)
        {
            _midi.Append(_midi.Length == 0 ? "midi " : " ").Append(value.ToString("x2"));
            Bytes++;
        }

        public void SetInterruptDivisor(int divisor)
        {
            Flush();
            _events.Add($"timer {divisor:x4}");
        }

        public void Add(string text)
        {
            Flush();
            _events.Add(text);
        }

        public List<string> Take()
        {
            Flush();
            var taken = _events.ToList();
            _events.Clear();
            return taken;
        }

        private void Flush()
        {
            if (_midi.Length > 0)
            {
                _events.Add(_midi.ToString());
                _midi.Clear();
            }
        }
    }

    private static string GameFolder =>
        Environment.GetEnvironmentVariable("COVERT_GAME_DIR") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Repository.Root, "game", "Covert Action", "Covert");

    [SkippableFact]
    public void EveryCallAndInterruptSendsTheMidiOfTheOriginal()
    {
        var vectors = Path.Combine(OracleVectors.Folder, "RSOUND", "midi.json");
        var driverFile = Path.Combine(GameFolder, "RSOUND.CA");
        Skip.IfNot(File.Exists(vectors), $"no MIDI vectors {vectors} (tools/rsound_vectors.py)");
        Skip.IfNot(File.Exists(driverFile), $"no {driverFile}");
        var bytes = File.ReadAllBytes(driverFile);
        using var document = JsonDocument.Parse(File.ReadAllBytes(vectors));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            document.RootElement.GetProperty("driver_sha256").GetString());

        int scenarios = 0, calls = 0, interrupts = 0, events = 0, midi = 0;
        var breaks = new List<string>();
        foreach (var scenario in document.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            scenarios++;
            var name = scenario.GetProperty("name").GetString();
            var recorder = new Recorder();
            // a fresh file: the tunes and the gunfire rewrite tune bytes as they play
            var driver = new RolandSoundDriver(RolandDriverFile.Read(bytes), recorder, recorder);
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
                        recorder.Add(driver.MpuFound ? "detected" : "not detected");
                        calls++;
                        break;
                    case "stop": driver.Stop(); calls++; break;
                    case "play":
                        Assert.False(driver.Play(args[0], args.ElementAtOrDefault(1), args.ElementAtOrDefault(2)), "the Roland driver never waits");
                        calls++;
                        break;
                }
                Compare(where + " call", expectedCall, recorder.Take(), breaks);
                events += expectedCall.Count;
                for (var i = 0; i < expectedIrqs.Count; i++)
                {
                    driver.TimerInterrupt();
                    Compare($"{where} tick {i}", expectedIrqs[i], recorder.Take(), breaks);
                    events += expectedIrqs[i].Count;
                }
                interrupts += expectedIrqs.Count;
                opIndex++;
            }
            midi += recorder.Bytes;
        }
        output.WriteLine($"{scenarios} scenarios, {calls} calls, {interrupts} interrupts, {events} events ({midi} MIDI bytes) compared");
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
        static string Cut(string s) => s.Length > 160 ? s[..160] + "..." : s;
        breaks.Add($"{where}: from event {first} of {expected.Count}/{actual.Count}: original [{string.Join(", ", expected.Skip(first).Take(3).Select(Cut))}], C# [{string.Join(", ", actual.Skip(first).Take(3).Select(Cut))}]");
    }
}
