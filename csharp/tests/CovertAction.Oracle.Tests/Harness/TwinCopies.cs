using System.Globalization;
using CovertAction.Content;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests.Harness;

/// <summary>
/// The proof, on the player's programs, that one program's copy of a library routine is the same code as another
/// program's copy whose recorded runs the C# reproduces: the two bodies are compared byte for byte, and where they
/// differ the difference must be one this proof can account for. That is a call whose two targets are the same
/// routine (docs/source-map/original-functions.csv, under the common name of docs/source-map/twin-names.csv), a
/// pointer to the same text in both programs' initialised data, or the address of a variable this table lists for
/// both programs (the same place in it on both sides). Everything else must be equal.
/// </summary>
/// <remarks>
/// Used where a copy's own program never runs it on a recorded route, or runs it only where the C# cannot replay
/// it: the copy's test then stands on the twin's recorded runs. The proof is a check that runs on the game files
/// and is skipped without them; when the copy's own vectors compare, they are replayed as well.
/// </remarks>
internal static class TwinCopies
{
    /// <summary>A variable both copies address, at its place in each program, and how many bytes of it they may.</summary>
    internal sealed record Variable(string Name, int Copy, int Proven, int Size);

    /// <summary>A copy, the copy it is the same routine as, and the vector name of that proven copy.</summary>
    internal sealed record Twin(string Module, string Coordinate, string ProvenModule, string ProvenCoordinate, string ProvenName, Variable[] Data);

    // GAME's copies against FINAL's (the animation player, the text routines both programs link)
    private static readonly Variable[] GameFinal =
    [
        new("Txt and the byte before it (trim_text_to_width cuts through D_7201)", 0x7201, 0x79e1, 1 + 1024),
        new("pan_port", 0x426c, 0x4afc, 2),
        new("the sprites' images (GAME pan_sprite_segs, FINAL sprite_images), by an image byte", 0xa1f4, 0xa9fe, 0x200),
        new("the paragraph of the far data the sprites lie in (GAME F_10e2, FINAL 0fb4; oracle_config.FAR_DATA)", 0x10e2, 0x0fb4, 1),
    ];

    // BUG's copies against TAC's (the clue and event texts; PartAddresses has most of them)
    private static readonly Variable[] BugTac =
    [
        new("ps", 0x0172, 0x06fe, 0x400),
        new("cl", 0x1172, 0x16fe, 0x4e0),
        new("Diff", 0x1652, 0x91e0, 2),
        new("the clue file's token table (BUG msg_tokens, TAC clue_pattern)", 0x20ba, 0x2bbc, 10),
        new("msg_subst", 0x231c, 0x2e1e, 14),
        new("the message tag (msg_tag_buf)", 0x3a2e, 0x72d4, 16),
        new("Txt", 0x5542, 0x8dda, 512),
        new("the first substitution text (TAC subst_str_a)", 0x59bc, 0x9902, 40),
        new("the second substitution text (TAC subst_str_b)", 0x59e4, 0x992a, 40),
        new("ct", 0x5e32, 0x9e18, 0x200),
        new("the face of the box (BUG popup_face, TAC picface)", 0x6758, 0xa8e6, 2),
        new("ev", 0x6760, 0xa8f0, 0x400),
        new("the clue's way (BUG clue_method, TAC clue_icon)", 0x763c, 0xbb76, 2),
        new("TheCrime", 0x7640, 0xbb7e, 2),
        new("NACTIVITY", 0x77b0, 0xbdf2, 2),
        new("ac", 0x77b4, 0xbdf6, 0x600),
        new("org", 0x7fc0, 0xc604, 0x200),
    ];

    /// <summary>The copies proven this way, by their program and the name their test calls them by.</summary>
    public static readonly IReadOnlyDictionary<(string Module, string Name), Twin> ByName = new Dictionary<(string, string), Twin>
    {
        // GAME's animation player is never started on a recorded route; its draw_actor is, but every recorded call
        // draws a sprite whose parent is not on the stage, whose place the original takes from an uninitialised local
        [("GAME", "run_pan_scene")] = new("GAME", "00f7:b44a", "FINAL", "0100:ac6c", "play_animation", GameFinal),
        [("GAME", "draw_actor")] = new("GAME", "00f7:be3d", "FINAL", "0100:b65f", "draw_sprite", GameFinal),
        // GAME has no caller of its copy
        [("GAME", "trim_text_to_width")] = new("GAME", "00f7:780c", "FINAL", "0100:3a22", "trim_text_to_width", GameFinal),
        // BUG's recorded route is a practice: no campaign, so no clue or event text
        [("BUG", "load_event_text")] = new("BUG", "00e8:3fc8", "TAC", "00e8:a84c", "build_event_text", BugTac),
        [("BUG", "expand_clue_text")] = new("BUG", "00e8:2fe9", "TAC", "00e8:986d", "build_clue_sentence", BugTac),
        [("BUG", "activity_clue_text")] = new("BUG", "00e8:2fc9", "TAC", "00e8:984d", "activity_clue_text", BugTac),
        [("BUG", "lookup_clue_text")] = new("BUG", "00e8:2d22", "TAC", "00e8:95a6", "lookup_clue_text", BugTac),
    };

    /// <summary>
    /// Runs a copy's recorded cases; for a copy of <see cref="ByName"/> it first proves it the same routine as its
    /// twin, and when its own cases give nothing to compare it stands on the twin's (which must have vectors).
    /// </summary>
    /// <returns>The tally of the copy's own cases, or null when only the proof stands for it.</returns>
    public static OracleTally? CasesOrTwin(string module, string name, Func<OracleTally> cases, ITestOutputHelper output)
    {
        if (!ByName.TryGetValue((module, name), out var twin))
        {
            return cases();
        }
        output.WriteLine(Prove(twin));
        try
        {
            return cases();
        }
        catch (SkipException e)
        {
            Skip.If(OracleVectors.ForFunction(twin.ProvenModule, twin.ProvenName).Count == 0,
                $"{module} {name}: {e.Message}; and no oracle vectors for its twin {twin.ProvenModule} {twin.ProvenName}");
            output.WriteLine($"TWIN {module} {name}: none of its own cases compares ({e.Message}); it stands on {twin.ProvenModule} {twin.ProvenName}'s");
            return null;
        }
    }

    /// <summary>Compares the two copies; fails naming every difference the proof cannot account for.</summary>
    /// <returns>A line saying what was compared and what differs.</returns>
    public static string Prove(Twin twin)
    {
        var copy = Program.Of(twin.Module);
        var proven = Program.Of(twin.ProvenModule);
        var (copyStart, copyBody) = copy.Body(twin.Coordinate);
        var (provenStart, provenBody) = proven.Body(twin.ProvenCoordinate);
        Assert.True(copyBody.Length == provenBody.Length,
            $"{twin.Module} {twin.Coordinate} has {copyBody.Length} bytes, {twin.ProvenModule} {twin.ProvenCoordinate} {provenBody.Length}");
        var unexplained = new List<string>();
        var explained = new Dictionary<string, int>();
        for (var i = 0; i < copyBody.Length; i++)
        {
            if (copyBody[i] == provenBody[i])
            {
                continue;
            }
            // the differing byte is the low byte of a word here, or the high byte of the word before
            string? why = null;
            var at = i;
            foreach (var w in new[] { i, i - 1 })
            {
                if (w >= 0 && w + 1 < copyBody.Length && (why = Explain(twin, copy, copyStart, copyBody, proven, provenStart, provenBody, w)) is not null)
                {
                    at = w;
                    break;
                }
            }
            if (why is null)
            {
                unexplained.Add($"+{i:x4}: {copyBody[i]:x2} / {provenBody[i]:x2}");
                continue;
            }
            explained[why] = explained.GetValueOrDefault(why) + 1;
            i = at + 1;
        }
        Assert.True(unexplained.Count == 0,
            $"{twin.Module} {twin.Coordinate} and {twin.ProvenModule} {twin.ProvenCoordinate} differ where no call, text or listed variable accounts for it: {string.Join(", ", unexplained)}");
        return $"TWIN {twin.Module} {twin.Coordinate} = {twin.ProvenModule} {twin.ProvenCoordinate}: {copyBody.Length} bytes, the same apart from "
            + (explained.Count == 0 ? "nothing" : string.Join("; ", explained.Select(e => $"{e.Key} ({e.Value})")));
    }

    /// <summary>Why the words at <paramref name="w"/> of the two bodies may differ, or null.</summary>
    private static string? Explain(Twin twin, Program copy, int copyStart, byte[] a, Program proven, int provenStart, byte[] b, int w)
    {
        var x = a[w] | a[w + 1] << 8;
        var y = b[w] | b[w + 1] << 8;
        if (w > 0 && a[w - 1] == b[w - 1] && a[w - 1] is 0xe8 or 0x9a)
        {
            // a near call (rel16 in the function's frame) or a far call (offset, then the segment word)
            int Target(Program p, int start, byte[] body, int word)
            {
                if (body[w - 1] == 0xe8)
                {
                    var frame = start - p.FrameOffset(start);
                    return frame + ((p.FrameOffset(start) + w + 2 + (short)word) & 0xffff);
                }
                return (body[w + 2] | body[w + 3] << 8) * 16 + word;
            }
            var to = copy.NameAt(Target(copy, copyStart, a, x));
            var toProven = proven.NameAt(Target(proven, provenStart, b, y));
            return to is not null && toProven is not null && Common(twin.Module, to) == Common(twin.ProvenModule, toProven)
                ? $"calls of {Common(twin.Module, to)}"
                : null;
        }
        foreach (var v in twin.Data)
        {
            if (x - v.Copy is var offset && offset >= 0 && offset < v.Size && y - v.Proven == offset)
            {
                return v.Name;
            }
        }
        var text = copy.TextAt(x);
        return text.Length > 0 && text.AsSpan().SequenceEqual(proven.TextAt(y)) ? "pointers to the same text" : null;
    }

    private static string Common(string module, string name) => TwinNames.Value.GetValueOrDefault((module, name), name);

    private static readonly Lazy<Dictionary<(string Module, string Name), string>> TwinNames = new(() =>
        File.ReadAllLines(Path.Combine(Repository.CSharp, "docs", "source-map", "twin-names.csv")).Skip(1)
            .Select(line => line.Split(','))
            .Where(f => f.Length >= 3)
            .ToDictionary(f => (f[0], f[1]), f => f[2]));

    /// <summary>The functions of every program: where each starts (linear, in its load image) and its name.</summary>
    private static readonly Lazy<Dictionary<string, SortedList<int, string>>> Functions = new(() =>
    {
        var all = new Dictionary<string, SortedList<int, string>>();
        foreach (var line in File.ReadAllLines(Path.Combine(Repository.CSharp, "docs", "source-map", "original-functions.csv")).Skip(1))
        {
            var fields = line.Split(',');
            var (module, linear) = Linear(fields[0]);
            if (!all.TryGetValue(module, out var list))
            {
                all[module] = list = [];
            }
            list[linear] = fields[1];
        }
        return all;
    });

    private static (string Module, int Linear) Linear(string coordinate)
    {
        var parts = coordinate.Split(' ', ':');
        return (parts[0], int.Parse(parts[1], NumberStyles.HexNumber) * 16 + int.Parse(parts[2], NumberStyles.HexNumber));
    }

    /// <summary>One of the player's programs, as the proof reads it.</summary>
    private sealed class Program
    {
        private static readonly Dictionary<string, Program> Loaded = [];

        private readonly string _module;
        private readonly byte[] _image;
        private readonly byte[] _data;
        private readonly Dictionary<int, int> _frames = [];

        private Program(string module, Executable exe)
        {
            _module = module;
            _image = exe.Image;
            _data = exe.DataSegment();
        }

        public static Program Of(string module)
        {
            lock (Loaded)
            {
                if (!Loaded.TryGetValue(module, out var program))
                {
                    var game = GameFolder.FromEnvironment() ?? new GameFolder(Path.Combine(Repository.Root, "game", "Covert Action", "Covert"));
                    Skip.If(game.Find(module + ".EXE") is null, $"no {module}.EXE: set COVERT_GAME_DIR to a folder with the original game");
                    Loaded[module] = program = new Program(module, Executable.Read(game.Read(module + ".EXE")));
                }
                return program;
            }
        }

        /// <summary>A function's start and its bytes up to the next function, without the padding behind it.</summary>
        public (int Start, byte[] Bytes) Body(string coordinate)
        {
            var (_, start) = Linear($"{_module} {coordinate}");
            var starts = Functions.Value[_module].Keys;
            var end = starts.FirstOrDefault(s => s > start, _image.Length);
            var length = end - start;
            while (length > 0 && _image[start + length - 1] is 0x00 or 0x90)
            {
                length--;
            }
            _frames[start] = int.Parse(coordinate.Split(':')[1], NumberStyles.HexNumber);
            return (start, _image.AsSpan(start, length).ToArray());
        }

        /// <summary>A function's offset in its frame (its coordinate's offset).</summary>
        public int FrameOffset(int start) => _frames[start];

        public string? NameAt(int linear) => Functions.Value[_module].GetValueOrDefault(linear);

        /// <summary>The text at an address of the initialised data, without its end mark (empty when there is none).</summary>
        public byte[] TextAt(int address)
        {
            var end = Array.IndexOf(_data, (byte)0, address);
            return end <= address ? [] : _data.AsSpan(address, end - address).ToArray();
        }
    }
}
