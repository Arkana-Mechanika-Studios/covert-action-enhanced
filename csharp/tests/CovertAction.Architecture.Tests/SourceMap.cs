using System.Text.RegularExpressions;
using CovertAction.Tests.Shared;

namespace CovertAction.Architecture.Tests;

/// <summary>One original function of docs/source-map/original-functions.csv.</summary>
/// <param name="Coordinate">MODULE frame:offset.</param>
/// <param name="Name">Its name in symbols/names.txt without the leading underscore.</param>
/// <param name="Source">The src/c file or library member, or empty.</param>
/// <param name="Status">matched, lib, thunk, asm or data.</param>
/// <param name="CSharp">The C# member that reconstructs it, or "not ported".</param>
/// <param name="Note">For a function that will not be ported as it stands, the reason; else empty.</param>
internal sealed record OriginalFunction(string Coordinate, string Name, string Source, string Status, string CSharp, string Note)
{
    /// <summary>The module of the coordinate ("CODE").</summary>
    public string Module => Coordinate[..Coordinate.IndexOf(' ')];
}

/// <summary>One <c>// orig:</c> comment of the product sources and the declaration under it.</summary>
/// <param name="File">The source file.</param>
/// <param name="Line">Its line number, 1-based.</param>
/// <param name="Text">The comment after "// orig: ".</param>
/// <param name="Coordinates">The function coordinates it cites, in order (empty for a data citation).</param>
/// <param name="Member">Namespace.Type or Namespace.Type.Member of the declaration under the comment.</param>
internal sealed record OrigComment(string File, int Line, string Text, IReadOnlyList<string> Coordinates, string Member)
{
    public string Where => $"{Path.GetRelativePath(Repository.CSharp, File)}:{Line}";
}

/// <summary>
/// The source map (docs/source-map/original-functions.csv) and the traceability comments of the product sources,
/// read the way csharp/tools/source_map.py reads them.
/// </summary>
internal static partial class SourceMap
{
    public const string NotPorted = "not ported";

    public static string CsvPath => Path.Combine(Repository.CSharp, "docs", "source-map", "original-functions.csv");

    [GeneratedRegex(@"\b(COVERT|INTRO|FINAL|GAME|TAC|BUG|CHASE|CODE|MGRAPHIC|MISC|ISOUND|ASOUND|RSOUND|TSOUND) ([0-9a-f]{4}:[0-9a-f]{4})\b")]
    private static partial Regex Coordinate();

    [GeneratedRegex(@"^\s*// orig: (.*)$")]
    private static partial Regex Orig();

    [GeneratedRegex(@"\b(?:class|struct|interface|record)\s+([A-Za-z_]\w*)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^namespace\s+([\w.]+)\s*;")]
    private static partial Regex Namespace();

    [GeneratedRegex(@"([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(")]
    private static partial Regex MemberDeclaration();

    /// <summary>The rows of the csv by coordinate.</summary>
    public static Dictionary<string, OriginalFunction> Functions { get; } = File.ReadLines(CsvPath)
        .Skip(1)
        .Select(SplitCsv)
        .ToDictionary(f => f[0], f => new OriginalFunction(f[0], f[1], f[2], f[3], f[4], f[5]));

    /// <summary>Every <c>// orig:</c> comment of csharp/src.</summary>
    public static List<OrigComment> Comments { get; } = [.. Repository.OwnSources().Order(StringComparer.Ordinal).SelectMany(ReadComments)];

    private static IEnumerable<OrigComment> ReadComments(string file)
    {
        var lines = File.ReadAllLines(file);
        string ns = "", type = "";
        for (var i = 0; i < lines.Length; i++)
        {
            if (Namespace().Match(lines[i]) is { Success: true } n)
            {
                ns = n.Groups[1].Value;
            }
            var isComment = lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal);
            if (!isComment && TypeDeclaration().Match(lines[i]) is { Success: true } t)
            {
                type = t.Groups[1].Value;
            }
            if (Orig().Match(lines[i]) is not { Success: true } orig)
            {
                continue;
            }
            // the declaration: the first line under the comment that is neither a comment nor an attribute
            var declaration = lines.Skip(i + 1).FirstOrDefault(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith('[')) ?? "";
            string member;
            if (TypeDeclaration().Match(declaration) is { Success: true } declared)
            {
                member = $"{ns}.{declared.Groups[1].Value}";
            }
            else if (MemberDeclaration().Match(declaration) is { Success: true } method)
            {
                member = $"{ns}.{type}.{method.Groups[1].Value}";
            }
            else
            {
                member = $"{ns}.{type}";
            }
            var coordinates = Coordinate().Matches(orig.Groups[1].Value).Select(m => m.Value).ToList();
            yield return new OrigComment(file, i + 1, orig.Groups[1].Value, coordinates, member);
        }
    }

    /// <summary>One csv line into its fields (quotes as csv.writer of Python writes them).</summary>
    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }
        fields.Add(field.ToString());
        return fields;
    }
}
