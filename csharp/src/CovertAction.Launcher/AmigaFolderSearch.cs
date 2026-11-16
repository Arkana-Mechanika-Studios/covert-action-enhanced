using CovertAction.Audio.Amiga;

namespace CovertAction.Launcher;

/// <summary>
/// Where the player's own Amiga music files (thememusic.dat, finalmusic.dat, spy6music.dat, from the Amiga version's disk 1)
/// are, for the Amiga music option (D-640; new code, outside the game). A folder the player names always wins and is looked
/// at as it is (<see cref="Resolve"/>); the automatic search only runs when none is named: an <c>amiga/files</c> (or
/// <c>amiga</c>) folder beside the program, or beside the game folder and the two folders above it, counting when it holds
/// thememusic.dat itself, in its <c>disk1</c> folder or in a folder directly below it. No disk is scanned.
/// </summary>
public static class AmigaFolderSearch
{
    /// <summary>How many folders above the game folder are looked in (a store's install folder holds the game one or two folders down).</summary>
    private const int FoldersAbove = 2;

    /// <summary>The folders, below a place, where the files are kept, in the order tried.</summary>
    private static readonly string[][] Below = [["amiga", "files"], ["amiga"], ["Amiga", "files"], ["Amiga"]];

    /// <summary>The folder the Amiga files stay in when none is chosen: <c>amiga/files</c> beside the program, where the player is told to put them.</summary>
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "amiga", "files");

    /// <summary>The folder to use when none is chosen: the first place of <see cref="Candidates"/> that holds the music, and where it was found; else null.</summary>
    /// <param name="gameFolder">The game folder in use or found (the places around it are looked in); null: none.</param>
    public static (string Folder, string Source)? Automatic(string? gameFolder) => Find(gameFolder, AppContext.BaseDirectory);

    /// <summary>The first of <see cref="Candidates"/> that holds the music (see <see cref="Resolve"/>); null when none does.</summary>
    /// <param name="gameFolder">The game folder, or null.</param>
    /// <param name="programFolder">The program's folder.</param>
    public static (string Folder, string Source)? Find(string? gameFolder, string programFolder)
    {
        foreach (var (folder, source) in Candidates(gameFolder, programFolder))
        {
            if (Resolve(folder) is { } holding)
            {
                return (holding, source);
            }
        }
        return null;
    }

    /// <summary>The places to look, in order: beside the program, then in the game folder and the two folders above it.</summary>
    /// <param name="gameFolder">The game folder, or null.</param>
    /// <param name="programFolder">The program's folder.</param>
    public static IEnumerable<(string Folder, string Source)> Candidates(string? gameFolder, string programFolder)
    {
        foreach (var below in Below)
        {
            yield return (Path.Combine([programFolder, .. below]), "beside the program");
        }
        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            yield break;
        }
        var folder = Path.TrimEndingDirectorySeparator(gameFolder);
        for (var level = 0; level <= FoldersAbove && folder is { Length: > 0 }; level++)
        {
            foreach (var below in Below)
            {
                yield return (Path.Combine([folder, .. below]), level == 0 ? "in the game's folder" : "next to the game");
            }
            folder = Path.GetDirectoryName(folder) ?? "";
        }
    }

    /// <summary>
    /// The folder that holds thememusic.dat for a folder the player named or a place searched: the folder itself, its
    /// <c>disk1</c> folder (the Amiga disks extracted as the disk's names are), or a folder directly below it; null when
    /// none holds it or the folder cannot be read.
    /// </summary>
    /// <param name="folder">The folder.</param>
    public static string? Resolve(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }
            if (File.Exists(Path.Combine(folder, AmigaTunes.ThemeFile)))
            {
                return folder;
            }
            var disk1 = Path.Combine(folder, "disk1");
            if (File.Exists(Path.Combine(disk1, AmigaTunes.ThemeFile)))
            {
                return disk1;
            }
            return Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(below => File.Exists(Path.Combine(below, AmigaTunes.ThemeFile)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The names of the three music files a folder lacks (none when it holds all).</summary>
    /// <param name="folder">A folder as <see cref="Resolve"/> gives it.</param>
    public static IReadOnlyList<string> Missing(string folder) =>
        [.. new[] { AmigaTunes.ThemeFile, AmigaTunes.FinalFile, AmigaTunes.SpyFile }.Where(name => !File.Exists(Path.Combine(folder, name)))];
}
