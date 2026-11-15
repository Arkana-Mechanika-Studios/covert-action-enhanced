using CovertAction.Audio.Mt32;
using CovertAction.Content;
using CovertAction.Platform;

namespace CovertAction.Launcher;

/// <summary>
/// For the launcher's sound row (new code, outside the game): what a ROM folder holds, and the theme tune played on a
/// sound card through the player's own driver, as the game would play it.
/// </summary>
public static class SoundCheck
{
    /// <summary>The tune the test plays: the theme the menus play (FINAL's career screens).</summary>
    private const int ThemeTune = 2;

    /// <summary>The ROM folder used when none is named: MT32_ROMS next to the program (a release carries it empty).</summary>
    public static string DefaultRomFolder => RomFolderSearch.DefaultFolder;

    /// <summary>Whether <paramref name="folder"/> holds an MT-32's ROMs, and a CM-32L's.</summary>
    /// <param name="folder">The folder looked in.</param>
    public static (bool Mt32, bool Cm32l) Roms(string folder) =>
        (Mt32Roms.Find(folder, RolandModule.Mt32, out _) is not null, Mt32Roms.Find(folder, RolandModule.Cm32l, out _) is not null);

    /// <summary>
    /// Starts the theme tune on the driver of <paramref name="letter"/> on the host's sound device; it plays until the
    /// device is closed.
    /// </summary>
    /// <param name="folder">The player's game folder (its driver files).</param>
    /// <param name="host">The host whose sound device and clock the driver plays on.</param>
    /// <param name="letter">The sound driver's letter (I, T, A, R).</param>
    /// <param name="romFolder">The MT-32 ROMs' folder; null: the one <see cref="RomFolderSearch.Automatic"/> finds.</param>
    /// <param name="report">Told why the Roland plays the AdLib driver instead, when it does (D-20).</param>
    /// <exception cref="FileNotFoundException">The driver file is not in the game folder.</exception>
    /// <exception cref="InvalidDataException">The driver file is not the driver.</exception>
    public static void PlayTheme(GameFolder folder, IHost host, char letter, string? romFolder, Action<string> report)
    {
        var sound = new SoundDriverChoice(folder, host, report, romFolder ?? RomFolderSearch.Automatic(folder.Path).Folder);
        sound.Choose(letter);
        sound.PlaySound(ThemeTune);
    }
}
