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

    /// <summary>Effects the mix test plays in turn over the theme: a shot, the Uzi's burst, a door (TAC's commands 3, 6, 13).</summary>
    public static IReadOnlyList<int> TestEffects { get; } = [3, 6, 13];

    /// <summary>
    /// Starts the theme tune on the music card and returns the game's sound driver with the music and the effects on two
    /// cards (D-620): the caller plays <see cref="TestEffects"/> on it now and then, which the effects card plays.
    /// </summary>
    /// <param name="folder">The player's game folder (its driver files).</param>
    /// <param name="host">The host whose sound device and clock the two drivers play on.</param>
    /// <param name="musicLetter">The music card's driver letter (N, I, T, A, R).</param>
    /// <param name="effectsLetter">The effects card's driver letter.</param>
    /// <param name="romFolder">The MT-32 ROMs' folder; null: the default.</param>
    /// <param name="report">Told why a Roland plays the AdLib driver instead, or the Amiga music the sound card's, when it does (D-20, D-640).</param>
    /// <param name="cardLetter">The sound card's driver letter, which plays the music when the Amiga files are missing; null: the music's letter.</param>
    /// <param name="amigaFolder">With <see cref="SoundDriverChoice.AmigaMusic"/> as the music's letter: the player's Amiga files' folder; null: found beside the program or the game (D-640).</param>
    /// <param name="amigaOnlyWhereOriginal">The Amiga music plays only where the Amiga game had music (D-640).</param>
    /// <exception cref="FileNotFoundException">A driver file is not in the game folder.</exception>
    /// <exception cref="InvalidDataException">A driver file is not the driver.</exception>
    public static ISoundDriver PlayMix(GameFolder folder, IHost host, char musicLetter, char effectsLetter, string? romFolder, Action<string> report,
        char? cardLetter = null, string? amigaFolder = null, bool amigaOnlyWhereOriginal = false)
    {
        var sound = new SoundDriverChoice(folder, host, report, romFolder ?? DefaultRomFolder, musicLetter, effectsLetter, amigaFolder, amigaOnlyWhereOriginal);
        sound.Choose(cardLetter ?? musicLetter);
        sound.PlaySound(ThemeTune);
        return sound;
    }
}
