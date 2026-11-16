using CovertAction.Audio.Amiga;

namespace CovertAction.Launcher;

/// <summary>
/// Where the player's own Amiga files are, for the Amiga music option (D-640) and the Amiga sound effects option (D-641; new
/// code, outside the game): the music files (thememusic.dat, finalmusic.dat, spy6music.dat, from the Amiga version's disk 1)
/// and the five sound banks (gamesnd.dat, tacsnd.dat, codesnd.dat and bugsnd.dat from disk 2, chasesnd.dat from disk 3). A
/// folder the player names always wins and is looked at as it is (<see cref="Resolve"/>, <see cref="ResolveBanks"/>); the
/// automatic search only runs when none is named: an <c>amiga/files</c> (or <c>amiga</c>) folder beside the program, or beside
/// the game folder and the two folders above it, counting when it holds the first file itself, in its <c>disk1</c> (the
/// banks: <c>disk2</c>) folder or in a folder directly below it. No disk is scanned.
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
    public static (string Folder, string Source)? Find(string? gameFolder, string programFolder) => Find(gameFolder, programFolder, Resolve);

    /// <summary>The folder with the sound banks to use when none is chosen: the first place of <see cref="Candidates"/> that holds them, and where it was found; else null.</summary>
    /// <param name="gameFolder">The game folder in use or found; null: none.</param>
    public static (string Folder, string Source)? AutomaticBanks(string? gameFolder) => FindBanks(gameFolder, AppContext.BaseDirectory);

    /// <summary>The first of <see cref="Candidates"/> that holds the sound banks (see <see cref="ResolveBanks"/>); null when none does.</summary>
    /// <param name="gameFolder">The game folder, or null.</param>
    /// <param name="programFolder">The program's folder.</param>
    public static (string Folder, string Source)? FindBanks(string? gameFolder, string programFolder) => Find(gameFolder, programFolder, ResolveBanks);

    private static (string Folder, string Source)? Find(string? gameFolder, string programFolder, Func<string, string?> resolve)
    {
        foreach (var (folder, source) in Candidates(gameFolder, programFolder))
        {
            if (resolve(folder) is { } holding)
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
    public static string? Resolve(string folder) => Holding(folder, AmigaTunes.ThemeFile, "disk1");

    /// <summary>
    /// The folder that holds gamesnd.dat for a folder the player named or a place searched: the folder itself, its
    /// <c>disk2</c> folder or a folder directly below it, or, for a folder named like a disk (<c>disk1</c>), the same below
    /// its parent (<c>disk2</c> beside it); null when none holds it or the folder cannot be read. The other banks are looked
    /// for around that folder (<see cref="BankFile"/>): chasesnd.dat is on disk 3.
    /// </summary>
    /// <param name="folder">The folder.</param>
    public static string? ResolveBanks(string folder) => ResolveDisk(folder, GameBank, "disk2");

    /// <summary>
    /// The folder that holds faces.pix for a folder the player named or a place searched: as <see cref="ResolveBanks"/> for
    /// the pictures' first file, with the <c>disk1</c> folder preferred. The other pictures are looked for around that folder
    /// (<see cref="BankFile"/>): the cities and the maps are on disk 2.
    /// </summary>
    /// <param name="folder">The folder.</param>
    public static string? ResolvePictures(string folder) => ResolveDisk(folder, "faces.pix", "disk1");

    /// <summary>The folder with the pictures to use when none is chosen: the first place of <see cref="Candidates"/> that holds them, and where it was found; else null.</summary>
    /// <param name="gameFolder">The game folder in use or found; null: none.</param>
    public static (string Folder, string Source)? AutomaticPictures(string? gameFolder) => Find(gameFolder, AppContext.BaseDirectory, ResolvePictures);

    /// <summary>The names of the pictures' files (of <see cref="AmigaPictureList"/>) that are not around a folder (none when it has all).</summary>
    /// <param name="pictureFolder">A folder as <see cref="ResolvePictures"/> gives it; null: none was found, so all are missing.</param>
    public static IReadOnlyList<string> MissingPictures(string? pictureFolder) =>
        [.. AmigaPictureList.Entries.Select(entry => entry.File).Where(name => pictureFolder is null || BankFile(pictureFolder, name) is null)];

    private static string? ResolveDisk(string folder, string marker, string preferred)
    {
        if (Holding(folder, marker, preferred) is { } holding)
        {
            return holding;
        }
        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(folder);
            return Path.GetFileName(trimmed).StartsWith("disk", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(trimmed) is { Length: > 0 } parent
                ? Holding(parent, marker, preferred)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The first sound bank's file name: the folder that holds it is the folder of the banks.</summary>
    private static string GameBank => AmigaSoundBanks.FileNames[0];

    private static string? Holding(string folder, string marker, string preferred)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }
            if (File.Exists(Path.Combine(folder, marker)))
            {
                return folder;
            }
            var disk = Path.Combine(folder, preferred);
            if (File.Exists(Path.Combine(disk, marker)))
            {
                return disk;
            }
            return Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(below => File.Exists(Path.Combine(below, marker)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The path of a sound bank's file around the folder of the banks: in the folder itself, in a folder directly below it, in
    /// its parent or in a folder directly below that (disk 2's folder has disk 3 beside it); null when it is not there.
    /// </summary>
    /// <param name="bankFolder">A folder as <see cref="ResolveBanks"/> gives it.</param>
    /// <param name="name">The file's name (one of <see cref="AmigaSoundBanks.FileNames"/>).</param>
    public static string? BankFile(string bankFolder, string name)
    {
        try
        {
            var places = new List<string> { bankFolder };
            places.AddRange(Directory.EnumerateDirectories(bankFolder).Order(StringComparer.OrdinalIgnoreCase));
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(bankFolder)) is { Length: > 0 } parent)
            {
                places.Add(parent);
                places.AddRange(Directory.EnumerateDirectories(parent).Order(StringComparer.OrdinalIgnoreCase));
            }
            return places.Select(place => Path.Combine(place, name)).FirstOrDefault(File.Exists);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The names of the sound banks' files that are not around a folder (none when it has all five).</summary>
    /// <param name="bankFolder">A folder as <see cref="ResolveBanks"/> gives it; null: none was found, so all five are missing.</param>
    public static IReadOnlyList<string> MissingBanks(string? bankFolder) =>
        [.. AmigaSoundBanks.FileNames.Where(name => bankFolder is null || BankFile(bankFolder, name) is null)];

    /// <summary>The names of the three music files a folder lacks (none when it holds all).</summary>
    /// <param name="folder">A folder as <see cref="Resolve"/> gives it.</param>
    public static IReadOnlyList<string> Missing(string folder) =>
        [.. new[] { AmigaTunes.ThemeFile, AmigaTunes.FinalFile, AmigaTunes.SpyFile }.Where(name => !File.Exists(Path.Combine(folder, name)))];
}
