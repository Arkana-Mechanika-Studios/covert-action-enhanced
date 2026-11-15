using System.Text.Json;
using System.Text.Json.Nodes;

namespace CovertAction.Session.Tests;

/// <summary>
/// One checkpoint of a session: the moment the program took the next entry of the key script (csharp/docs/SESSION-PROOF.md).
/// </summary>
/// <param name="Number">The checkpoint's number, from 0: the number of script entries taken before it.</param>
/// <param name="Entry">The entry taken: a key code, null for a poll that found no key; meaningless when <paramref name="End"/>.</param>
/// <param name="End">The program asked for an entry after the last one: the session ends here.</param>
/// <param name="Picture">SHA-256 of the frame: the 64000 pixels of the screen and its 768 DAC bytes.</param>
/// <param name="Handoff">SHA-256 of the 12000-byte hand-off block at that moment.</param>
/// <param name="Calls">The calls to the outside since the previous checkpoint, as the side recorded them (JSON).</param>
/// <param name="Written">The SHA-256 of every file written so far, by name (JSON), when one changed since the previous checkpoint; else null.</param>
/// <param name="Part">In a whole session: the program that took the entry (INTRO, FINAL, GAME, ...); else null.</param>
/// <param name="Text">In a whole session, while the display is in text mode: SHA-256 of the text page (4000 bytes as at B800:0000, then the cursor's row and column); else null.</param>
internal sealed record Checkpoint(int Number, int? Entry, bool End, string Picture, string Handoff, string Calls, string? Written = null, string? Part = null, string? Text = null)
{
    /// <summary>The checkpoint as one line of checkpoints.jsonl, in the format the original side writes (tools/session/session_store.py).</summary>
    public string ToJsonLine()
    {
        var line = new JsonObject
        {
            ["n"] = Number,
            ["entry"] = End ? JsonValue.Create("end") : Entry is { } key ? JsonValue.Create(key) : null,
            ["service"] = "csharp",
            ["picture"] = Picture,
            ["handoff"] = Handoff,
            ["calls"] = JsonNode.Parse(Calls),
        };
        if (Written is not null)
        {
            line["written"] = JsonNode.Parse(Written);
        }
        if (Part is not null)
        {
            line["part"] = Part;
        }
        if (Text is not null)
        {
            line["text"] = Text;
        }
        return line.ToJsonString();
    }

    /// <summary>Reads a line of checkpoints.jsonl.</summary>
    public static Checkpoint Parse(string line)
    {
        var json = JsonNode.Parse(line)!.AsObject();
        var entry = json["entry"];
        var end = entry is JsonValue v && v.GetValueKind() == JsonValueKind.String;
        return new Checkpoint(
            json["n"]!.GetValue<int>(),
            end || entry is null ? null : entry.GetValue<int>(),
            end,
            json["picture"]!.GetValue<string>(),
            json["handoff"]!.GetValue<string>(),
            json["calls"]?.ToJsonString() ?? "[]",
            json["written"]?.ToJsonString(),
            json["part"]?.GetValue<string>(),
            json["text"]?.GetValue<string>());
    }
}

/// <summary>
/// A run of the original recorded by tools/session/original_side.py: the inputs both sides are fed, the hand-off block
/// before and after, the checkpoints and their pictures. Read only: a recording is evidence and is never changed.
/// </summary>
internal sealed class SessionRecording
{
    private SessionRecording(string folder)
    {
        Folder = folder;
        var inputs = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "inputs.json")))!.AsObject();
        Keys = [.. inputs["keys"]!.AsArray().Select(k => k is null ? (int?)null : k.GetValue<int>())];
        UnixTime = inputs["unix_time"]!.GetValue<long>();
        PartTimes = inputs["part_times"] is JsonArray times ? [.. times.Select(t => t!.GetValue<long>())] : [];
        if (inputs["stick_at_start"] is JsonArray atStart && inputs["sticks"] is JsonArray sticks)
        {
            // item 12: a stick on the game port, at the start and at each entry, and the port's readings the tool's model gave
            StickAtStart = Stick(atStart);
            Sticks = [.. sticks.Select(s => Stick(s!.AsArray()))];
            JoystickAtStart = Reading(inputs["joystick_at_start"]!.AsArray());
            Joystick = [.. inputs["joystick"]!.AsArray().Select(r => Reading(r!.AsArray()))];
        }
        HandoffIn = File.ReadAllBytes(Path.Combine(folder, "handoff-in.bin"));
        HandoffOut = File.ReadAllBytes(Path.Combine(folder, "handoff-out.bin"));
        Checkpoints = [.. File.ReadAllLines(Path.Combine(folder, "checkpoints.jsonl")).Where(l => l.Length > 0).Select(Checkpoint.Parse)];
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")))!.AsObject();
        Stop = meta["stop"]!.GetValue<string>();
        Name = meta["spec"]!["name"]!.GetValue<string>();
        Part = meta["spec"]!["part"]!.GetValue<string>();
        CallsAfter = meta["calls_after"]?.ToJsonString() ?? "[]";
        Spec = meta["spec"]!.AsObject();
        Format = meta["format"]?.GetValue<int>() ?? 1;
        StartImage = meta["start_image"]?.GetValue<string>();
        Unasked = meta["unasked"] is JsonArray unasked ? unasked.Count : 0;
        DriverSegments = meta["driver_segments"]?.GetValue<string>() is { } segments ? Convert.FromHexString(segments) : null;
        DeviceStream = meta["device_stream"]?.GetValue<bool>() ?? false;
        TextAtEnd = meta["text_at_end"]?.GetValue<string>();
        var written = Path.Combine(folder, "written");
        Written = Directory.Exists(written)
            ? Directory.EnumerateFiles(written).ToDictionary(f => Path.GetFileName(f).ToUpperInvariant(), File.ReadAllBytes)
            : [];
        // the earlier recordings this one took its block or disk files from: the session ("PART/name") and the folder used
        var sources = new List<(string Session, string Folder)>();
        if (Spec["handoff_from"]?["recording"] is { } blockSession && meta["handoff_from"]?["recording"] is { } blockFolder)
        {
            sources.Add((blockSession.GetValue<string>(), blockFolder.GetValue<string>()));
        }
        foreach (var (name, source) in Spec["disk"] as JsonObject ?? [])
        {
            if (meta["disk"]?[name.ToUpperInvariant()]?["recording"] is { } diskFolder)
            {
                sources.Add((source!["recording"]!.GetValue<string>(), diskFolder.GetValue<string>()));
            }
        }
        Sources = sources;
        var disk = Path.Combine(folder, "disk");
        Disk = Directory.Exists(disk)
            ? Directory.EnumerateFiles(disk).ToDictionary(f => Path.GetFileName(f).ToUpperInvariant(), File.ReadAllBytes)
            : [];
    }

    /// <summary>The earlier sessions ("PART/name") whose recordings gave this one its hand-off block or disk files, with the recording folder used.</summary>
    public IReadOnlyList<(string Session, string Folder)> Sources { get; }

    /// <summary>
    /// Why the recording no longer fits the recordings it was made from (one of them was recorded again since: the block
    /// or file it took may differ now), or null.
    /// </summary>
    /// <param name="root">The session root.</param>
    public string? StaleAgainstSources(string root)
    {
        foreach (var (session, used) in Sources)
        {
            var latest = Directory.EnumerateDirectories(Path.Combine(root, session), "original-*")
                .Where(f => File.Exists(Path.Combine(f, "meta.json")))
                .Order(StringComparer.Ordinal)
                .LastOrDefault();
            if (latest is null || Path.GetFileName(latest) != Path.GetFileName(used.TrimEnd('/', '\\')))
            {
                return $"the recording is stale: {session} was recorded again since it took its block or file from {Path.GetFileName(used)} (record it again)";
            }
        }
        return null;
    }

    /// <summary>In a whole session with a joystick (item 12): the stick at the start (x, y from -1 to 1, the buttons held), or null without one.</summary>
    public (double X, double Y, int Buttons)? StickAtStart { get; }

    /// <summary>In a whole session with a joystick: the stick from the moment each entry is taken until the next is (one per entry).</summary>
    public IReadOnlyList<(double X, double Y, int Buttons)> Sticks { get; } = [];

    /// <summary>In a whole session with a joystick: the game port's reading at the start, as the original side's model gave it (port bits, X count, Y count).</summary>
    public (int Buttons, int XCount, int YCount)? JoystickAtStart { get; }

    /// <summary>In a whole session with a joystick: the game port's reading for each entry's stick, as the original side's model gave it.</summary>
    public IReadOnlyList<(int Buttons, int XCount, int YCount)> Joystick { get; } = [];

    private static (double X, double Y, int Buttons) Stick(JsonArray stick) =>
        (stick[0]!.GetValue<double>(), stick[1]!.GetValue<double>(), stick.Count > 2 ? stick[2]!.GetValue<int>() : 0);

    private static (int Buttons, int XCount, int YCount) Reading(JsonArray reading) =>
        (reading[0]!.GetValue<int>(), reading[1]!.GetValue<int>(), reading[2]!.GetValue<int>());

    /// <summary>In a whole session: the time of day of each part the launcher starts, in order (the last for the parts after); empty: the session's for all.</summary>
    public IReadOnlyList<long> PartTimes { get; }

    /// <summary>In a whole session: the keys the original's set-up read that the rewrite does not ask for (D-600: the graphics question), not script entries.</summary>
    public int Unasked { get; }

    /// <summary>
    /// In a whole session: the six bytes of the driver segments (SHARED+0x60..0x65) the original's launcher wrote into the
    /// block once it had loaded the drivers (D-602), or null.
    /// </summary>
    public byte[]? DriverSegments { get; }

    /// <summary>
    /// In a whole session from item 8 on: whether the original side recorded the sound device's stream among the calls
    /// (the sound driver's port writes and its timer ticks: <see cref="Tests.DeviceStream"/>).
    /// </summary>
    public bool DeviceStream { get; }

    /// <summary>In a whole session from item 8 on: the text page (its SHA-256, in texts/) when the run ended in text mode, or null.</summary>
    public string? TextAtEnd { get; }

    /// <summary>The characters of the text page the run ended with, row by row (empty when it did not end in text mode).</summary>
    public string TextAtEndCharacters()
    {
        if (TextAtEnd is null)
        {
            return "";
        }
        var page = TextPage(TextAtEnd);
        return string.Join("\n", Enumerable.Range(0, 25).Select(row => new string([.. Enumerable.Range(0, 80).Select(column => (char)page[2 * (row * 80 + column)])]).TrimEnd()));
    }

    /// <summary>A whole session's recorded hand-off block by its SHA-256 (blocks/&lt;sha&gt;.bin), or null when the recording has none.</summary>
    public byte[]? Block(string handoff) =>
        File.Exists(Path.Combine(Folder, "blocks", handoff + ".bin")) ? File.ReadAllBytes(Path.Combine(Folder, "blocks", handoff + ".bin")) : null;

    /// <summary>A recorded text page (4000 bytes as at B800:0000, the cursor's row and column), by its SHA-256.</summary>
    public byte[] TextPage(string text) => File.ReadAllBytes(Path.Combine(Folder, "texts", text + ".txt"));

    /// <summary>The files the session found on the player's disk besides the game's (spec <c>disk</c>: a saved game an earlier session wrote), by their names in capitals.</summary>
    public IReadOnlyDictionary<string, byte[]> Disk { get; }

    /// <summary>The recording format (tools/session/session_store.py FORMAT): from 2 on, a checkpoint holds the written files that changed.</summary>
    public int Format { get; }

    /// <summary>The SHA-256 of the 1 MiB start image the original side ran from (tools/oracle's base_start.mem), or null.</summary>
    public string? StartImage { get; }

    /// <summary>
    /// The picture on the screen when the part starts: the 64000 bytes at A000:0000 of the start image the original side
    /// ran from (what the program before it left there; no part sets the video mode again, so a part that looks at the
    /// keyboard before it draws shows it), found under <paramref name="statesRoot"/> by the spec's start and checked
    /// against <see cref="StartImage"/>; null when the image is not there.
    /// </summary>
    /// <param name="statesRoot">tools/oracle's captured states (build/oracle/states).</param>
    public byte[]? StartScreen(string statesRoot)
    {
        const int screen = 0xa0000, size = 64000;
        var path = Path.Combine(statesRoot, Part, Spec["start"]!.GetValue<string>(), "base_start.mem");
        if (StartImage is null || !File.Exists(path))
        {
            return null;
        }
        var image = File.ReadAllBytes(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(image)) == StartImage ? image[screen..(screen + size)] : null;
    }

    /// <summary>The files the part wrote (created, or wrote into), by their names in capitals, as they were at the end.</summary>
    public IReadOnlyDictionary<string, byte[]> Written { get; }

    /// <summary>The session spec the recording was made from (tools/session/sessions/...), as the original side read it.</summary>
    public JsonObject Spec { get; }

    /// <summary>
    /// Why the recording no longer fits its committed spec (another key script, hand-off change, time of day or start
    /// image), or null when it fits or the spec cannot be found. A stale recording must be made again
    /// (tools/session/original_side.py), never edited.
    /// </summary>
    public string? StaleAgainst(string specFile)
    {
        if (!File.Exists(specFile))
        {
            return null;
        }
        var committed = JsonNode.Parse(File.ReadAllText(specFile))!.AsObject();
        var changed = new[] { "start", "handoff_from", "handoff", "disk", "dos", "unix_time", "keys", "part_times", "graphics_key", "stick" }
            .Where(field => !JsonNode.DeepEquals(committed[field], Spec[field]))
            .ToList();
        return changed.Count == 0 ? null : $"the recording is stale: its spec's {string.Join(", ", changed)} changed since (record it again)";
    }

    /// <summary>The calls to the outside after the last checkpoint (JSON, as in a checkpoint).</summary>
    public string CallsAfter { get; }

    /// <summary>Item 14: the game files the session's disk does not have (spec `dos.missing`), in capitals.</summary>
    public IReadOnlyList<string> Missing => Names("missing");

    /// <summary>Item 14: the files the session's disk refuses to make or to open to write (spec `dos.cannot_create`), in capitals.</summary>
    public IReadOnlyList<string> CannotCreate => Names("cannot_create");

    private List<string> Names(string field) =>
        Spec["dos"]?[field] is JsonArray names ? [.. names.Select(n => n!.GetValue<string>().ToUpperInvariant())] : [];

    /// <summary>The recording's folder.</summary>
    public string Folder { get; }

    /// <summary>The session's name (its spec's file name).</summary>
    public string Name { get; }

    /// <summary>The part of the game the session plays (CODE, CHASE, ...).</summary>
    public string Part { get; }

    /// <summary>The key script resolved: key codes (scan code high, character low), null for a poll that finds no key.</summary>
    public IReadOnlyList<int?> Keys { get; }

    /// <summary>The time of day the game reads, in seconds since 1970.</summary>
    public long UnixTime { get; }

    /// <summary>The hand-off block the part started with.</summary>
    public byte[] HandoffIn { get; }

    /// <summary>The hand-off block as the part left it.</summary>
    public byte[] HandoffOut { get; }

    /// <summary>The checkpoints in order.</summary>
    public IReadOnlyList<Checkpoint> Checkpoints { get; }

    /// <summary>Why the run ended: "exit" (the part ended), "script exhausted", or what the machine could not do.</summary>
    public string Stop { get; }

    /// <summary>A recorded frame: 64000 pixels and 768 DAC bytes.</summary>
    public byte[] Frame(string picture) => File.ReadAllBytes(Path.Combine(Folder, "frames", picture + ".idx"));

    /// <summary>The latest recording of every session of a part under the session root (one per session folder).</summary>
    public static IEnumerable<SessionRecording> LatestOfPart(string root, string part)
    {
        var partFolder = Path.Combine(root, part);
        if (!Directory.Exists(partFolder))
        {
            yield break;
        }
        foreach (var session in Directory.EnumerateDirectories(partFolder).Order(StringComparer.Ordinal))
        {
            var latest = Directory.EnumerateDirectories(session, "original-*")
                .Where(f => File.Exists(Path.Combine(f, "meta.json")))
                .Order(StringComparer.Ordinal)
                .LastOrDefault();
            if (latest is not null)
            {
                yield return new SessionRecording(latest);
            }
        }
    }
}
