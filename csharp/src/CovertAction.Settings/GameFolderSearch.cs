// Adapted from the Darklands Restoration Project, code/src/Darklands.Platform/GameFolderSearch.cs; same author.
// Adapted: Covert Action's product ids, folder names, files and subfolders; the registry and the environment are read
// through IMachineLookup (tests give a fake one); Steam's usual folders on Linux and macOS are looked at too.
using CovertAction.Content;
using System.Text.RegularExpressions;

namespace CovertAction.Settings;

/// <summary>
/// Where the player's copy of the game is: a folder counts when it holds the programs and files the rewrite reads
/// (<see cref="RequiredFiles"/>), or when one of its usual subfolders does. When nothing names the folder it is looked for
/// in the program's folder and the one above it, the current folder, GOG's registration, Steam's libraries, Windows' list
/// of installed programs, then the usual folders. No disk is scanned: only the named places and their immediate
/// subfolders are looked at, and the registry is read only when the places before it hold no game.
/// </summary>
internal static partial class GameFolderSearch
{
    /// <summary>
    /// GOG's product id of "Sid Meier's Covert Action (Classic)": the key GOG's installer registers the game under
    /// (HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\id, value "path"). Read from the GOG database entry
    /// https://www.gogdb.org/product/1207660463 (store page: gog.com/game/sid_meiers_covert_action); not verified on an
    /// installed copy, so the installed-programs list (matched by name) is a second way to GOG's folder.
    /// </summary>
    public const string GogId = "1207660463";

    /// <summary>
    /// Steam's app id of "Sid Meier's Covert Action (Classic)" (store.steampowered.com/app/327390; the same id in
    /// SteamDB and Lutris' installer). A DOSBox-based release.
    /// </summary>
    public const string SteamAppId = "327390";

    /// <summary>
    /// The folders under GOG's "Games" and Steam's "steamapps\common" the game installs to. GOG's is "Sid Meiers Covert Action"
    /// (the install directory of GOG's build, gogdb.org/product/1207660463/build/8501); Steam's name is UNCONFIRMED (no source
    /// found gives it), so the others are guesses, and Steam's common folder is also searched for any folder named like the game.
    /// </summary>
    private static readonly string[] InstallFolderNames =
        ["Sid Meiers Covert Action", "Sid Meier's Covert Action", "Sid Meier's Covert Action (Classic)", "Sid Meiers Covert Action Classic", "Covert Action"];

    /// <summary>What a program's name in Windows' list of installed programs, or a folder's name, contains for this game.</summary>
    private const string NamePart = "Covert Action";

    /// <summary>The files every start reads: the set-up's and the menus' programs, the graphics driver, the fonts, the campaign.</summary>
    public static readonly IReadOnlyList<string> RequiredFiles = ["INTRO.EXE", "FINAL.EXE", "GAME.EXE", "MGRAPHIC.EXE", "FONTS.CV"];

    /// <summary>The subfolders a copy of the game is often in (a CD or a digital store puts the game one or two folders down).</summary>
    private static readonly string[] Subfolders = ["Covert", "COVERT", Path.Combine("Covert Action", "Covert"), "Covert Action", "CA"];

    /// <summary>The folder that holds the game: <paramref name="folder"/> itself or one of its usual subfolders; null when none does.</summary>
    public static string? GameFolderIn(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }
        try
        {
            var full = Path.GetFullPath(folder.Trim().Trim('"'));
            if (HoldsGame(full))
            {
                return full;
            }
            if (Subfolders.Select(s => Path.Combine(full, s)).FirstOrDefault(HoldsGame) is { } usual)
            {
                return usual;
            }
            return SubfolderNamedLikeGame(full);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A store's install folder may spell the game folder as it likes (a case-sensitive file system, a release that adds a
    /// folder): a direct subfolder with "covert" in its name, or that folder's own "Covert" subfolder.
    /// </summary>
    private static string? SubfolderNamedLikeGame(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }
        foreach (var sub in Directory.EnumerateDirectories(folder).Where(d => Path.GetFileName(d).Contains("covert", StringComparison.OrdinalIgnoreCase)))
        {
            if (HoldsGame(sub))
            {
                return sub;
            }
            var inner = Directory.EnumerateDirectories(sub)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Covert", StringComparison.OrdinalIgnoreCase) && HoldsGame(d));
            if (inner is not null)
            {
                return inner;
            }
        }
        return null;
    }

    /// <summary>The required files that are not in <paramref name="folder"/> (all of them when it does not exist).</summary>
    public static IReadOnlyList<string> Missing(string folder) =>
        Directory.Exists(folder) ? [.. RequiredFiles.Where(f => !HostFiles.Exists(folder, f))] : RequiredFiles;

    private static bool HoldsGame(string folder) => Missing(folder).Count == 0;

    /// <summary>
    /// The places looked in when no folder is named, with what each is, in order and lazily (the registry is read only when
    /// the places before it have been passed over): the program's folder and the one above it, the current folder, GOG's
    /// registration, Steam's library, Windows' installed programs, the usual install folders of each fixed drive.
    /// </summary>
    public static IEnumerable<(string Folder, string Source)> Candidates(string programFolder, string currentFolder, IMachineLookup lookup)
    {
        yield return (programFolder, "the program's folder");
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(programFolder)) is { } above)
        {
            yield return (above, "the folder above the program");
        }
        yield return (currentFolder, "the current folder");
        foreach (var gog in GogFolders(lookup))
        {
            yield return (gog, "GOG's registration");
        }
        foreach (var steam in SteamFolders(lookup))
        {
            yield return (steam, "Steam's library");
        }
        if (OperatingSystem.IsLinux() && lookup.Environment("HOME") is { Length: > 0 } home)
        {
            foreach (var linux in LinuxCandidates(home, lookup.Environment))
            {
                yield return linux;
            }
        }
        foreach (var installed in InstalledProgramFolders(lookup))
        {
            yield return (installed, "Windows' installed programs");
        }
        foreach (var usual in UsualFolders(lookup))
        {
            yield return (usual, "a usual install folder");
        }
    }

    /// <summary>The game folders of <see cref="Candidates"/>, each once, with where each was found (all of them: the launcher offers each).</summary>
    public static IReadOnlyList<(string Folder, string Source)> Found(string programFolder, string currentFolder, IMachineLookup? lookup = null) =>
        [.. Holding(Candidates(programFolder, currentFolder, lookup ?? SystemLookup.Instance))
            .DistinctBy(c => Path.TrimEndingDirectorySeparator(c.Folder), StringComparer.OrdinalIgnoreCase)];

    /// <summary>The first game folder of <see cref="Candidates"/>; the places after it are never read (neither is the registry when the program's folder holds the game).</summary>
    public static (string Folder, string Source)? FindFirst(string programFolder, string currentFolder, IMachineLookup? lookup = null)
    {
        foreach (var found in Holding(Candidates(programFolder, currentFolder, lookup ?? SystemLookup.Instance)))
        {
            return found;
        }
        return null;
    }

    private static IEnumerable<(string Folder, string Source)> Holding(IEnumerable<(string Folder, string Source)> candidates)
    {
        foreach (var (folder, source) in candidates)
        {
            if (GameFolderIn(folder) is { } game)
            {
                yield return (game, source);
            }
        }
    }

    /// <summary>GOG's registration of the game: the "path" value of its product key (64-bit and 32-bit views).</summary>
    private static IEnumerable<string> GogFolders(IMachineLookup lookup)
    {
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games\" + GogId, @"SOFTWARE\GOG.com\Games\" + GogId })
        {
            if (lookup.RegistryValue(RegistryRoot.LocalMachine, key, "path") is { Length: > 0 } path)
            {
                yield return path;
            }
        }
    }

    /// <summary>The game's folder in each of Steam's libraries (Steam's own folder and those of libraryfolders.vdf).</summary>
    private static IEnumerable<string> SteamFolders(IMachineLookup lookup)
    {
        foreach (var library in SteamLibraryFolders(lookup))
        {
            var common = Path.Combine(library, "steamapps", "common");
            foreach (var name in InstallFolderNames)
            {
                yield return Path.Combine(common, name);
            }
            // the Steam folder name is unconfirmed: any folder of the library named like the game
            foreach (var named in FoldersNamedLikeGame(common))
            {
                yield return named;
            }
        }
    }

    /// <summary>Steam's own folder and the libraries it lists, each once.</summary>
    private static IEnumerable<string> SteamLibraryFolders(IMachineLookup lookup)
    {
        var steam = lookup.RegistryValue(RegistryRoot.CurrentUser, @"Software\Valve\Steam", "SteamPath")
            ?? lookup.RegistryValue(RegistryRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        var roots = new List<string>();
        if (steam is { Length: > 0 })
        {
            roots.Add(steam);
        }
        else if (lookup.Environment("HOME") is { Length: > 0 } home)
        {
            // Steam on Linux and macOS (no registry): its usual folders
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
        }
        var libraries = new List<string>();
        foreach (var root in roots)
        {
            libraries.Add(root);
            if (lookup.ReadText(Path.Combine(root, "steamapps", "libraryfolders.vdf")) is { } vdf)
            {
                libraries.AddRange(SteamLibraries(vdf));
            }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The library folders listed in Steam's libraryfolders.vdf ("path" entries, backslashes doubled).</summary>
    public static IEnumerable<string> SteamLibraries(string vdf) =>
        SteamPath().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex SteamPath();

    /// <summary>The folders directly in <paramref name="parent"/> whose names have the game's name in them.</summary>
    private static IEnumerable<string> FoldersNamedLikeGame(string parent)
    {
        try
        {
            if (!Directory.Exists(parent))
            {
                return [];
            }
            return [.. Directory.EnumerateDirectories(parent).Where(d => Path.GetFileName(d).Contains(NamePart, StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Windows' list of installed programs: the install folder of each one whose name has "Covert Action" in it.</summary>
    private static IEnumerable<string> InstalledProgramFolders(IMachineLookup lookup)
    {
        var places = new (RegistryRoot Root, string Key)[]
        {
            (RegistryRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (root, key) in places)
        {
            foreach (var name in lookup.RegistrySubKeys(root, key))
            {
                var program = key + "\\" + name;
                if (lookup.RegistryValue(root, program, "DisplayName") is not { } display || !display.Contains(NamePart, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (lookup.RegistryValue(root, program, "InstallLocation") is { Length: > 0 } location)
                {
                    yield return location.Trim('"');
                }
                else if (lookup.RegistryValue(root, program, "UninstallString") is { Length: > 0 } uninstall && UninstallerFolder(uninstall) is { } folder)
                {
                    // GOG's uninstaller (unins000.exe) is in the install folder
                    yield return folder;
                }
            }
        }
    }

    /// <summary>The folder of the program an uninstall command starts (quoted or not, with or without arguments); null when unclear.</summary>
    internal static string? UninstallerFolder(string command)
    {
        var text = command.Trim();
        string? path;
        if (text.StartsWith('"'))
        {
            path = text.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        else
        {
            var end = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = end >= 0 ? text[..(end + 4)] : null;
        }
        return path is null ? null : Path.GetDirectoryName(path);
    }

    /// <summary>The usual places: GOG's, GOG Galaxy's and Steam's default folders, and the folders players unpack into, on each fixed drive.</summary>
    private static IEnumerable<string> UsualFolders(IMachineLookup lookup)
    {
        foreach (var variable in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            if (lookup.Environment(variable) is { Length: > 0 } programs)
            {
                foreach (var name in InstallFolderNames)
                {
                    yield return Path.Combine(programs, "GOG Galaxy", "Games", name);
                    yield return Path.Combine(programs, "Steam", "steamapps", "common", name);
                }
            }
        }
        foreach (var drive in lookup.FixedDrives())
        {
            foreach (var name in InstallFolderNames)
            {
                yield return Path.Combine(drive, "GOG Games", name);
                yield return Path.Combine(drive, "Games", name);
                yield return Path.Combine(drive, "SteamLibrary", "steamapps", "common", name);
                yield return Path.Combine(drive, "Program Files (x86)", "GOG Galaxy", "Games", name);
                yield return Path.Combine(drive, "Program Files (x86)", "Steam", "steamapps", "common", name);
            }
            yield return Path.Combine(drive, "COVERT");
            yield return Path.Combine(drive, "Games", "COVERT");
        }
    }
}
