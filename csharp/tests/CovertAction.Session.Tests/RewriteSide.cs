using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CovertAction.Content;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Launcher;
using CovertAction.Persistence;
using CovertAction.Platform;

namespace CovertAction.Session.Tests;

/// <summary>What the rewrite did with a session's inputs: its checkpoints, pictures and the hand-off block it left.</summary>
/// <param name="Checkpoints">The checkpoints in order.</param>
/// <param name="Frames">Each picture once, by its SHA-256: 64000 pixels and 768 DAC bytes.</param>
/// <param name="HandoffOut">The hand-off block as the part left it.</param>
/// <param name="Stop">"script exhausted" when the part asked for a key after the last entry, else "exit".</param>
/// <param name="CallsAfter">The calls to the outside after the last checkpoint (JSON, as in a checkpoint).</param>
/// <param name="Written">The files the part wrote, by their names in capitals, as they were at the end.</param>
/// <param name="Registered">The registered differences of a whole session the run met outside the pictures (D-600, D-602), in words.</param>
internal sealed record RewriteRun(IReadOnlyList<Checkpoint> Checkpoints, IReadOnlyDictionary<string, byte[]> Frames, byte[] HandoffOut, string Stop, string CallsAfter, IReadOnlyDictionary<string, byte[]> Written, IReadOnlyList<string>? Registered = null);

/// <summary>
/// The C# side of the session proof: runs the part a recording's hand-off block names through the product's own
/// path (<see cref="Game.RunPart"/> on the headless host), fed the same keys and time of day, and records a
/// checkpoint each time the game takes an entry of the key list (csharp/docs/SESSION-PROOF.md).
/// </summary>
internal static class RewriteSide
{
    /// <summary>Plays the recording's inputs on the rewrite.</summary>
    /// <param name="original">The recording whose inputs are played.</param>
    /// <param name="folder">The player's game folder.</param>
    /// <param name="unixTime">A different time of day than the recording's (to check that a changed input is seen); null: the recording's.</param>
    /// <param name="writeFolder">A new folder for the files the game writes (the player's folder is never written); null: nothing can be written.</param>
    /// <param name="startScreen">The picture the program before the part left on the screen (the original side's start image), or null: black.</param>
    public static RewriteRun Run(SessionRecording original, GameFolder folder, long? unixTime = null, string? writeFolder = null, byte[]? startScreen = null)
    {
        var keys = original.Keys.Select(k => k ?? HeadlessHost.NoKeyYet);
        var host = new HeadlessHost(keys, unixTime ?? original.UnixTime);
        var game = new Game(original.Disk.Count == 0 && original.Missing.Count == 0 ? folder : PlayersDisk(original, folder, writeFolder), host, writeFolder);
        // item 14: a file the session's disk refuses to make: something Windows does not let the game write in its place
        // (a folder of that name), as a read-only save in the saves folder; the folder is no written file
        foreach (var name in original.CannotCreate)
        {
            Directory.CreateDirectory(Path.Combine(writeFolder ?? throw new ArgumentException("a session whose disk refuses files needs a write folder", nameof(writeFolder)), name));
        }
        // the screen as the part finds it: the launcher's drivers keep the picture of the program before (GAME's last
        // scene before a raid), and a part that looks at the keyboard before it draws shows it (the escape raid)
        startScreen?.CopyTo(game.Graphics.Pages[0], 0);
        var shared = new SharedState();
        SharedBlockCodec.Layout.Load(original.HandoffIn, shared);
        // the campaign tables behind the fields (the case the menus part hands to the campaign, or one handed back)
        SharedBlockCodec.LoadCampaign(original.HandoffIn, shared);

        var checkpoints = new List<Checkpoint>();
        var frames = new Dictionary<string, byte[]>();
        var ended = false;
        // the calls to the outside since the last checkpoint, as the original side writes them: ["play_sound", effect, ...]
        var calls = new JsonArray();
        var writtenSeen = "{}";
        // once the script is used up the original side stops at that poll; the rewrite runs on until it sees the window
        // closed, and what it asks for meanwhile is not part of the session (a raid's music for the frame, the escape raid)
        game.SoundRequested += arguments =>
        {
            if (!ended)
            {
                calls.Add(new JsonArray([JsonValue.Create("play_sound"), .. arguments.Select(a => (JsonNode)JsonValue.Create(a))]));
            }
        };
        host.EntryTaken += entry =>
        {
            var end = entry == HeadlessHost.EndOfKeys;
            ended |= end;
            var frame = new byte[64000 + 768];
            game.Graphics.Screen.CopyTo(frame);
            game.Graphics.Palette.CopyTo(frame.AsSpan(64000));
            var picture = Sha(frame);
            frames.TryAdd(picture, frame);
            int? key = end || entry == HeadlessHost.NoKeyYet ? null : entry;
            // the files written so far, when they changed since the previous checkpoint (as the original side records them)
            var written = WrittenState(writeFolder);
            var changed = written == writtenSeen ? null : written;
            writtenSeen = written;
            checkpoints.Add(new Checkpoint(checkpoints.Count, key, end, picture, Sha(Handoff(shared, original.HandoffIn)), calls.ToJsonString(), changed));
            calls.Clear();
        };

        game.RunPart(shared);
        var written = writeFolder is not null && Directory.Exists(writeFolder)
            ? Directory.EnumerateFiles(writeFolder).ToDictionary(f => Path.GetFileName(f).ToUpperInvariant(), File.ReadAllBytes)
            : [];
        return new RewriteRun(checkpoints, frames, Handoff(shared, original.HandoffIn), ended ? "script exhausted" : "exit", calls.ToJsonString(), written);
    }

    /// <summary>
    /// The programs of the original's launcher table (COVERT's module_names, DGROUP 0124: COVERT 0000:0010),
    /// by the hand-off's "next part" code: the part a whole session's checkpoint belongs to on the rewrite's side.
    /// </summary>
    private static readonly string[] LauncherPrograms =
        ["GAME", "TAC", "CODE", "CHASE", "BUG", "CHASE", "CHASE", "TAC", "TAC", "FINAL", "TAC", "FINAL", "BUG", "TAC", "CHASE", "CODE", "BUG", "FINAL"];

    /// <summary>
    /// Plays a whole session (csharp/docs/SESSION-PROOF.md, item 7) on the rewrite: the product's own launcher flow
    /// (<see cref="Game.Main(SharedState)"/>: the set-up screen with the original's questions, then the parts by their hand-off codes), fed the recording's
    /// keys, the time of day the original side gave each part, and the player's disk. Each checkpoint carries the part
    /// running (the launcher table's program for the code <see cref="Game.PartStarting"/> names; INTRO before) and, while
    /// the set-up's text page is shown, the page.
    /// </summary>
    /// <param name="original">The recording whose inputs are played.</param>
    /// <param name="folder">The player's game folder.</param>
    /// <param name="writeFolder">A new folder for the files the game writes.</param>
    /// <param name="withStick">Whether the host gets the recording's stick (item 12); false only to show that the comparison sees it.</param>
    public static RewriteRun RunWhole(SessionRecording original, GameFolder folder, string writeFolder, bool withStick = true)
    {
        var keys = original.Keys.Select(k => k ?? HeadlessHost.NoKeyYet);
        // item 8: the sound driver plays on a device that never asks for frames; its timer interrupts are given as the
        // original's driver got them (as many before each sound request and each checkpoint)
        var output = new TickedOutput();
        var host = new HeadlessHost(keys, original.UnixTime) { TimeOfDay = original.UnixTime, Audio = output };
        // the original's set-up questions, answered by the recording's keys as on the original side (the player's game
        // shows only the set-up's title and takes the answers from the launcher's settings, D-603)
        var options = new GameOptions(AskSetUpQuestions: true);
        if (withStick)
        {
            GiveTheStick(original, host);
        }
        var game = new Game(original.Disk.Count == 0 ? folder : PlayersDisk(original, folder, writeFolder), host, writeFolder, options);
        var shared = new SharedState();
        var part = "INTRO";
        var parts = 0;
        var checkpoints = new List<Checkpoint>();
        var frames = new Dictionary<string, byte[]>();
        var ended = false;
        var calls = new JsonArray();
        var writtenSeen = "{}";
        var registered = new List<string>();
        var schedule = new DeviceStream.Schedule(original);
        var ticks = 0;
        var requests = 0;
        void TicksUpTo(int target)
        {
            if (output.Driver is { } driver && target > ticks)
            {
                driver.RunInterrupts(target - ticks);
            }
        }
        game.PartStarting += code =>
        {
            part = code >= 0 && code < LauncherPrograms.Length ? LauncherPrograms[code] : $"code {code}";
            // the time of day the original side gives this part (its seed): the spec's for the n-th part, the last one for the parts after
            host.TimeOfDay = original.PartTimes.Count == 0 ? original.UnixTime : original.PartTimes[Math.Min(parts++, original.PartTimes.Count - 1)];
        };
        game.SoundDeviceWritten += e =>
        {
            ticks += e.Kind == CovertAction.Audio.SoundDeviceEventKind.TimerTick ? 1 : 0;
            if (!ended)
            {
                calls.Add(DeviceStream.Call(e));
            }
        };
        game.SoundRequested += arguments =>
        {
            if (!ended)
            {
                TicksUpTo(schedule.BeforeRequest(requests++));
                calls.Add(new JsonArray([JsonValue.Create("play_sound"), .. arguments.Select(a => (JsonNode)JsonValue.Create(a))]));
            }
        };
        byte[] Block()
        {
            var block = Handoff(shared, original.HandoffIn);
            if (part != "INTRO" && original.DriverSegments is { } segments)
            {
                // D-602: the rewrite loads no driver programs; the original's launcher writes their segments into the
                // block once it has loaded them, and the comparison takes its six bytes (SHARED+0x60..0x65)
                segments.CopyTo(block, 0x60);
            }
            return block;
        }
        host.EntryTaken += entry =>
        {
            var end = entry == HeadlessHost.EndOfKeys;
            if (!ended)
            {
                TicksUpTo(schedule.AtCheckpoint(checkpoints.Count));
            }
            ended |= end;
            var frame = new byte[64000 + 768];
            game.Graphics.Screen.CopyTo(frame);
            game.Graphics.Palette.CopyTo(frame.AsSpan(64000));
            var picture = Sha(frame);
            frames.TryAdd(picture, frame);
            string? text = null;
            if (game.TextPage is { } page)
            {
                var bytes = TextBytes(page);
                text = Sha(bytes);
                frames.TryAdd("text-" + text, bytes);
            }
            int? key = end || entry == HeadlessHost.NoKeyYet ? null : entry;
            var written = WrittenState(writeFolder, (name, bytes) => SavedWithDriverSegments(name, bytes, original.DriverSegments));
            var changed = written == writtenSeen ? null : written;
            writtenSeen = written;
            var block = Block();
            var handoff = Sha(block);
            // the block itself, for the registered differences of the block across the parts (CA-1 after a raid)
            frames.TryAdd("block-" + handoff, block);
            checkpoints.Add(new Checkpoint(checkpoints.Count, key, end, picture, handoff, calls.ToJsonString(), changed, part, text));
            calls.Clear();
        };

        game.Main(shared);
        if (!ended)
        {
            // the ticks up to the end of the last part (the launcher's own end has no counterpart: D-602)
            TicksUpTo(schedule.AtEnd);
        }
        if (original.DriverSegments is not null)
        {
            registered.Add("D-602 (the driver segments SHARED+0x60..0x65 in the block from the first part on, and at 0x50..0x55 of a saved game: the rewrite loads no driver programs)");
        }
        if (original.Unasked > 0)
        {
            registered.Add($"D-600 (the set-up's graphics question: {original.Unasked} key the original read that the rewrite does not ask for)");
        }
        var files = Directory.Exists(writeFolder)
            ? Directory.EnumerateFiles(writeFolder).ToDictionary(f => Path.GetFileName(f).ToUpperInvariant(), f => SavedWithDriverSegments(Path.GetFileName(f).ToUpperInvariant(), File.ReadAllBytes(f), original.DriverSegments))
            : [];
        return new RewriteRun(checkpoints, frames, Block(), ended ? "script exhausted" : "exit", calls.ToJsonString(), files, registered);
    }

    /// <summary>
    /// Item 12: a session with a stick on the game port. The host's joystick is the product's model of the stick
    /// (<see cref="JoystickReading.FromStick"/>, U-12) at the start and, from the moment the game takes each entry, that
    /// entry's stick, as the original side answers the port's reads. The two models must give the same readings: a
    /// difference stops the run before anything is compared.
    /// </summary>
    private static void GiveTheStick(SessionRecording original, HeadlessHost host)
    {
        if (original.StickAtStart is not { } atStart)
        {
            return;
        }
        static JoystickReading Model((double X, double Y, int Buttons) stick) => JoystickReading.FromStick(stick.X, stick.Y, stick.Buttons);
        static void Agree(JoystickReading model, (int Buttons, int XCount, int YCount) tool, string where)
        {
            if ((model.Buttons, model.XCount, model.YCount) != tool)
            {
                throw new InvalidOperationException($"the joystick models differ {where}: JoystickReading.FromStick gives {model}, the original side's {tool}");
            }
        }
        var readings = original.Sticks.Select(Model).ToArray();
        Agree(Model(atStart), original.JoystickAtStart!.Value, "at the start");
        for (var i = 0; i < readings.Length; i++)
        {
            Agree(readings[i], original.Joystick[i], $"at entry {i}");
        }
        host.Joystick = Model(atStart);
        var taken = 0;
        host.EntryTaken += entry =>
        {
            if (entry != HeadlessHost.EndOfKeys && taken < readings.Length)
            {
                host.Joystick = readings[taken++];
            }
        };
    }

    /// <summary>A text page as the original side records it: character and attribute of every cell (as at B800:0000), then the cursor's row and column.</summary>
    private static byte[] TextBytes(TextScreen page)
    {
        var bytes = new byte[TextScreen.Columns * TextScreen.Rows * 2 + 2];
        for (var i = 0; i < TextScreen.Columns * TextScreen.Rows; i++)
        {
            bytes[2 * i] = page.Characters[i];
            bytes[2 * i + 1] = page.Attributes[i];
        }
        bytes[^2] = (byte)page.CursorRow;
        bytes[^1] = (byte)page.CursorColumn;
        return bytes;
    }

    /// <summary>
    /// The player's folder with the files the session finds on his disk besides the game's (the recording's disk/: a
    /// saved game an earlier session wrote), and without the game files the spec's `dos` takes away, as the original side reads them: a copy of the game folder beside the write
    /// folder, so that they are read like the player's own files and become written files only once the game writes them.
    /// </summary>
    private static GameFolder PlayersDisk(SessionRecording original, GameFolder folder, string? writeFolder)
    {
        var beside = Path.GetDirectoryName(writeFolder ?? throw new ArgumentException("a session with disk files needs a write folder", nameof(writeFolder)))!;
        var disk = Path.Combine(beside, "disk");
        Directory.CreateDirectory(disk);
        foreach (var file in Directory.EnumerateFiles(folder.Path))
        {
            File.Copy(file, Path.Combine(disk, Path.GetFileName(file)));
        }
        // item 14: the game files the session takes off the player's disk
        foreach (var name in original.Missing)
        {
            if (folder.Find(name) is { } players)
            {
                File.Delete(Path.Combine(disk, Path.GetFileName(players)));
            }
        }
        foreach (var (name, bytes) in original.Disk)
        {
            if (folder.Find(name) is { } players)
            {
                File.Delete(Path.Combine(disk, Path.GetFileName(players)));
            }
            File.WriteAllBytes(Path.Combine(disk, name), bytes);
        }
        return new GameFolder(disk);
    }

    /// <summary>The SHA-256 of every file in the write folder, by its name in capitals, in name order (JSON).</summary>
    private static string WrittenState(string? writeFolder, Func<string, byte[], byte[]>? asCompared = null)
    {
        var state = new JsonObject();
        if (writeFolder is not null && Directory.Exists(writeFolder))
        {
            foreach (var file in Directory.EnumerateFiles(writeFolder).Select(f => Path.GetFileName(f).ToUpperInvariant()).Order(StringComparer.Ordinal))
            {
                var bytes = File.ReadAllBytes(Path.Combine(writeFolder, file));
                state[file] = Sha(asCompared is null ? bytes : asCompared(file, bytes));
            }
        }
        return state.ToJsonString();
    }

    /// <summary>The place of the driver segments in a saved game: the file holds the block from SHARED+0x10 on (SaveFileCodec).</summary>
    private const int SavedDriverSegments = 0x60 - 0x10;

    /// <summary>
    /// D-602 in a saved game (CV?.SVE): the file holds the hand-off block's head with the driver segments, which the
    /// original's launcher wrote and the rewrite has as 0. The comparison takes the original's six bytes where the
    /// rewrite's are 0; any other value there is still compared.
    /// </summary>
    private static byte[] SavedWithDriverSegments(string name, byte[] bytes, byte[]? segments)
    {
        if (segments is null || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^CV\d\.SVE$")
            || bytes.Length < SavedDriverSegments + segments.Length
            || bytes.AsSpan(SavedDriverSegments, segments.Length).ContainsAnyExcept((byte)0))
        {
            return bytes;
        }
        var compared = (byte[])bytes.Clone();
        segments.CopyTo(compared, SavedDriverSegments);
        return compared;
    }

    /// <summary>The hand-off block the state stands for: the start block with the modelled fields and the campaign tables written over it.</summary>
    private static byte[] Handoff(SharedState shared, byte[] start)
    {
        var block = (byte[])start.Clone();
        SharedBlockCodec.Layout.Store(shared, block);
        SharedBlockCodec.StoreCampaign(shared, block);
        return block;
    }

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
