using System.Text.Json.Nodes;
using CovertAction.Audio;
using CovertAction.Platform;

namespace CovertAction.Session.Tests;

/// <summary>
/// The sound device's stream of a whole session (csharp/docs/SESSION-PROOF.md, item 8): what the sound driver did to its
/// device (the AdLib card's OPL registers, the MPU-401's MIDI bytes and commands, the Tandy's SN76496 bytes, the PC
/// speaker) and to the PC timer (its rate, each interrupt and its end), among a checkpoint's calls, in order with the
/// sound requests. The original side records it from the ports (tools/session/campaign_side.py), the C# side from
/// <c>Game.SoundDeviceWritten</c>, as the same calls: <c>["opl", register, value]</c>, <c>["midi", byte]</c>,
/// <c>["mpu", command]</c>, <c>["psg", byte]</c>, <c>["speaker", "tone" | "div xxxx" | "on" | "off"]</c>,
/// <c>["timer_rate", divisor]</c>, <c>["tick"]</c>, <c>["tick_end"]</c>.
/// </summary>
internal static class DeviceStream
{
    /// <summary>The calls of the stream (with the sound requests, which order it).</summary>
    private static readonly HashSet<string> Kinds = ["play_sound", "opl", "midi", "mpu", "psg", "speaker", "timer_rate", "tick", "tick_end"];

    /// <summary>The register entry of a tick inside a call of the driver (DIVERGENCES.md).</summary>
    public const string TickInsideCall = "D-19 (a timer tick inside a call of the sound driver: the rewrite's comes after the call's own writes)";

    /// <summary>
    /// The stream of a checkpoint's calls as text, one call a line. For the original's calls (<paramref name="original"/>),
    /// a tick that fell inside a call of the sound driver (between its <c>play_sound</c> and its <c>sound_return</c>) is
    /// taken, with what it wrote, after the call's own writes, where the rewrite has it (D-19: the C#'s interrupts come
    /// between the device's frames, never inside a command); <paramref name="reordered"/> counts those ticks that had own
    /// writes of the call after them. A tick in the driver's wait at the end of a call (ISOUND's command 28) stays where it is.
    /// </summary>
    public static string Normal(string calls, bool original, out int reordered)
    {
        var stream = new List<string>();
        var moved = new List<string>();
        var inCall = false;
        var depth = 0;
        var ticksMoved = 0;
        reordered = 0;
        foreach (var call in JsonNode.Parse(calls)!.AsArray())
        {
            var kind = call![0]!.GetValue<string>();
            if (original && kind == "sound_return")
            {
                stream.AddRange(moved);
                moved.Clear();
                inCall = false;
                ticksMoved = 0;
                continue;
            }
            if (!Kinds.Contains(kind))
            {
                continue;
            }
            var text = call.ToJsonString();
            if (original && kind == "play_sound")
            {
                inCall = true;
            }
            if (inCall && kind == "tick")
            {
                depth++;
                ticksMoved++;
                moved.Add(text);
            }
            else if (inCall && depth > 0)
            {
                moved.Add(text);
                depth -= kind == "tick_end" ? 1 : 0;
            }
            else
            {
                // a write of the call itself after a tick that fell inside it: the order differs from the rewrite's
                reordered += ticksMoved;
                ticksMoved = 0;
                stream.Add(text);
            }
        }
        stream.AddRange(moved);
        return string.Join("\n", stream);
    }

    /// <summary>Whether a session's calls hold a kind of device call.</summary>
    public static bool Has(IEnumerable<string> calls, string kind) =>
        calls.Any(c => JsonNode.Parse(c)!.AsArray().Any(call => call![0]!.GetValue<string>() == kind));

    /// <summary>Where two streams first differ, with the calls around it on both sides.</summary>
    public static string Describe(string a, string b)
    {
        var (x, y) = (a.Length == 0 ? [] : a.Split('\n'), b.Length == 0 ? [] : b.Split('\n'));
        var at = 0;
        while (at < x.Length && at < y.Length && x[at] == y[at])
        {
            at++;
        }
        static string Around(string[] s, int at) => string.Join(" ", s.Skip(Math.Max(0, at - 3)).Take(8));
        return $"{x.Length} calls against {y.Length}, the first difference at call {at}: original ...{Around(x, at)}..., rewrite ...{Around(y, at)}...";
    }

    /// <summary>
    /// The original's calls after its last checkpoint up to the end of the last part (its <c>["exit", name]</c>), and what
    /// follows, which is the launcher's own end (the sound driver's stop: D-602, no counterpart in the rewrite).
    /// </summary>
    public static (string Compared, int LauncherWrites) Tail(string callsAfter)
    {
        var calls = JsonNode.Parse(callsAfter)!.AsArray();
        var last = -1;
        for (var i = 0; i < calls.Count; i++)
        {
            if (calls[i]![0]!.GetValue<string>() == "exit" && calls[i]![1]!.GetValue<string>() != "COVERT.EXE")
            {
                last = i;
            }
        }
        var compared = new JsonArray();
        var launcher = 0;
        for (var i = 0; i < calls.Count; i++)
        {
            if (i <= last)
            {
                compared.Add(calls[i]!.DeepClone());
            }
            else if (Kinds.Contains(calls[i]![0]!.GetValue<string>()))
            {
                launcher++;
            }
        }
        return (compared.ToJsonString(), launcher);
    }

    /// <summary>
    /// The timer ticks the original's driver got, counted along its calls: before each sound request (in order), up to each
    /// checkpoint, and up to the end of the last part. The C# side gives its driver as many by then
    /// (<see cref="ClockedSoundDriver.RunInterrupts"/>), so that both drivers have had the same interrupts between the same requests.
    /// </summary>
    public sealed class Schedule
    {
        private readonly List<int> _beforeRequest = [];
        private readonly List<int> _atCheckpoint = [];

        public Schedule(SessionRecording original)
        {
            var ticks = 0;
            void Count(string calls)
            {
                foreach (var call in JsonNode.Parse(calls)!.AsArray())
                {
                    switch (call![0]!.GetValue<string>())
                    {
                        case "tick":
                            ticks++;
                            break;
                        case "play_sound":
                            _beforeRequest.Add(ticks);
                            break;
                    }
                }
            }
            foreach (var checkpoint in original.Checkpoints)
            {
                Count(checkpoint.Calls);
                _atCheckpoint.Add(ticks);
            }
            Count(Tail(original.CallsAfter).Compared);
            AtEnd = ticks;
        }

        /// <summary>The ticks before the n-th sound request (from 0); the last count for a request the original did not make.</summary>
        public int BeforeRequest(int n) => n < _beforeRequest.Count ? _beforeRequest[n] : _beforeRequest.Count == 0 ? 0 : _beforeRequest[^1];

        /// <summary>The ticks up to the n-th checkpoint.</summary>
        public int AtCheckpoint(int n) => n < _atCheckpoint.Count ? _atCheckpoint[n] : AtEnd;

        /// <summary>The ticks up to the end of the last part.</summary>
        public int AtEnd { get; }
    }

    /// <summary>The C#'s device event as the original side's call.</summary>
    public static JsonArray Call(SoundDeviceEvent e) => e.Kind switch
    {
        SoundDeviceEventKind.OplRegister => ["opl", e.Value, e.Data],
        SoundDeviceEventKind.MidiByte => ["midi", e.Value],
        SoundDeviceEventKind.MpuCommand => ["mpu", e.Value],
        SoundDeviceEventKind.ChipByte => ["psg", e.Value],
        SoundDeviceEventKind.SpeakerTone => ["speaker", "tone"],
        SoundDeviceEventKind.SpeakerDivisor => ["speaker", $"div {e.Value:x4}"],
        SoundDeviceEventKind.SpeakerOn => ["speaker", "on"],
        SoundDeviceEventKind.SpeakerOff => ["speaker", "off"],
        SoundDeviceEventKind.TimerRate => ["timer_rate", e.Value],
        SoundDeviceEventKind.TimerTick => ["tick"],
        SoundDeviceEventKind.TimerTickEnd => ["tick_end"],
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "a device event the original side does not record"),
    };
}

/// <summary>
/// The headless host's sound device in a whole session: it never asks for frames; it keeps the driver the game started
/// on it, whose timer interrupts the session gives as the original's came (<see cref="DeviceStream.Schedule"/>).
/// </summary>
internal sealed class TickedOutput : IPcmOutput
{
    /// <summary>The driver the game started, if it is one of the reconstructed drivers.</summary>
    public ClockedSoundDriver? Driver { get; private set; }

    public int SampleRate => 44100;

    public long FramesPlayed => 0;

    public int LatencyFrames => 0;

    public void Start(IPcmSource source) => Driver = source as ClockedSoundDriver;

    public void Dispose()
    {
    }
}
