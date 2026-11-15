// Adapted from the Darklands Restoration Project, src/Darklands.Launcher/ILauncher.cs; same author.
// Adapted: Covert Action's context (the set-up screen's sound cards, the saves folder, the MT-32 ROMs, a picture of the
// player's own game, the 8x16 text font); the surface is drawn at one design size and scaled by the host; no packs,
// filter previews, problem reports or frameless window.
using System.Text.Json.Nodes;

namespace CovertAction.Settings;

/// <summary>
/// The settings screen shown before the game. A host gives it the settings and a few services, and a surface to draw
/// on; it returns what the player chose and the settings-file keys to save. It writes no file: the host saves.
/// Another launcher (another look, another toolkit) fits in by implementing this.
/// </summary>
internal interface ILauncher
{
    LauncherResult Run(LauncherContext context, ILauncherSurface surface);
}

/// <summary>Why the launcher is shown.</summary>
internal enum LauncherReason
{
    /// <summary>The player asked for it (--setup, Shift held, CovertAction Launcher.exe).</summary>
    Asked,

    /// <summary>The game folder is not set or holds no Covert Action.</summary>
    GameNotFound,

    /// <summary>The game could not start; <see cref="LauncherContext.Problem"/> says why.</summary>
    StartFailed,
}

/// <summary>What the launcher is given.</summary>
/// <param name="Settings">The settings file's values.</param>
/// <param name="Start">The settings of this run (the command line and environment over the file): what is fixed, and the effective values.</param>
/// <param name="Version">The program's version.</param>
/// <param name="Reason">Why the launcher is shown.</param>
/// <param name="Problem">A message for the player (why the game could not start), or null.</param>
/// <param name="Font">The VGA 8x16 text font: 256 characters of 16 rows, a byte a row (bit 7 the leftmost dot).</param>
/// <param name="CheckGameFolder">A folder the player named → the game folder in it, or null.</param>
/// <param name="FoundGameFolders">The game folders found in the usual places, with where each was found.</param>
/// <param name="DescribeRoms">The ROM folder of the settings (null: the default) → what is in the folder it stands for.</param>
/// <param name="AutomaticRoms">The game folder (or null) → the ROM folder looked for in the usual places when none is chosen, and what is in it; null: only the default folder.</param>
/// <param name="DefaultSavesFolder">The saves folder used when none is set.</param>
/// <param name="SoundTest">Plays a little of the game's music on request; null when the host cannot.</param>
/// <param name="ReleaseNotes">The release notes shipped with the program (Markdown; its items up to "Getting started" are shown).</param>
/// <param name="SettingsPath">The settings file the changes are saved into (null: none can be written).</param>
/// <param name="Picture">The game folder → a picture of the player's own game to show behind the screen (320x200 ARGB), or null.</param>
internal sealed record LauncherContext(
    GameSettings Settings,
    StartSettings Start,
    string Version,
    LauncherReason Reason,
    string? Problem,
    byte[] Font,
    Func<string, string?> CheckGameFolder,
    Func<IReadOnlyList<(string Folder, string Source)>> FoundGameFolders,
    Func<string?, RomStatus> DescribeRoms,
    string DefaultSavesFolder,
    ISoundTest? SoundTest,
    string ReleaseNotes,
    string? SettingsPath,
    Func<string, uint[]?>? Picture = null,
    Func<string?, RomStatus>? AutomaticRoms = null);

/// <summary>
/// The sound test: a little of the game's music on the chosen card. It starts in the background; while it plays the
/// launcher draws again now and then to show its status.
/// </summary>
internal interface ISoundTest
{
    /// <summary>Starts the test.</summary>
    void Play(SoundTestRequest request);

    void Stop();

    /// <summary>Starting or playing.</summary>
    bool Playing { get; }

    /// <summary>What the test is doing ("Playing ..."), or why it could not play (then not <see cref="Playing"/>); null when stopped.</summary>
    string? Status { get; }
}

/// <summary>
/// What the sound test plays on: the sound card ("adlib", "mt32" ...), the game folder (its drivers) and the ROM folder;
/// for the test of the mix (D-620) also the effects' card, the music then playing on <paramref name="Card"/>.
/// </summary>
internal sealed record SoundTestRequest(string Card, string GameFolder, string? RomFolder, string? EffectsCard = null);

/// <summary>The Roland ROMs in a folder.</summary>
/// <param name="Folder">The folder looked in (the default one when none is set).</param>
/// <param name="Mt32">An MT-32's control and PCM ROMs are there.</param>
/// <param name="Cm32l">A CM-32L's control and PCM ROMs are there.</param>
/// <param name="Source">Where the folder was found ("beside the program", "DOSBox Staging's") when the player did not choose it; null when chosen, or the default.</param>
internal sealed record RomStatus(string Folder, bool Mt32, bool Cm32l, string? Source = null)
{
    public bool Found => Mt32 || Cm32l;
}

internal enum LauncherChoice
{
    /// <summary>Start the game with the settings.</summary>
    Play,

    /// <summary>Leave the program (after saving the changes, if any).</summary>
    Quit,
}

/// <summary>What the player chose, the settings file's values with the changes, and the keys that changed (dotted, with their new JSON values).</summary>
internal sealed record LauncherResult(LauncherChoice Choice, GameSettings Settings, IReadOnlyDictionary<string, JsonNode?> Changes);

/// <summary>A window the launcher draws on and reads input from. The launcher draws at its design size; the surface fits it to the window.</summary>
internal interface ILauncherSurface
{
    /// <summary>The screen's size, for the window sizes worth offering; null when unknown.</summary>
    (int Width, int Height)? DisplaySize { get; }

    /// <summary>Shows a finished frame of <paramref name="width"/> x <paramref name="height"/> ARGB pixels, row by row.</summary>
    void Present(ReadOnlySpan<uint> pixels, int width, int height);

    /// <summary>Waits for input, at most <paramref name="timeout"/> (null: as long as it takes); returns what came, or [<see cref="TimePassed"/>].</summary>
    IReadOnlyList<LauncherInput> Wait(TimeSpan? timeout);

    /// <summary>The clipboard's text, or null.</summary>
    string? Clipboard();

    /// <summary>
    /// Asks the system for a folder (its own dialog), given its title and where it opens; null when not available. The
    /// function returns null when the player cancelled, and throws <see cref="NotSupportedException"/> when no dialog could be shown.
    /// </summary>
    Func<string, string, string?>? PickFolder { get; }
}

/// <summary>An input event, in the launcher's design pixels.</summary>
internal abstract record LauncherInput;

internal sealed record PointerMoved(int X, int Y) : LauncherInput;

internal sealed record PointerPressed(int X, int Y) : LauncherInput;

/// <summary>The button let go: a button pressed and let go over itself acts then.</summary>
internal sealed record PointerReleased(int X, int Y) : LauncherInput;

internal sealed record KeyPressed(LauncherKey Key, bool Shift = false, bool Control = false) : LauncherInput;

internal sealed record PathDropped(string Path) : LauncherInput;

/// <summary>The window changed: draw again.</summary>
internal sealed record SurfaceResized : LauncherInput;

/// <summary>The window's close button.</summary>
internal sealed record CloseRequested : LauncherInput;

/// <summary>Nothing came for the time <see cref="ILauncherSurface.Wait"/> was given.</summary>
internal sealed record TimePassed : LauncherInput;

internal enum LauncherKey
{
    Up,
    Down,
    Left,
    Right,
    Enter,
    Space,
    Escape,
    Tab,
    Paste,
}
