using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace CovertAction.Host.Sdl;

/// <summary>
/// An opt-in timing trace of the SDL host (COVERT_FRAME_TRACE=&lt;file&gt;): one line per frame shown, with where the time
/// between two frames went (the game's code, the host's waits, showing the frame), the key presses and how long they
/// took to reach the screen, the sound device's callbacks, and the garbage collector's pauses; a summary at the end.
/// For finding lag (csharp/docs/PERFORMANCE.md); without the variable nothing is measured.
/// </summary>
/// <remarks>
/// New code. The columns of a frame line (milliseconds): <c>t</c> when the frame was handed to SDL, from the start;
/// <c>gap</c> since the frame before was handed over; <c>game</c> the part of the gap neither waiting nor showing (the
/// game's code and the host's events); <c>ask</c> and <c>slept</c> the waits asked for and how long they took;
/// <c>conv</c> the colours turned into the texture's pixels; <c>upload</c> the texture's upload; <c>show</c> drawing and
/// SDL_RenderPresent; <c>polls</c> the key polls; <c>keys</c> keys taken; <c>gc</c> garbage-collection pause; then the
/// latency of each key taken since the frame before (from the key's event to this frame), and the sound callbacks.
/// </remarks>
internal sealed class FrameTrace : IDisposable
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    private readonly StreamWriter _writer;
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly long _sdlOffset; // Stopwatch ticks at SDL's nanosecond 0
    private readonly ConcurrentQueue<string> _audio = new();
    private long _lastPresent, _lastStart;
    private double _asked, _slept, _waitTicks;
    private int _polls, _keysTaken;
    private readonly List<double> _latencies = [];
    private readonly Queue<long> _arrivals = new();   // the events' times of the keys in the host's queue
    private readonly List<long> _taken = [];          // ... of the keys the game took since the last frame
    private TimeSpan _gcBefore = GC.GetTotalPauseDuration();

    // the summary
    private readonly List<double> _gaps = [], _games = [], _shows = [], _oversleeps = [], _frameWaits = [], _starts = [], _allLatencies = [], _callbacks = [], _callbackGaps = [];
    private int _frames, _textFrames, _gcFrames, _lateCallbacks;
    private double _gcTotal;
    private readonly Dictionary<string, (int Frames, double Start, double End)> _parts = [];
    private string _part = "start";
    private long _lastCallback;

    private FrameTrace(string path)
    {
        _writer = new StreamWriter(path, false) { AutoFlush = false };
        _sdlOffset = Stopwatch.GetTimestamp() - (long)(SdlNative.SDL_GetTicksNS() / 1e9 * Stopwatch.Frequency);
        _lastPresent = _lastStart = _start;
        _writer.WriteLine("# COVERT_FRAME_TRACE: t gap game ask slept conv upload show polls keys gc sgap | key latencies | sound callbacks (ms)");
    }

    /// <summary>The trace COVERT_FRAME_TRACE asks for, or null.</summary>
    public static FrameTrace? FromEnvironment() =>
        Environment.GetEnvironmentVariable("COVERT_FRAME_TRACE") is { Length: > 0 } path ? new FrameTrace(path) : null;

    /// <summary>A line of its own in the trace (the host's set-up, a part of the game starting).</summary>
    public void Mark(string text)
    {
        _writer.WriteLine("# " + Now().ToString("F1", CultureInfo.InvariantCulture) + " " + text);
        if (text.StartsWith("part ", StringComparison.Ordinal))
        {
            _part = text[5..];
        }
    }

    /// <summary>
    /// A wait of the host: asked for so many milliseconds, from <paramref name="start"/> to now; <paramref name="frame"/>:
    /// the wait for the display's next frame (its overshoot is how late the frame is).
    /// </summary>
    public void Slept(double asked, long start, bool frame = false)
    {
        var took = Stopwatch.GetTimestamp() - start;
        _asked += asked;
        _waitTicks += took;
        _slept += took * TicksToMs;
        (frame ? _frameWaits : _oversleeps).Add(took * TicksToMs - asked);
    }

    /// <summary>A key poll of the game.</summary>
    public void Polled() => _polls++;

    /// <summary>A key came from SDL at its event's time (SDL nanoseconds; 0: now).</summary>
    public void KeyArrived(ulong sdlNanoseconds) =>
        _arrivals.Enqueue(sdlNanoseconds == 0 ? Stopwatch.GetTimestamp() : _sdlOffset + (long)(sdlNanoseconds / 1e9 * Stopwatch.Frequency));

    /// <summary>The game took the oldest key of the host's queue.</summary>
    public void KeyTaken()
    {
        _keysTaken++;
        _taken.Add(_arrivals.TryDequeue(out var arrival) ? arrival : Stopwatch.GetTimestamp());
    }

    /// <summary>A frame shown: the times (Stopwatch ticks) it started, its pixels were converted, uploaded and presented.</summary>
    public void Frame(bool text, long start, long converted, long uploaded, long presented)
    {
        var gap = (presented - _lastPresent) * TicksToMs;
        var show = (presented - start) * TicksToMs;
        var game = gap - show - _waitTicks * TicksToMs;
        var gc = GC.GetTotalPauseDuration();
        var gcMs = (gc - _gcBefore).TotalMilliseconds;
        _gcBefore = gc;
        _latencies.Clear();
        foreach (var arrival in _taken)
        {
            _latencies.Add((presented - arrival) * TicksToMs);
        }
        _taken.Clear();
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{(presented - _start) * TicksToMs:F2} {gap:F2} {game:F2} {_asked:F0} {_slept:F2} {(converted - start) * TicksToMs:F2} {(uploaded - converted) * TicksToMs:F2} {(presented - uploaded) * TicksToMs:F2} {_polls} {_keysTaken} {gcMs:F2} {(start - _lastStart) * TicksToMs:F2}");
        if (text)
        {
            line += " text";
        }
        if (_latencies.Count > 0)
        {
            line += " | keys " + string.Join(",", _latencies.Select(l => l.ToString("F1", CultureInfo.InvariantCulture)));
        }
        var sound = new List<string>();
        while (_audio.TryDequeue(out var a))
        {
            sound.Add(a);
        }
        if (sound.Count > 0)
        {
            line += " | sound " + string.Join(",", sound);
        }
        _writer.WriteLine(line);

        _frames++;
        _textFrames += text ? 1 : 0;
        _gaps.Add(gap);
        _starts.Add((start - _lastStart) * TicksToMs);
        _lastStart = start;
        _games.Add(game);
        _shows.Add(show);
        _allLatencies.AddRange(_latencies);
        if (gcMs > 0)
        {
            _gcFrames++;
            _gcTotal += gcMs;
        }
        var t = (presented - _start) * TicksToMs;
        _parts[_part] = _parts.TryGetValue(_part, out var p) ? (p.Frames + 1, p.Start, t) : (1, t, t);
        _lastPresent = presented;
        _asked = _slept = _waitTicks = 0;
        _polls = _keysTaken = 0;
    }

    /// <summary>
    /// A callback of the sound device (its thread): it began at <paramref name="start"/> and was asked for so many frames
    /// at the rate; it is late when it came more than a buffer and a half after the one before.
    /// </summary>
    public void AudioCallback(long start, int frames, int rate)
    {
        var end = Stopwatch.GetTimestamp();
        var took = (end - start) * TicksToMs;
        var since = _lastCallback == 0 ? 0 : (start - _lastCallback) * TicksToMs;
        _lastCallback = start;
        var buffer = 1000.0 * frames / rate;
        var late = since > 1.5 * buffer && _callbacks.Count > 0;
        lock (_callbacks)
        {
            _callbacks.Add(took);
            if (since > 0)
            {
                _callbackGaps.Add(since);
            }
            _lateCallbacks += late ? 1 : 0;
        }
        _audio.Enqueue(string.Create(CultureInfo.InvariantCulture, $"{took:F2}/{since:F1}{(late ? "!" : "")}"));
    }

    private double Now() => (Stopwatch.GetTimestamp() - _start) * TicksToMs;

    private static string Percentiles(List<double> values)
    {
        if (values.Count == 0)
        {
            return "none";
        }
        var sorted = values.Order().ToArray();
        double At(double q) => sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        return string.Create(CultureInfo.InvariantCulture,
            $"n {sorted.Length}, mean {sorted.Average():F2}, p50 {At(0.5):F2}, p90 {At(0.9):F2}, p99 {At(0.99):F2}, max {sorted[^1]:F2}");
    }

    public void Dispose()
    {
        var w = _writer;
        w.WriteLine("# summary");
        w.WriteLine($"# frames {_frames} (text {_textFrames}) in {Now() / 1000:F1} s");
        foreach (var (part, p) in _parts)
        {
            var seconds = (p.End - p.Start) / 1000;
            w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"#   part {part}: {p.Frames} frames, {(seconds > 0 ? p.Frames / seconds : 0):F1} a second"));
        }
        w.WriteLine("# gap between frames: " + Percentiles(_gaps));
        w.WriteLine("# game code in a gap:  " + Percentiles(_games));
        w.WriteLine("# showing a frame:     " + Percentiles(_shows));
        w.WriteLine("# between frame starts: " + Percentiles(_starts));
        w.WriteLine("# a wait's overshoot:  " + Percentiles(_oversleeps));
        w.WriteLine("# a frame wait's:      " + Percentiles(_frameWaits));
        w.WriteLine("# key to screen:       " + Percentiles(_allLatencies));
        lock (_callbacks)
        {
            w.WriteLine("# sound callback:      " + Percentiles(_callbacks));
            w.WriteLine("# between callbacks:   " + Percentiles(_callbackGaps) + $", late {_lateCallbacks}");
        }
        w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# gc: {_gcFrames} frames with a pause, {_gcTotal:F1} ms in all; collections {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));
        w.Dispose();
    }
}
