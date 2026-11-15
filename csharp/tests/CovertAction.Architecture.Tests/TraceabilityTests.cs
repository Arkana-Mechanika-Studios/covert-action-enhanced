using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CovertAction.Core;
using CovertAction.Tests.Shared;

namespace CovertAction.Architecture.Tests;

/// <summary>
/// The traceability rules of CONTRIBUTING.md, as far as a test can hold them: every <c>// orig:</c> comment names a
/// real place of the original with the right name and status, the source map agrees with the comments, every method
/// of the game is either a reconstruction listed there or registered as new code, and every marker
/// (<c>BUG</c>, <c>DIVERGENCE</c>, <c>UNCERTAIN</c>) has its entry in a register.
/// </summary>
public sealed partial class TraceabilityTests
{
    private static readonly string[] Statuses = ["matched", "lib", "thunk", "asm", "driver"];

    /// <summary>"MODULE ffff:oooo name [(source)] status" at the start of a function citation.</summary>
    [GeneratedRegex(@"^(?<coordinate>[A-Z]+ [0-9a-f]{4}:[0-9a-f]{4}) (?<name>\S+) (?:\((?<source>[^)]*)\) )?(?<status>[a-z]+)\b")]
    private static partial Regex FunctionCitation();

    [GeneratedRegex(@"; tests: (?<tests>[\w, ]+)")]
    private static partial Regex TestsPart();

    [GeneratedRegex(@"^\s*// (?<kind>BUG|DIVERGENCE|UNCERTAIN): (?<text>.*)$")]
    private static partial Regex Marker();

    [GeneratedRegex(@"\b(?<id>(?:CA|D|U)-\d+)\b")]
    private static partial Regex RegisterId();

    private static string Fail(IEnumerable<string> breaks) => string.Join("\n", breaks);

    // ---- // orig: comments against the index of the original ----

    [Fact]
    public void EveryOrigCommentNamesAPlaceOfTheOriginal()
    {
        var breaks = new List<string>();
        foreach (var comment in SourceMap.Comments)
        {
            if (comment.Coordinates.Count == 0)
            {
                // a data citation: a variable of a data segment, a byte of the hand-off block, or a record type of the headers
                if (!Regex.IsMatch(comment.Text, @"DS:[0-9a-f]{4}|SHARED\+0x[0-9a-f]+|DGROUP|\b(struct|union) \w+|src/c/include/\w+\.h"))
                {
                    breaks.Add($"{comment.Where}: names no coordinate (MODULE ffff:oooo, MODULE DS:xxxx, SHARED+0xNN, struct name, a header of src/c/include)");
                }
                continue;
            }
            foreach (var coordinate in comment.Coordinates.Where(c => !SourceMap.Functions.ContainsKey(c)))
            {
                breaks.Add($"{comment.Where}: {coordinate} is not a function of the original (docs/source-map/original-functions.csv)");
            }
        }
        Assert.True(breaks.Count == 0, Fail(breaks));
    }

    [Fact]
    public void AFunctionCitationGivesTheOriginalsNameSourceAndStatus()
    {
        var breaks = new List<string>();
        foreach (var comment in SourceMap.Comments.Where(c => c.Coordinates.Count > 0))
        {
            var citation = FunctionCitation().Match(comment.Text);
            if (!citation.Success || !SourceMap.Functions.TryGetValue(citation.Groups["coordinate"].Value, out var function))
            {
                breaks.Add($"{comment.Where}: write '// orig: MODULE ffff:oooo name (source) status; also ...; tests: ...'");
                continue;
            }
            if (citation.Groups["name"].Value != function.Name)
            {
                breaks.Add($"{comment.Where}: {function.Coordinate} is {function.Name}, not {citation.Groups["name"].Value}");
            }
            var status = citation.Groups["status"].Value;
            if (!Statuses.Contains(status) || status != function.Status)
            {
                breaks.Add($"{comment.Where}: the status of {function.Coordinate} is {function.Status}, not {status}");
            }
            if (function.Status == "matched" && citation.Groups["source"].Value != function.Source)
            {
                breaks.Add($"{comment.Where}: the source of {function.Coordinate} is ({function.Source})");
            }
        }
        Assert.True(breaks.Count == 0, Fail(breaks));
    }

    [Fact]
    public void AReconstructedFunctionNamesTheTestsThatCoverIt()
    {
        var testClasses = Directory.EnumerateFiles(Path.Combine(Repository.CSharp, "tests"), "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension).ToHashSet();
        var breaks = new List<string>();
        foreach (var comment in SourceMap.Comments.Where(c => c.Coordinates.Count > 0))
        {
            var tests = TestsPart().Match(comment.Text);
            var status = SourceMap.Functions.GetValueOrDefault(comment.Coordinates[0])?.Status;
            if (!tests.Success)
            {
                // a function with C of its own is always tested; library routines and thunks are, where they have code here
                if (status == "matched")
                {
                    breaks.Add($"{comment.Where}: a reconstructed function names its tests ('; tests: SomeTests')");
                }
                continue;
            }
            foreach (var name in tests.Groups["tests"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!testClasses.Contains(name))
                {
                    breaks.Add($"{comment.Where}: there is no test class {name}");
                }
            }
        }
        Assert.True(breaks.Count == 0, Fail(breaks));
    }

    /// <summary>
    /// A reconstructed function keeps the original's name: MicroProse's own (it has capitals: DoCode, CheckForDisk) as
    /// it is, one of ours (lower case with underscores) in C#'s capitalisation (check_solution becomes CheckSolution).
    /// </summary>
    [Fact]
    public void AReconstructedFunctionKeepsTheOriginalsName()
    {
        var breaks = new List<string>();
        foreach (var comment in SourceMap.Comments.Where(c => c.Coordinates.Count > 0))
        {
            var function = SourceMap.Functions.GetValueOrDefault(comment.Coordinates[0]);
            if (function is not { Status: "matched" or "asm" })
            {
                continue;
            }
            var expected = function.Name.Any(char.IsUpper)
                ? function.Name
                : string.Concat(function.Name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            var actual = comment.Member[(comment.Member.LastIndexOf('.') + 1)..];
            if (actual != expected)
            {
                breaks.Add($"{comment.Where}: {function.Coordinate} {function.Name} is reconstructed as {actual}; name it {expected}");
            }
        }
        Assert.True(breaks.Count == 0, Fail(breaks));
    }

    // ---- the source map ----

    [Fact]
    public void TheSourceMapAgreesWithTheComments()
    {
        var cited = new Dictionary<string, string>();
        var breaks = new List<string>();
        foreach (var comment in SourceMap.Comments)
        {
            foreach (var coordinate in comment.Coordinates)
            {
                if (!cited.TryAdd(coordinate, comment.Member) && cited[coordinate] != comment.Member)
                {
                    breaks.Add($"{comment.Where}: {coordinate} is already reconstructed by {cited[coordinate]}");
                }
            }
        }
        foreach (var function in SourceMap.Functions.Values)
        {
            var member = cited.GetValueOrDefault(function.Coordinate, SourceMap.NotPorted);
            if (function.CSharp != member)
            {
                breaks.Add($"{function.Coordinate} {function.Name}: the map says '{function.CSharp}', the comments say '{member}'");
            }
        }
        Assert.True(breaks.Count == 0, "run python -B csharp/tools/source_map.py; then:\n" + Fail(breaks.Take(20)));
    }

    [SkippableFact]
    public void TheSourceMapListsEveryFileOfTheReadableC()
    {
        // The readable C of the original's functions is not published; its private copy is local/src/c (or src/c).
        var sources = new[] { Path.Combine(Repository.Root, "src", "c"), Path.Combine(Repository.Root, "local", "src", "c") }
            .FirstOrDefault(Directory.Exists);
        Skip.If(sources is null, "no readable C (src/c or local/src/c): the source map's files are not checked against it");
        var listed = SourceMap.Functions.Values.Select(f => f.Source).ToHashSet();
        var missing = Directory.EnumerateFiles(sources!, "*.c", SearchOption.AllDirectories)
            .Select(file => "src/c/" + Path.GetRelativePath(Path.Combine(sources!), file).Replace('\\', '/'))
            .Where(file => !listed.Contains(file))
            .ToList();
        Assert.True(missing.Count == 0, "run python -B csharp/tools/source_map.py; not in the map:\n" + Fail(missing.Take(20)));
    }

    /// <summary>
    /// Every method of the game (Core and Presentation) that other code can call is accounted for: it reconstructs an
    /// original function and is in the source map, or it is new code and docs/source-map/new-code.csv says why it exists.
    /// </summary>
    [Fact]
    public void EveryMethodOfTheGameIsAReconstructionOrRegisteredNewCode()
    {
        var reconstructed = SourceMap.Functions.Values.Select(f => f.CSharp).ToHashSet();
        // new-code.csv, and one new-code-<MODULE>.csv per module package
        var newCode = Directory.EnumerateFiles(Path.Combine(Repository.CSharp, "docs", "source-map"), "new-code*.csv")
            .SelectMany(file => File.ReadLines(file).Skip(1)).Where(l => l.Trim().Length > 0).Select(l => l.Split(',')[0]).ToList();

        // private methods are the helpers of the method that calls them and are covered by its comment
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var methods = new[] { "CovertAction.Core", "CovertAction.Presentation" }
            .SelectMany(name => Assembly.Load(name).GetTypes())
            .Where(type => !type.Name.Contains('<') && !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(type => type.GetMethods(declared))
            .Where(method => !method.IsPrivate && !method.IsSpecialName && !method.Name.Contains('<') && !method.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToHashSet();

        var unaccounted = methods.Where(m => !reconstructed.Contains(m) && !newCode.Contains(m)).Order().ToList();
        Assert.True(unaccounted.Count == 0,
            "neither under an '// orig:' comment (then run tools/source_map.py) nor in docs/source-map/new-code.csv:\n" + Fail(unaccounted));

        var stale = newCode.Where(m => !methods.Contains(m)).ToList();
        Assert.True(stale.Count == 0, "docs/source-map/new-code.csv lists methods that do not exist:\n" + Fail(stale));
    }

    /// <summary>The modules whose port is complete: each of their functions has a C# member or a stated reason why it has none.</summary>
    private static readonly string[] CompleteModules = ["CODE", "INTRO", "COVERT"];

    [Fact]
    public void EveryFunctionOfACompleteModuleIsPortedOrHasAReason()
    {
        var open = SourceMap.Functions.Values
            .Where(f => CompleteModules.Contains(f.Module) && f.CSharp == SourceMap.NotPorted && f.Note.Length == 0)
            .Select(f => $"{f.Coordinate} {f.Name} ({f.Status})")
            .ToList();
        Assert.True(open.Count == 0, "neither ported nor given a reason in docs/source-map/not-ported.csv: " + Fail(open));
    }

    // ---- markers and registers ----

    private static HashSet<string> RegisterIds(string path) =>
        [.. File.ReadLines(path).Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Select(line => RegisterId().Match(line.Split('|')[1])).Where(m => m.Success).Select(m => m.Value)];

    /// <summary>
    /// The ids of a register of docs/faithfulness: the file itself and its per-module parts (DIVERGENCES.md and
    /// DIVERGENCES-FINAL.md, ...).
    /// </summary>
    private static HashSet<string> FaithfulnessIds(string name) =>
        [.. Directory.EnumerateFiles(Path.Combine(Repository.CSharp, "docs", "faithfulness"), Path.GetFileNameWithoutExtension(name) + "*.md").SelectMany(RegisterIds)];

    /// <summary>The markers of the product sources: (kind, register id or "", where, text).</summary>
    private static List<(string Kind, string Id, string Where, string Text)> Markers() =>
        [.. Repository.OwnSources().SelectMany(file => File.ReadAllLines(file).Select((line, i) => (Match: Marker().Match(line), Line: i + 1))
            .Where(m => m.Match.Success)
            .Select(m => (m.Match.Groups["kind"].Value, RegisterId().Match(m.Match.Groups["text"].Value).Value,
                $"{Path.GetRelativePath(Repository.CSharp, file)}:{m.Line}", m.Match.Groups["text"].Value)))];

    [Theory]
    [InlineData("DIVERGENCE", "D-", "DIVERGENCES.md")]
    [InlineData("UNCERTAIN", "U-", "UNCERTAIN.md")]
    public void EveryMarkerHasItsRegisterEntryAndEveryEntryItsMarker(string kind, string prefix, string register)
    {
        var entries = FaithfulnessIds(register);
        var markers = Markers().Where(m => m.Kind == kind).ToList();
        var breaks = markers
            .Where(m => !m.Text.StartsWith(prefix, StringComparison.Ordinal) || !entries.Contains(m.Id))
            .Select(m => $"{m.Where}: '// {kind}: {prefix}n ...' must begin with an id listed in docs/faithfulness/{register}")
            .Concat(entries.Where(id => markers.All(m => m.Id != id)).Select(id => $"docs/faithfulness/{register}: {id} is marked nowhere in the code"))
            .ToList();
        Assert.True(breaks.Count == 0, Fail(breaks));
    }

    [Fact]
    public void EveryBugMarkerCitesTheBugRegisterAndARealSwitch()
    {
        var bugRegister = Path.Combine(Repository.Root, "docs", "original-bugs.md");
        if (!File.Exists(bugRegister))
        {
            return; // the C# folder on its own
        }
        var entries = RegisterIds(bugRegister);
        var switches = typeof(OriginalBehaviour).GetProperties().Select(p => p.Name).ToHashSet();
        var breaks = new List<string>();
        foreach (var marker in Markers().Where(m => m.Kind == "BUG"))
        {
            if (!entries.Contains(marker.Id))
            {
                breaks.Add($"{marker.Where}: '// BUG:' must cite an entry of docs/original-bugs.md (CA-n)");
            }
            foreach (Match cited in Regex.Matches(marker.Text, @"OriginalBehaviour\.(\w+)"))
            {
                if (!switches.Contains(cited.Groups[1].Value))
                {
                    breaks.Add($"{marker.Where}: OriginalBehaviour has no switch {cited.Groups[1].Value}");
                }
            }
        }

        // and each switch says which entry it is about
        var source = File.ReadAllText(Path.Combine(Repository.CSharp, "src", "CovertAction.Core", "OriginalBehaviour.cs"));
        foreach (Match property in Regex.Matches(source, @"/// <summary>(?<doc>(?:(?!</summary>).)*)</summary>\s*public bool (?<name>\w+)", RegexOptions.Singleline))
        {
            var id = RegisterId().Match(property.Groups["doc"].Value);
            if (!id.Success || !entries.Contains(id.Value))
            {
                breaks.Add($"OriginalBehaviour.{property.Groups["name"].Value}: its comment must cite its entry of docs/original-bugs.md");
            }
        }
        Assert.True(breaks.Count == 0, Fail(breaks));
    }
}
