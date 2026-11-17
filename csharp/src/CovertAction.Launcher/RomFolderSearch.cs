using CovertAction.Content;
using CovertAction.Audio.Mt32;

namespace CovertAction.Launcher;

/// <summary>
/// Where the player's MT-32 (or CM-32L) ROM files are when no folder is named (new code, outside the game). A named folder
/// (the setting, --mt32-roms, COVERT_MT32_ROMS) always wins and is never searched around; this is only the default: the
/// MT32_ROMS folder beside the program when it holds a usable set, else the places players commonly keep ROMs in, each
/// counting only when it holds a usable set (an MT-32's or a CM-32L's control and PCM ROMs, as <see cref="Mt32Roms"/> finds
/// them). Only the named folders are looked at; no disk is scanned.
/// </summary>
public static class RomFolderSearch
{
    /// <summary>The folder names ROMs are kept in, beside the game and in the folders above it.</summary>
    private static readonly string[] RomFolderNames = ["MT32_ROMS", "mt32-roms", "roms"];

    /// <summary>How many folders above the game folder are looked in (a store's install folder holds the game one or two folders down).</summary>
    private const int FoldersAbove = 2;

    /// <summary>
    /// The folder looked in when none is chosen and none of the places holds a set: MT32_ROMS beside the program (a release
    /// carries it empty), where the player is told to put the ROMs.
    /// </summary>
    public static string DefaultFolder => HostFiles.FindDirectory(AppContext.BaseDirectory, "MT32_ROMS") ?? Path.Combine(AppContext.BaseDirectory, "MT32_ROMS");

    /// <summary>
    /// The folder to use when none is chosen: the first place of <see cref="Candidates"/> that holds a usable ROM set, with
    /// what it is ("beside the program", "next to the game", "DOSBox Staging's"); else the <see cref="DefaultFolder"/> with no
    /// source (its status line then says there are no ROMs).
    /// </summary>
    /// <param name="gameFolder">The game folder in use or found (the places around it are looked in); null: none.</param>
    public static (string Folder, string? Source) Automatic(string? gameFolder) =>
        Find(gameFolder, AppContext.BaseDirectory, Environment.GetEnvironmentVariable, HoldsRoms) is { } found ? found : (DefaultFolder, null);

    /// <summary>The first of <see cref="Candidates"/> for which <paramref name="holdsRoms"/> says yes; null when none.</summary>
    /// <param name="gameFolder">The game folder, or null.</param>
    /// <param name="programFolder">The program's folder.</param>
    /// <param name="environment">An environment variable's value, or null (tests give their own).</param>
    /// <param name="holdsRoms">Whether a folder holds a usable ROM set.</param>
    public static (string Folder, string Source)? Find(string? gameFolder, string programFolder, Func<string, string?> environment, Func<string, bool> holdsRoms)
    {
        foreach (var (folder, source) in Candidates(gameFolder, programFolder, environment))
        {
            if (Safe(holdsRoms, folder))
            {
                return (folder, source);
            }
        }
        return null;
    }

    /// <summary>
    /// The places to look, in order: MT32_ROMS beside the program; MT32_ROMS, mt32-roms and roms in the game folder and the
    /// two folders above it (the store's install folder); DOSBox Staging's mt32-roms folders; DOSBox-X's; Munt's usual
    /// folders. Environment variables that are not set give no place.
    /// </summary>
    public static IEnumerable<(string Folder, string Source)> Candidates(string? gameFolder, string programFolder, Func<string, string?> environment)
    {
        yield return (HostFiles.FindDirectory(programFolder, "MT32_ROMS") ?? Path.Combine(programFolder, "MT32_ROMS"), "beside the program");
        if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            var folder = Path.TrimEndingDirectorySeparator(gameFolder);
            for (var level = 0; level <= FoldersAbove && folder is { Length: > 0 }; level++)
            {
                foreach (var name in RomFolderNames)
                {
                    yield return (HostFiles.FindDirectory(folder, name) ?? Path.Combine(folder, name), level == 0 ? "in the game's folder" : "next to the game");
                }
                folder = Path.GetDirectoryName(folder) ?? "";
            }
        }
        foreach (var folder in DosboxStaging(environment))
        {
            yield return (folder, "DOSBox Staging's");
        }
        foreach (var folder in DosboxX(environment))
        {
            yield return (folder, "DOSBox-X's");
        }
        foreach (var folder in Munt(environment))
        {
            yield return (folder, "Munt's");
        }
    }

    /// <summary>
    /// DOSBox Staging's default MT-32 ROM folder (its manual, dosbox-staging.org/0.83/manual/sound/sound-devices/roland-mt-32):
    /// mt32-roms in its configuration folder, %LOCALAPPDATA%\DOSBox on Windows, ~/Library/Preferences/DOSBox on macOS,
    /// ~/.config/dosbox on Linux. Older versions used %APPDATA%\dosbox-staging, which is looked in too.
    /// </summary>
    private static IEnumerable<string> DosboxStaging(Func<string, string?> environment)
    {
        foreach (var root in Roots(environment, "LOCALAPPDATA", "DOSBox"))
        {
            yield return Path.Combine(root, "mt32-roms");
        }
        foreach (var root in Roots(environment, "APPDATA", "dosbox-staging"))
        {
            yield return Path.Combine(root, "mt32-roms");
        }
        foreach (var root in Roots(environment, "LOCALAPPDATA", "dosbox-staging"))
        {
            yield return Path.Combine(root, "mt32-roms");
        }
        if (environment("HOME") is { Length: > 0 } home)
        {
            yield return Path.Combine(home, "Library", "Preferences", "DOSBox", "mt32-roms");
            yield return Path.Combine(environment("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(home, ".config"), "dosbox", "mt32-roms");
        }
    }

    /// <summary>
    /// DOSBox-X's: it has no default folder of its own (it looks in the folder it is started from unless mt32.romdir names
    /// one), so only the folders players name after it are tried. UNCONFIRMED: no documentation says DOSBox-X keeps ROMs there.
    /// </summary>
    private static IEnumerable<string> DosboxX(Func<string, string?> environment)
    {
        foreach (var variable in new[] { "LOCALAPPDATA", "APPDATA" })
        {
            foreach (var root in Roots(environment, variable, "DOSBox-X"))
            {
                yield return Path.Combine(root, "mt32-roms");
                yield return Path.Combine(root, "mt32");
            }
        }
    }

    /// <summary>
    /// Munt's usual folders: it has no required one (its Qt program asks where the ROMs are), so a Munt folder under the
    /// per-user and program folders, and its "roms" and "mt32-roms" subfolders. UNCONFIRMED: only conventions.
    /// </summary>
    private static IEnumerable<string> Munt(Func<string, string?> environment)
    {
        foreach (var variable in new[] { "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)" })
        {
            foreach (var root in Roots(environment, variable, "Munt"))
            {
                yield return root;
                yield return Path.Combine(root, "roms");
                yield return Path.Combine(root, "mt32-roms");
            }
        }
    }

    /// <summary>The folder <paramref name="name"/> under the folder an environment variable names; none when it is not set.</summary>
    private static IEnumerable<string> Roots(Func<string, string?> environment, string variable, string name)
    {
        if (environment(variable) is { Length: > 0 } root)
        {
            yield return Path.Combine(root, name);
        }
    }

    /// <summary>Whether <paramref name="folder"/> holds a usable MT-32 or CM-32L set.</summary>
    private static bool HoldsRoms(string folder)
    {
        var (mt32, cm32l) = SoundCheck.Roms(folder);
        return mt32 || cm32l;
    }

    /// <summary>A folder that cannot be read holds no set.</summary>
    private static bool Safe(Func<string, bool> holdsRoms, string folder)
    {
        try
        {
            return holdsRoms(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
