using CovertAction.Tests.Shared;

namespace CovertAction.Architecture.Tests;

/// <summary>
/// What may be in the rewrite's folder at all: source, documents and tools, never the game. The player supplies the
/// game; the oracle vectors derived from it live under build/ outside this folder.
/// </summary>
public sealed class RepositoryTests
{
    /// <summary>File types of the original game and of things derived from it.</summary>
    private static readonly string[] GameExtensions = [".exe", ".com", ".pic", ".pan", ".dta", ".sve", ".ca", ".lib", ".obj", ".bin", ".img", ".zip"];

    [Fact]
    public void NoGameFileIsInTheRewritesFolder()
    {
        var breaks = Repository.TrackedFiles()
            .Where(file => GameExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .Select(file => Path.GetRelativePath(Repository.CSharp, file))
            .ToList();
        Assert.True(breaks.Count == 0, "game files (or files derived from them) under csharp/:\n" + string.Join("\n", breaks));
    }

    [Fact]
    public void NoOracleVectorIsInTheRewritesFolder()
    {
        // a vector carries whole data segments of the original program
        var breaks = Repository.TrackedFiles()
            .Where(file => Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("\"dgroup_in\"", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Repository.CSharp, file))
            .ToList();
        Assert.True(breaks.Count == 0, "oracle vectors under csharp/ (they belong in build/oracle):\n" + string.Join("\n", breaks));
    }

    /// <summary>The one binary file of the rewrite: SDL 3 for Windows, unmodified, as THIRD_PARTY_NOTICES.md describes it.</summary>
    private const string SdlLibrary = "src/CovertAction.Host.Sdl/Native/win-x64/SDL3.dll";

    /// <summary>The text font taken from Darklands as it is (not a source file, so it is not found by its first line).</summary>
    private const string TextFontResource = "src/CovertAction.Host.Sdl/Resources/vga-8x16.b64";

    private const string SdlLibrarySha256 = "66844f10251cc1c10619ed5d8e4e3224f564900ee6d69ba87d63de782addae40";

    [Fact]
    public void NoFileIsLargerThanSourceAndDocumentsEverAre()
    {
        const long limit = 1024 * 1024;
        var breaks = Repository.TrackedFiles()
            .Where(file => new FileInfo(file).Length > limit)
            .Select(file => Path.GetRelativePath(Repository.CSharp, file).Replace('\\', '/'))
            .Where(file => file != SdlLibrary)
            .ToList();
        Assert.True(breaks.Count == 0, "files over 1 MiB under csharp/:\n" + string.Join("\n", breaks));
    }

    [Fact]
    public void TheSdlLibraryIsTheOfficialReleaseTheNoticesName()
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(Repository.CSharp, SdlLibrary))));
        Assert.Equal(SdlLibrarySha256, hash);
        var notices = Path.Combine(Repository.Root, "THIRD_PARTY_NOTICES.md");
        if (File.Exists(notices))
        {
            Assert.Contains(SdlLibrarySha256, File.ReadAllText(notices));
        }
    }

    /// <summary>
    /// A file taken from the Darklands Restoration Project says so in its first line and is listed in docs/REUSED.md,
    /// and the list names no file that is not there.
    /// </summary>
    [Fact]
    public void EveryReusedFileIsListed()
    {
        var listed = File.ReadLines(Path.Combine(Repository.CSharp, "docs", "REUSED.md"))
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('`')[1])
            .ToHashSet();
        var marked = Repository.ProductSources()
            .Where(Repository.IsFromDarklands)
            .Select(file => Path.GetRelativePath(Repository.CSharp, file).Replace('\\', '/'))
            .Append(SdlLibrary)
            .Append(TextFontResource)
            .ToHashSet();
        Assert.True(marked.SetEquals(listed), "docs/REUSED.md and the files' first lines disagree about: " + string.Join(", ", marked.Except(listed).Concat(listed.Except(marked))));
    }
}
