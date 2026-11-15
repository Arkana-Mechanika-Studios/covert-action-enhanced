// Adapted from the Darklands Restoration Project, src/Darklands.Host.Sdl/LauncherHost.cs; same author.
// Adapted: Covert Action's context (the game-folder check, the MT-32 ROMs, the saves folder, a picture of the player's
// game, the VGA font); the launcher has a window of its own; no packs, filter previews, problem reports or web links.
using System.Text.Json.Nodes;
using CovertAction.Content;
using CovertAction.Launcher;
using CovertAction.Settings;

namespace CovertAction.Host.Sdl;

/// <summary>
/// The launcher (CovertAction.Settings) in an SDL window: what it is given from this program (the game-folder checks,
/// the ROM status, the release notes, the sound test) and the saving of what the player changed. New code.
/// </summary>
internal static class LauncherHost
{
    /// <summary>The picture of the player's own game shown faintly behind the launcher (an office of the agency's).</summary>
    private const string BackgroundPicture = "HOTEL.PIC";

    /// <summary>
    /// Shows the launcher; saves the changes into <paramref name="settingsPath"/> (its template is written first when the
    /// file is missing). Returns the settings file's values to play with, or null when the player left.
    /// </summary>
    public static GameSettings? Run(LauncherReason reason, string? problem, GameSettings settings, StartSettings start, string? settingsPath)
    {
        using var soundTest = new SdlSoundTest();
        var context = Context(reason, problem, settings, start, settingsPath, soundTest);
        LauncherResult result;
        using (var window = new SdlLauncherWindow("Covert Action - settings"))
        {
            result = new Settings.Launcher().Run(context, window);
        }
        Save(settingsPath, result.Changes);
        return result.Choice == LauncherChoice.Play ? result.Settings : null;
    }

    /// <summary>What the launcher is given (also for --launcher-screenshot, without a window or a sound test).</summary>
    internal static LauncherContext Context(LauncherReason reason, string? problem, GameSettings settings, StartSettings start, string? settingsPath, ISoundTest? soundTest) =>
        new(settings,
            start,
            Version,
            reason,
            problem,
            FontBytes(),
            GameFolderSearch.GameFolderIn,
            () => GameFolderSearch.Found(AppContext.BaseDirectory, Environment.CurrentDirectory),
            DescribeRoms,
            SavesFolder.Default,
            soundTest,
            ReleaseNotes(),
            settingsPath,
            Picture,
            AutomaticRoms);

    /// <summary>The program's version (Directory.Build.props; the release script sets it).</summary>
    internal static string Version =>
        typeof(LauncherHost).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>The VGA 8x16 font as 4096 bytes (the set-up screen's).</summary>
    internal static byte[] FontBytes()
    {
        var font = TextFont.Builtin;
        var bytes = new byte[TextFont.ByteLength];
        for (var c = 0; c < 256; c++)
        {
            for (var row = 0; row < TextFont.GlyphHeight; row++)
            {
                bytes[c * TextFont.GlyphHeight + row] = font.Row((byte)c, row);
            }
        }
        return bytes;
    }

    internal static RomStatus DescribeRoms(string? chosen)
    {
        var folder = string.IsNullOrWhiteSpace(chosen) ? SoundCheck.DefaultRomFolder : chosen;
        try
        {
            var (mt32, cm32l) = SoundCheck.Roms(folder);
            return new RomStatus(folder, mt32, cm32l);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new RomStatus(folder, false, false);
        }
    }

    /// <summary>The ROMs when no folder is chosen: where <see cref="RomFolderSearch"/> finds a usable set (around the game folder, DOSBox's, Munt's), else the default folder.</summary>
    internal static RomStatus AutomaticRoms(string? gameFolder)
    {
        var (folder, source) = RomFolderSearch.Automatic(gameFolder);
        var status = DescribeRoms(folder);
        return source is null ? status : status with { Source = source };
    }

    /// <summary>A picture of the player's game in its own colours (the graphics driver's), or null when it cannot be read.</summary>
    internal static uint[]? Picture(string gameFolder)
    {
        try
        {
            var folder = new GameFolder(gameFolder);
            var colours = McgaDriverFile.Read(folder.Read("MGRAPHIC.EXE")).DefaultColours;
            var picture = PictureFile.Read(folder.Read(BackgroundPicture));
            if (picture.Width != 320 || picture.Height != 200)
            {
                return null;
            }
            static uint Widen(byte six) => (uint)((six & 0x3f) << 2 | (six & 0x3f) >> 4);
            var argb = new uint[320 * 200];
            for (var i = 0; i < argb.Length; i++)
            {
                var index = picture.Pixels[i] % McgaDriverFile.Colours * 3;
                argb[i] = Widen(colours[index]) << 16 | Widen(colours[index + 1]) << 8 | Widen(colours[index + 2]);
            }
            return argb;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The changed keys into the settings file (the commented template written first when there is none yet).</summary>
    internal static void Save(string? settingsPath, IReadOnlyDictionary<string, JsonNode?> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }
        if (settingsPath is null)
        {
            Console.Error.WriteLine("Settings: no settings file could be written; the changes last until the program ends.");
            return;
        }
        if (!File.Exists(settingsPath) && !SettingsFile.TryWriteTemplate(settingsPath, out var written))
        {
            Console.Error.WriteLine("Settings: " + written);
            return;
        }
        var saved = SettingsFileEdits.TrySet(settingsPath, changes, out var message);
        (saved ? Console.Out : Console.Error).WriteLine("Settings: " + message);
    }

    /// <summary>RELEASE_NOTES.md next to the program (copied there by the build), or nothing.</summary>
    private static string ReleaseNotes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RELEASE_NOTES.md");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
