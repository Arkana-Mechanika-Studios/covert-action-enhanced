using System.Text.Json;
using CovertAction.Tests.Shared;

namespace CovertAction.Oracle.Tests.Harness;

/// <summary>
/// One call the original made across a boundary while a function ran. The recording fakes replay these: they check
/// that the C# makes the same call and hand back what the original was given.
/// </summary>
/// <remarks>
/// The generator (tools/oracle/run_function.py) names its three kinds of boundary like this:
/// a driver thunk by the routine's name in symbols/names.txt ("draw_string"; an unnamed slot by its label), with the
/// words the game pushed as arguments; a software interrupt as "int21.2a" (interrupt and AH in hex), with the
/// registers AX, BX, CX, DX, SI, DI as arguments; a port read or write as "inb", "outb", "inw", "outw" with the port
/// (and the value written).
/// </remarks>
/// <param name="Target">The boundary's name, without a leading underscore.</param>
/// <param name="Args">
/// The arguments; each a 16-bit word, or for a pointer whose target the generator captured at the time of the call an
/// object: <c>{"ptr": address, "str": base64}</c> (a C string, without its end mark) or <c>{"ptr": address, "mem": base64}</c>
/// (a buffer of fixed length).
/// </param>
/// <param name="Ret">What the call returned in AX (DX:AX for a long), null when unknown.</param>
/// <param name="Input">
/// The answer the generator scripted for this call (the entry of the case's <c>inputs</c> at the call's position):
/// a number for a thunk, a port or a library routine (the same as <paramref name="Ret"/>), an object of register
/// values for an interrupt.
/// </param>
/// <param name="Out">Bytes a library routine wrote through a pointer argument (a file read), or null.</param>
/// <param name="FileFlags">
/// For a read of a text file through the C library: the library's byte for the handle in its table of open files
/// (<c>_osfile</c>) after the read, as the oracle writes it (0x80 text, 0x04 the bytes read began with a line feed,
/// 0x02 a Ctrl-Z was met); null for older vectors and other calls.
/// </param>
internal sealed record BoundaryCall(string Target, IReadOnlyList<JsonElement> Args, long? Ret, JsonElement? Input, byte[]? Out,
    int? FileFlags = null);

/// <summary>
/// One run of an original function by the oracle generator: the memory it started from, its arguments, the answers
/// the outside world gave it, and everything it left behind.
/// </summary>
internal sealed class OracleCase
{
    /// <summary>The case's name within its vector, for messages.</summary>
    public required string Id { get; init; }

    /// <summary>Where the generator took the case from (a captured state of a real session, a fuzzed variant), for messages.</summary>
    public string Origin { get; init; } = "";

    /// <summary>
    /// Whether the case is a state the typed model must reproduce: a state captured in a real session, or a fuzzed one
    /// in which every changed variable stayed inside its range (the generator's <c>realistic</c> flag). The other
    /// cases broke something the model takes for granted (a pointer, a byte that is no variable) and prove nothing
    /// either way.
    /// </summary>
    public required bool Realistic { get; init; }

    /// <summary>The function's arguments as 16-bit words, in C order (a long takes two: low word first).</summary>
    public required short[] Args { get; init; }

    /// <summary>The module's whole data segment before the call.</summary>
    public required byte[] DgroupIn { get; init; }

    /// <summary>The module's whole data segment after the call.</summary>
    public required byte[] DgroupOut { get; init; }

    /// <summary>The 12000-byte hand-off block before the call, or null when the case has none.</summary>
    public byte[]? SharedIn { get; init; }

    /// <summary>The hand-off block after the call, or null.</summary>
    public byte[]? SharedOut { get; init; }

    /// <summary>AX on return: an int result.</summary>
    public required short Ax { get; init; }

    /// <summary>DX on return: with AX, a long result.</summary>
    public required short Dx { get; init; }

    /// <summary>The boundary calls the original made, in order, each with the answer scripted for it.</summary>
    public required IReadOnlyList<BoundaryCall> Calls { get; init; }

    /// <summary>
    /// Byte ranges of the data segment (start inclusive, end exclusive) that are not compared: the stack, which shares
    /// the segment with the variables (the case's <c>stack_mask</c>), and any range the vector lists under <c>ignore</c>.
    /// </summary>
    public required IReadOnlyList<(int Start, int End)> Ignore { get; init; }

    /// <summary>Whether the generator said where the stack is; only then can a change outside the modelled fields be told from stack noise.</summary>
    public required bool IgnoreDeclared { get; init; }

    /// <summary>The registers on entry, for assembly routines that take their arguments in registers (ax, bx, ...); empty for C functions.</summary>
    public IReadOnlyDictionary<string, ushort> RegsIn { get; init; } = new Dictionary<string, ushort>();

    /// <summary>The registers on return, for such routines.</summary>
    public IReadOnlyDictionary<string, ushort> RegsOut { get; init; } = new Dictionary<string, ushort>();

    /// <summary>
    /// Memory outside the data segment and the hand-off block that the function read before writing it, by linear
    /// address (segment * 16 + offset, with the segment values of <see cref="OracleVector.Segments"/>): interrupt
    /// vectors, the pointer at 0000:04F0, driver headers.
    /// </summary>
    public IReadOnlyDictionary<int, byte[]> FarIn { get; init; } = new Dictionary<int, byte[]>();

    /// <summary>The final values of the bytes the function wrote outside the data segment and the hand-off block.</summary>
    public IReadOnlyDictionary<int, byte[]> FarOut { get; init; } = new Dictionary<int, byte[]>();

    /// <summary>The exit code when the function ended the program (DOS function 4Ch), else null.</summary>
    public int? Exit { get; init; }

    /// <summary>
    /// The segments the module was loaded at in the sessions the vector's cases come from (one per recorded route):
    /// a test that must place memory of the module's own block (a far data segment) tries each.
    /// </summary>
    public IReadOnlyList<ushort> LoadSegments { get; init; } = [];
}

/// <summary>
/// The segment values of the session a vector was captured in. They appear inside the data: far pointers kept in the
/// data segment or the hand-off block, saved return addresses. The C# has no segments, so a test that meets such a
/// word compares it through this table or leaves it out.
/// </summary>
/// <param name="Psp">The program segment prefix of the module.</param>
/// <param name="Load">The segment the module was loaded at (frame 0000 of the labels).</param>
/// <param name="Dgroup">The data segment.</param>
/// <param name="Shared">The hand-off block as "segment:offset".</param>
internal sealed record OracleSegments(ushort Psp, ushort Load, ushort Dgroup, string Shared);

/// <summary>A vector file: the cases of one original function.</summary>
/// <param name="Module">The module in upper case: CODE, GAME, FINAL, TAC, BUG, CHASE.</param>
/// <param name="Label">The generator's label for the vector (its file name, the function's label sub_module_frame_offset).</param>
/// <param name="Name">The function's name in symbols/names.txt without the leading underscore, or its label when it has none.</param>
/// <param name="Cases">The recorded runs.</param>
/// <param name="Segments">The segment values of the captured session, or null when the vector does not give them.</param>
internal sealed record OracleVector(string Module, string Label, string Name, IReadOnlyList<OracleCase> Cases, OracleSegments? Segments = null);

/// <summary>
/// Finds and reads the oracle vectors: JSON files under build/oracle/&lt;MODULE&gt;/&lt;label&gt;.json, written by
/// tools/oracle from runs of the original program. They are derived from the player's game and never committed; a
/// checkout without them simply has no vectors, and the oracle tests then report themselves as skipped.
/// </summary>
/// <remarks>
/// The format (ARCHITECTURE.md "Oracle test contract"; written by tools/oracle/make_vectors.py):
/// <code>
/// { "module": "CODE", "label": "sub_code_0000_18a4", "name": "_check_solution",
///   "call": { ... }, "segments": { ... },               how the generator called it; not needed here
///   "cases": [ { "id": "00-realistic", "origin": "...", "args": [int16...],
///                "dgroup_in": "base64", "shared_in": "base64" or null,
///                "inputs": [n or {"ax": n, ...}, ...],  one per call
///                "result": { "ax": n, "dx": n },
///                "dgroup_out": "base64", "shared_out": "base64" or null,
///                "stack_mask": [start, end],            the bytes of the stack: not compared
///                "ignore": [[start, end], ...],         optional further ranges, also at the top level
///                "calls": [ { "target": "_draw_string", "args": [n, n, n, {"ptr": n, "str": "base64"}], "ret": n } ],
///                "realistic": true,
///                "regs_in": {...}, "regs_out": {...},   assembly routines with register arguments
///                "far_in": {"004f0": "base64"}, "far_out": {...},   memory outside DGROUP and the shared block
///                "exit": code } ] }                     when the function ended the program
/// </code>
/// The scripted answers in <c>inputs</c> are consumed in call order by driver calls, interrupts (except a program
/// exit) and port reads; a port write consumes none (tools/oracle/README.md).
/// Numbers may be written signed or unsigned; they are taken modulo their width. Files whose name starts with an
/// underscore (_index.json, _boundaries.json) are the generator's own tables, not vectors.
/// </remarks>
internal static class OracleVectors
{
    /// <summary>The environment variable that names another vector folder than build/oracle.</summary>
    public const string EnvironmentVariable = "COVERT_ORACLE_DIR";

    /// <summary>The folder the vectors are read from.</summary>
    public static string Folder =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } folder
            ? folder
            : Path.Combine(Repository.Root, "build", "oracle");

    /// <summary>Every vector of <paramref name="module"/> for the function <paramref name="name"/>; empty when there is none.</summary>
    public static IReadOnlyList<OracleVector> ForFunction(string module, string name, string? folder = null)
    {
        var directory = Path.Combine(folder ?? Folder, module);
        if (!Directory.Exists(directory))
        {
            return [];
        }
        return Directory.EnumerateFiles(directory, "*.json")
            .Where(file => !Path.GetFileName(file).StartsWith('_') && NameOf(file) == name)
            .Order(StringComparer.Ordinal)
            .Select(Load)
            .ToList();
    }

    /// <summary>
    /// The function name a vector file is for, read from the start of the file only: the vectors of a module are
    /// hundreds of megabytes together, and a test wants the one or two files of its function.
    /// </summary>
    private static string? NameOf(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[512];
        var read = stream.Read(head, 0, head.Length);
        var text = System.Text.Encoding.UTF8.GetString(head, 0, read);
        // "name": "_check_solution", or "name": null for a function without a name, which then goes by its label
        var match = System.Text.RegularExpressions.Regex.Match(text, "\"name\"\\s*:\\s*\"_*([^\"]*)\"");
        if (!match.Success && text.Contains("\"name\": null", StringComparison.Ordinal))
        {
            match = System.Text.RegularExpressions.Regex.Match(text, "\"label\"\\s*:\\s*\"([^\"]*)\"");
        }
        // a file that does not start as a vector does: let Load say what is wrong with it
        return match.Success ? match.Groups[1].Value : Load(path).Name;
    }

    /// <summary>Reads one vector file.</summary>
    /// <exception cref="InvalidDataException">The file does not follow the contract.</exception>
    public static OracleVector Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var vectorIgnore = Ranges(root);
            var label = root.GetProperty("label").GetString()!;
            var name = root.TryGetProperty("name", out var given) && given.ValueKind == JsonValueKind.String ? given.GetString()! : label;
            var loads = LoadSegments(root);
            var cases = root.GetProperty("cases").EnumerateArray().Select(c => Case(c, vectorIgnore, loads)).ToList();
            return new OracleVector(root.GetProperty("module").GetString()!.ToUpperInvariant(), label, name.TrimStart('_'), cases, Segments(root));
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or IndexOutOfRangeException)
        {
            throw new InvalidDataException($"{path} is not an oracle vector: {e.Message}", e);
        }
    }

    /// <summary>The load segments of a vector: its "segments" is one record, or a list of them when its cases come from several routes.</summary>
    private static List<ushort> LoadSegments(JsonElement root)
    {
        if (!root.TryGetProperty("segments", out var segments))
        {
            return [];
        }
        var records = segments.ValueKind == JsonValueKind.Array ? segments.EnumerateArray().ToList() : [segments];
        return [.. records.Where(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("load", out _))
            .Select(r => Convert.ToUInt16(r.GetProperty("load").GetString()!, 16))];
    }

    private static OracleCase Case(JsonElement c, IReadOnlyList<(int, int)>? vectorIgnore, IReadOnlyList<ushort> loads)
    {
        var result = c.GetProperty("result");
        var stack = c.TryGetProperty("stack_mask", out var mask) && mask.ValueKind == JsonValueKind.Array
            ? new[] { (mask[0].GetInt32(), mask[1].GetInt32()) }
            : null;
        var listed = Ranges(c) ?? vectorIgnore;
        // the elements outlive the document: clone them
        var inputs = c.TryGetProperty("inputs", out var scripted) ? scripted.EnumerateArray().Select(i => i.Clone()).ToList() : [];
        var calls = c.TryGetProperty("calls", out var recorded) ? recorded.EnumerateArray().ToList() : [];
        return new OracleCase
        {
            Id = c.GetProperty("id").ToString(),
            Origin = c.TryGetProperty("origin", out var origin) ? origin.ToString() : "",
            Args = [.. c.GetProperty("args").EnumerateArray().Select(a => unchecked((short)a.GetInt64()))],
            DgroupIn = c.GetProperty("dgroup_in").GetBytesFromBase64(),
            DgroupOut = c.GetProperty("dgroup_out").GetBytesFromBase64(),
            SharedIn = OptionalBytes(c, "shared_in"),
            SharedOut = OptionalBytes(c, "shared_out"),
            Ax = unchecked((short)result.GetProperty("ax").GetInt64()),
            Dx = result.TryGetProperty("dx", out var dx) ? unchecked((short)dx.GetInt64()) : (short)0,
            Calls = PairWithInputs(calls, inputs),
            Ignore = [.. stack ?? [], .. listed ?? []],
            IgnoreDeclared = stack is not null || listed is not null,
            RegsIn = Registers(c, "regs_in"),
            RegsOut = Registers(c, "regs_out"),
            FarIn = Far(c, "far_in"),
            FarOut = Far(c, "far_out"),
            LoadSegments = loads,
            Exit = c.TryGetProperty("exit", out var exit) && exit.ValueKind == JsonValueKind.Number ? exit.GetInt32() : null,
            // older vectors have no flag: there the id says where a case came from
            Realistic = c.TryGetProperty("realistic", out var realistic) && realistic.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? realistic.GetBoolean()
                : c.GetProperty("id").ToString().EndsWith("realistic", StringComparison.Ordinal),
        };
    }

    /// <summary>Gives each call the scripted answer it consumed: port writes and the program exit consume none.</summary>
    private static List<BoundaryCall> PairWithInputs(List<JsonElement> calls, List<JsonElement> inputs)
    {
        var next = 0;
        var paired = new List<BoundaryCall>();
        foreach (var call in calls)
        {
            var target = call.GetProperty("target").GetString()!;
            var consumes = target is not ("outb" or "outw" or "int21.4c" or "int21.00" or "int21.31") && !target.StartsWith("int20.", StringComparison.Ordinal);
            paired.Add(Call(call, consumes && next < inputs.Count ? inputs[next++] : null));
        }
        return paired;
    }

    private static OracleSegments? Segments(JsonElement root)
    {
        if (!root.TryGetProperty("segments", out var s) || s.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        ushort Hex(string property) => Convert.ToUInt16(s.GetProperty(property).GetString()!, 16);
        return new OracleSegments(Hex("psp"), Hex("load"), Hex("dgroup"), s.TryGetProperty("shared", out var shared) ? shared.ToString() : "");
    }

    private static Dictionary<string, ushort> Registers(JsonElement c, string property) =>
        c.TryGetProperty(property, out var regs) && regs.ValueKind == JsonValueKind.Object
            ? regs.EnumerateObject().Where(r => r.Value.ValueKind == JsonValueKind.Number).ToDictionary(r => r.Name, r => unchecked((ushort)r.Value.GetInt64()))
            : [];

    private static Dictionary<int, byte[]> Far(JsonElement c, string property) =>
        c.TryGetProperty(property, out var far) && far.ValueKind == JsonValueKind.Object
            ? far.EnumerateObject().ToDictionary(run => Convert.ToInt32(run.Name, 16), run => run.Value.GetBytesFromBase64())
            : [];

    private static BoundaryCall Call(JsonElement call, JsonElement? input) => new(
        call.GetProperty("target").GetString()!.TrimStart('_'),
        call.TryGetProperty("args", out var args) ? [.. args.EnumerateArray().Select(a => a.Clone())] : [],
        call.TryGetProperty("ret", out var ret) && ret.ValueKind == JsonValueKind.Number ? ret.GetInt64() : null,
        input,
        OptionalBytes(call, "out"),
        call.TryGetProperty("osfile", out var flags) && flags.ValueKind == JsonValueKind.Number ? flags.GetInt32() : null);

    private static byte[]? OptionalBytes(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetBytesFromBase64() : null;

    private static IReadOnlyList<(int, int)>? Ranges(JsonElement element) =>
        element.TryGetProperty("ignore", out var ranges) && ranges.ValueKind == JsonValueKind.Array
            ? [.. ranges.EnumerateArray().Select(r => (r[0].GetInt32(), r[1].GetInt32()))]
            : null;
}
