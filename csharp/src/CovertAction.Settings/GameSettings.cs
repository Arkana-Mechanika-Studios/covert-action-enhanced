// Adapted from the Darklands Restoration Project, src/Darklands.Platform/GameSettings.cs; same author.
// Adapted: Covert Action's settings (the sound card and the controls, the picture's scaling, the keyboard's repeat); no
// packs, filters, mods or console.
namespace CovertAction.Settings;

/// <summary>
/// The player's settings file (covertaction.settings.json). Every value is optional: null means "not set", so the
/// host can tell what the player chose from what a default fills in. New code (the host's configuration, not part of
/// the original); csharp/docs/SETTINGS.md describes the file.
/// </summary>
internal sealed class GameSettings
{
    /// <summary>The sound cards the game can play on: the set-up screen's sound answer (D-603).</summary>
    public static readonly IReadOnlyList<string> SoundCards = ["none", "speaker", "tandy", "adlib", "mt32"];

    /// <summary>The sound card without a setting: the AdLib.</summary>
    public const string DefaultSoundCard = "adlib";

    /// <summary>
    /// The card of an older settings file whose set-up screen asked for the card: read as <see cref="DefaultSoundCard"/>,
    /// without a problem (the set-up screen no longer asks).
    /// </summary>
    public const string FormerSetupCard = "setup";

    /// <summary>The controls: "joystick" (keyboard and joystick) or "keyboard" (keyboard only): the set-up screen's controls answer (D-603).</summary>
    public static readonly IReadOnlyList<string> ControlChoices = ["joystick", "keyboard"];

    /// <summary>The controls without a setting: keyboard and joystick.</summary>
    public const string DefaultControls = "joystick";

    /// <summary>How the 320x200 picture is enlarged to the window: pixel art (even pixels), nearest (hard pixels), smooth.</summary>
    public static readonly IReadOnlyList<string> Scalings = ["pixelart", "nearest", "smooth"];

    /// <summary>How a held key repeats: as the PC keyboard did (10.9 a second after half a second), or at Windows' rate.</summary>
    public static readonly IReadOnlyList<string> KeyRepeats = ["pc", "windows"];

    public string? GameFolder { get; set; }

    public string? SavesFolder { get; set; }

    /// <summary>The window is 320x240 times this whole number (the picture's 4:3 shape).</summary>
    public int? Scale { get; set; }

    /// <summary>The window size, "WIDTHxHEIGHT"; when set it replaces <see cref="Scale"/>.</summary>
    public string? Resolution { get; set; }

    public bool? Fullscreen { get; set; }

    /// <summary>One of <see cref="Scalings"/>.</summary>
    public string? Scaling { get; set; }

    /// <summary>One of <see cref="SoundCards"/>.</summary>
    public string? SoundCard { get; set; }

    public string? Mt32Roms { get; set; }

    /// <summary>One of <see cref="ControlChoices"/>.</summary>
    public string? Controls { get; set; }

    /// <summary>One of <see cref="KeyRepeats"/>.</summary>
    public string? KeyRepeat { get; set; }

    /// <summary>In a building raid the grey arrow keys walk like the keypad's digits and Shift with them lunges (D-610; off: the original's keys).</summary>
    public bool? ArrowKeysWalk { get; set; }

    /// <summary>The cards the music or the effects can play on: "card" (the sound card plays both, as the original), else a card of its own (D-620).</summary>
    public static readonly IReadOnlyList<string> PartCards = ["card", "none", "speaker", "tandy", "adlib", "mt32"];

    /// <summary>One of <see cref="PartCards"/>: the card the tunes play on.</summary>
    public string? MusicCard { get; set; }

    /// <summary>One of <see cref="PartCards"/>: the card the sound effects play on.</summary>
    public string? EffectsCard { get; set; }

    /// <summary>The mouse in the menus, lists and maps (input.mouse; not in the original, off by default).</summary>
    public bool? Mouse { get; set; }

    /// <summary>A copy (a change to it leaves this one as it is).</summary>
    public GameSettings Copy() => (GameSettings)MemberwiseClone();

    /// <summary>The sound driver's letter of a sound card (the set-up's letters: N, I, T, A, R); the AdLib's for any other.</summary>
    public static char SoundLetter(string? card) => card switch
    {
        "none" => 'N',
        "speaker" => 'I',
        "tandy" => 'T',
        "mt32" => 'R',
        _ => 'A',
    };

    /// <summary>
    /// The driver's letter of the music's or the effects' card (D-620): null for "card" (or none set), which plays on the
    /// sound card as the original; else the letter of the card of its own.
    /// </summary>
    public static char? PartLetter(string? part) => part is null || part == "card" ? null : SoundLetter(part);
}
