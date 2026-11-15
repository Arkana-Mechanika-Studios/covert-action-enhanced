namespace CovertAction.Tests.Shared;

/// <summary>Where the repository's folders are, found from the test's own location (the folder that holds CovertAction.sln).</summary>
internal static class Repository
{
    /// <summary>The csharp folder: the root of the C# rewrite.</summary>
    public static string CSharp { get; } = FindCSharp();

    /// <summary>The project root above it (symbols, docs, build; local/src/c where the author keeps the reconstruction).</summary>
    public static string Root { get; } = Path.GetDirectoryName(CSharp)!;

    /// <summary>The product source files (csharp/src, without build output).</summary>
    public static IEnumerable<string> ProductSources() =>
        Directory.EnumerateFiles(Path.Combine(CSharp, "src"), "*.cs", SearchOption.AllDirectories).Where(NotBuildOutput);

    /// <summary>Every file of the rewrite that could be committed (csharp without build output).</summary>
    /// <summary>
    /// The source files written for this rewrite: the product sources without the files taken from the Darklands
    /// Restoration Project, whose comments follow that project's rules and cite its original, not this one's.
    /// </summary>
    public static IEnumerable<string> OwnSources() => ProductSources().Where(file => !IsFromDarklands(file));

    /// <summary>Whether a source file's first line says it was taken (as it is, or adapted) from the Darklands Restoration Project.</summary>
    public static bool IsFromDarklands(string file)
    {
        var first = File.ReadLines(file).FirstOrDefault() ?? "";
        return first.StartsWith("// Reused from the Darklands Restoration Project", StringComparison.Ordinal)
            || first.StartsWith("// Adapted from the Darklands Restoration Project", StringComparison.Ordinal);
    }

    public static IEnumerable<string> TrackedFiles() =>
        Directory.EnumerateFiles(CSharp, "*", SearchOption.AllDirectories).Where(NotBuildOutput);

    private static bool NotBuildOutput(string path)
    {
        var parts = Path.GetRelativePath(CSharp, path).Split(Path.DirectorySeparatorChar);
        return !parts.Contains("bin") && !parts.Contains("obj") && !parts.Contains(".vs") && !parts.Contains("TestResults");
    }

    private static string FindCSharp()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CovertAction.sln")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("CovertAction.sln not found above " + AppContext.BaseDirectory);
    }
}
