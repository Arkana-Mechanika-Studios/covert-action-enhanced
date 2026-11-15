using System.Text.Json.Nodes;

namespace CovertAction.Host.Sdl;

/// <summary>
/// Opt-in scripted keys for timing runs of the real window (COVERT_INPUT_SCRIPT=&lt;inputs.json&gt;): a recorded
/// session's inputs (tools/session/original_side.py writes them), played as the headless host plays them. Each key
/// poll of the game takes the next entry, a key or a moment without one (null); after the last the window closes. The
/// time of day is the recording's, standing still, the part's own from its part times on, so that the game plays the
/// recorded session's path (as the session proof plays it, csharp/docs/SESSION-PROOF.md) at the pace of real time.
/// </summary>
/// <remarks>New code, for csharp/docs/PERFORMANCE.md; the player's keyboard is not read while a script plays.</remarks>
internal sealed class ScriptedInput
{
    private readonly Queue<int?> _entries;
    private readonly List<long> _partTimes;
    private int _parts;

    private ScriptedInput(Queue<int?> entries, long unixTime, List<long> partTimes)
    {
        _entries = entries;
        UnixSeconds = unixTime;
        _partTimes = partTimes;
    }

    /// <summary>The time of day the game is told.</summary>
    public long UnixSeconds { get; private set; }

    /// <summary>The script COVERT_INPUT_SCRIPT names, or null.</summary>
    public static ScriptedInput? FromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("COVERT_INPUT_SCRIPT") is not { Length: > 0 } path)
        {
            return null;
        }
        var inputs = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var keys = new Queue<int?>(inputs["keys"]!.AsArray().Select(k => k is null ? (int?)null : k.GetValue<int>()));
        var times = inputs["part_times"] is JsonArray t ? t.Select(x => x!.GetValue<long>()).ToList() : [];
        return new ScriptedInput(keys, inputs["unix_time"]?.GetValue<long>() ?? 0, times);
    }

    /// <summary>A part of the game starts: the time of day of the n-th part, as the session proof gives it.</summary>
    public void PartStarting()
    {
        if (_partTimes.Count > 0)
        {
            UnixSeconds = _partTimes[Math.Min(_parts++, _partTimes.Count - 1)];
        }
    }

    /// <summary>The next entry: true and the key, false for a moment without one; null when the script is used up.</summary>
    public bool? Next(out int key)
    {
        key = 0;
        if (!_entries.TryDequeue(out var entry))
        {
            return null;
        }
        key = entry ?? 0;
        return entry is not null;
    }
}
