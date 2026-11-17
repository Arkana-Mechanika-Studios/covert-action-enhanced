// Adapted from the Darklands Restoration Project, src/Darklands.Platform/SettingsFile.cs; same author.
// Adapted: Covert Action's file name, folders and keys; the template is written when the launcher first saves (not on
// the first run); no CONFIG.DRK words, packs, mods or console.
using System.Globalization;
using System.Text.Json;

namespace CovertAction.Settings;

/// <summary>The outcome of reading a settings file: what it says and what was wrong with it (already ignored).</summary>
internal sealed record SettingsLoad(GameSettings Settings, string? Path, IReadOnlyList<string> Problems);

/// <summary>
/// covertaction.settings.json: where it is looked for, how it is read (comments and trailing commas allowed; unknown
/// keys and invalid values are reported and ignored, never fatal) and the commented template. New code (the host's
/// configuration); csharp/docs/SETTINGS.md describes the file.
/// </summary>
internal static class SettingsFile
{
    public const string FileName = "covertaction.settings.json";

    /// <summary>The folder of the player's account the file goes in when the program's own folder cannot be written.</summary>
    public const string AppDataFolder = "CovertAction";

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The places searched after an explicit --settings file, in order: next to the program, then %APPDATA%\CovertAction.</summary>
    public static IReadOnlyList<string> SearchPaths(string? programFolder = null, string? appData = null) =>
    [
        Path.Combine(programFolder ?? AppContext.BaseDirectory, FileName),
        Path.Combine(appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppDataFolder, FileName),
    ];

    /// <summary>The settings file to read: the explicit one (even when missing, so it can be reported), else the first existing of <see cref="SearchPaths"/>.</summary>
    public static string? Locate(string? explicitFile, string? programFolder = null, string? appData = null) =>
        explicitFile ?? SearchPaths(programFolder, appData).FirstOrDefault(File.Exists);

    /// <summary>
    /// Where the launcher saves when no file was found: next to the program when that folder can be written (a release
    /// unpacked into the player's own folder), else in %APPDATA%\CovertAction.
    /// </summary>
    public static string WritablePath(string? programFolder = null, string? appData = null)
    {
        var paths = SearchPaths(programFolder, appData);
        return CanWriteIn(Path.GetDirectoryName(paths[0])!) ? paths[0] : paths[1];
    }

    private static bool CanWriteIn(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".covert-write-check-{Environment.ProcessId}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads the file; a missing or unreadable one gives the defaults and a problem.</summary>
    public static SettingsLoad Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoad(new GameSettings(), path, [$"settings: cannot read {path}: {e.Message}"]);
        }
        var load = Parse(text, Path.GetDirectoryName(Path.GetFullPath(path)));
        return load with { Path = path, Problems = [.. load.Problems.Select(p => $"settings: {path}: {p}")] };
    }

    /// <summary>
    /// Reads settings from text. Relative folders are taken relative to <paramref name="baseFolder"/> (the settings
    /// file's folder), and %VARIABLES% in them are expanded.
    /// </summary>
    public static SettingsLoad Parse(string text, string? baseFolder = null)
    {
        var settings = new GameSettings();
        var problems = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ReadOptions);
        }
        catch (JsonException e)
        {
            problems.Add($"not valid JSON ({e.Message}); every setting ignored");
            return new SettingsLoad(settings, null, problems);
        }
        using (document)
        {
            new Reader(problems, baseFolder).Root(document.RootElement, settings);
        }
        return new SettingsLoad(settings, null, problems);
    }

    /// <summary>Writes the commented template to <paramref name="path"/> (creating its folder); false when the file already exists or cannot be written.</summary>
    public static bool TryWriteTemplate(string path, out string message)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(Template);
            message = $"wrote the settings template {path}";
            return true;
        }
        catch (IOException e) when (File.Exists(path))
        {
            message = $"{path} already exists ({e.Message}); not overwritten";
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            message = $"cannot write {path}: {e.Message}";
            return false;
        }
    }

    /// <summary>"WIDTHxHEIGHT" with positive numbers, or null.</summary>
    public static (int Width, int Height)? ParseResolution(string? text)
    {
        var parts = (text ?? "").Split('x', 'X');
        return parts.Length == 2
            && int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var w)
            && int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var h) && w > 0 && h > 0
            ? (w, h)
            : null;
    }

    private sealed class Reader(List<string> problems, string? baseFolder)
    {
        public void Root(JsonElement root, GameSettings settings)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add("the top level must be an object; every setting ignored");
                return;
            }
            foreach (var property in root.EnumerateObject())
            {
                var name = property.Name;
                var value = property.Value;
                switch (name)
                {
                    case "gameFolder": settings.GameFolder = FilePath(name, value); break;
                    case "savesFolder": settings.SavesFolder = FilePath(name, value); break;
                    case "display": Section(name, value, settings, Display); break;
                    case "sound": Section(name, value, settings, Sound); break;
                    case "startup": Section(name, value, settings, Startup); break;
                    case "input": Section(name, value, settings, Input); break;
                    case "graphics": Section(name, value, settings, Graphics); break;
                    case "fixes": Section(name, value, settings, Fixes); break;
                    case "cutContent": Section(name, value, settings, CutContent); break;
                    default: Unknown(name); break;
                }
            }
            if (settings.Resolution is not null)
            {
                // a resolution replaces the scale (the template carries "scale": 3 beside an empty resolution)
                settings.Scale = null;
            }
        }

        private void Section(string name, JsonElement value, GameSettings settings, Action<string, JsonProperty, GameSettings> apply)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return;
            }
            if (value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"\"{name}\" must be an object; ignored");
                return;
            }
            foreach (var property in value.EnumerateObject())
            {
                apply(name, property, settings);
            }
        }

        private void Display(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "scale":
                    if (Integer(key, property.Value) is { } scale)
                    {
                        if (scale is < 1 or > 8)
                        {
                            problems.Add($"\"{key}\" must be a whole number from 1 to 8; ignored");
                        }
                        else
                        {
                            settings.Scale = scale;
                        }
                    }
                    break;
                case "resolution":
                    if (Text(key, property.Value) is { } text)
                    {
                        if (ParseResolution(text) is null)
                        {
                            problems.Add($"\"{key}\" must be WIDTHxHEIGHT with positive numbers, e.g. \"1280x960\" (not \"{text}\"); ignored");
                        }
                        else
                        {
                            settings.Resolution = text;
                        }
                    }
                    break;
                case "fullscreen": settings.Fullscreen = Boolean(key, property.Value) ?? settings.Fullscreen; break;
                case "scaling": settings.Scaling = OneOf(key, property.Value, GameSettings.Scalings) ?? settings.Scaling; break;
                default: Unknown(key); break;
            }
        }

        private void Sound(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "card" when IsFormerSetupCard(property.Value):
                    // an older file's "the set-up screen asks": the set-up screen no longer asks, the default card plays
                    settings.SoundCard = null;
                    break;
                case "card": settings.SoundCard = OneOf(key, property.Value, GameSettings.SoundCards) ?? settings.SoundCard; break;
                case "mt32Roms": settings.Mt32Roms = FilePath(key, property.Value); break;
                case "music": settings.MusicCard = OneOf(key, property.Value, GameSettings.MusicChoices) ?? settings.MusicCard; break;
                case "amigaFiles": settings.AmigaFiles = FilePath(key, property.Value); break;
                case "amigaOriginalOnly": settings.AmigaOriginalOnly = Boolean(key, property.Value) ?? settings.AmigaOriginalOnly; break;
                case "effects": settings.EffectsCard = OneOf(key, property.Value, GameSettings.EffectsChoices) ?? settings.EffectsCard; break;
                default: Unknown(key); break;
            }
        }

        private static bool IsFormerSetupCard(JsonElement value) =>
            value.ValueKind == JsonValueKind.String && string.Equals(value.GetString()?.Trim(), GameSettings.FormerSetupCard, StringComparison.OrdinalIgnoreCase);

        private void Startup(string section, JsonProperty property, GameSettings settings)
        {
            // an older file's "skipSetup": the set-up screen no longer asks anything, so there is nothing to skip; ignored
            if (property.Name != "skipSetup")
            {
                Unknown($"{section}.{property.Name}");
            }
        }

        private void Input(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "controls": settings.Controls = OneOf(key, property.Value, GameSettings.ControlChoices) ?? settings.Controls; break;
                case "keyRepeat": settings.KeyRepeat = OneOf(key, property.Value, GameSettings.KeyRepeats) ?? settings.KeyRepeat; break;
                case "arrowKeysWalk": settings.ArrowKeysWalk = Boolean(key, property.Value) ?? settings.ArrowKeysWalk; break;
                case "keyMap": settings.KeyMap = Pairs(key, property.Value, pad: false) ?? settings.KeyMap; break;
                case "padMap": settings.PadMap = Pairs(key, property.Value, pad: true) ?? settings.PadMap; break;
                case "mouse": settings.Mouse = Boolean(key, property.Value) ?? settings.Mouse; break;
                default: Unknown(key); break;
            }
        }

        private void Graphics(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "amiga": settings.AmigaPictures = Boolean(key, property.Value) ?? settings.AmigaPictures; break;
                default: Unknown(key); break;
            }
        }

        private void Fixes(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "originalBugs": settings.FixOriginalBugs = Boolean(key, property.Value) ?? settings.FixOriginalBugs; break;
                case "campaignBugs": settings.FixCampaignBugs = Boolean(key, property.Value) ?? settings.FixCampaignBugs; break;
                default: Unknown(key); break;
            }
        }

        private void CutContent(string section, JsonProperty property, GameSettings settings)
        {
            var key = $"{section}.{property.Name}";
            switch (property.Name)
            {
                case "capturedDossier": settings.CutCapturedDossier = Boolean(key, property.Value) ?? settings.CutCapturedDossier; break;
                case "standaloneChase": settings.CutStandaloneChase = Boolean(key, property.Value) ?? settings.CutStandaloneChase; break;
                case "hardestCircuit": settings.CutHardestCircuit = Boolean(key, property.Value) ?? settings.CutHardestCircuit; break;
                case "fillerTexts": settings.CutFillerTexts = Boolean(key, property.Value) ?? settings.CutFillerTexts; break;
                default: Unknown(key); break;
            }
        }

        private void Unknown(string key) => problems.Add($"unknown key \"{key}\" ignored");

        private string? OneOf(string key, JsonElement value, IReadOnlyList<string> choices)
        {
            if (Text(key, value) is not { } text)
            {
                return null;
            }
            var known = choices.FirstOrDefault(c => string.Equals(c, text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                problems.Add($"\"{key}\" must be one of {string.Join(", ", choices)} (not \"{text}\"); ignored");
            }
            return known;
        }

        private string? Text(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                problems.Add($"\"{key}\" must be a string; ignored");
                return null;
            }
            return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString();
        }

        private string? FilePath(string key, JsonElement value)
        {
            var text = Text(key, value);
            if (text is null)
            {
                return null;
            }
            text = Environment.ExpandEnvironmentVariables(text);
            return baseFolder is null || Path.IsPathRooted(text) ? text : Path.GetFullPath(Path.Combine(baseFolder, text));
        }

        // D-671, D-672: a list of "left=right" texts; the pairs that do not read are left out with a message
        private List<string>? Pairs(string key, JsonElement value, bool pad)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"\"{key}\" must be a list of texts like \"Q=Up\"; ignored");
                return null;
            }
            var texts = value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null);
            return [.. KeyBindings.ParsePairs(texts, pad, key, problems).Select(KeyBindings.Format)];
        }

        private bool? Boolean(string key, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                default:
                    problems.Add($"\"{key}\" must be true or false; ignored");
                    return null;
            }
        }

        private int? Integer(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }
            problems.Add($"\"{key}\" must be a whole number; ignored");
            return null;
        }
    }

    /// <summary>The commented file the launcher writes before its first save: every key, its default and the command-line option that does the same.</summary>
    public const string Template = """
        // Covert Action (C# rewrite) settings. Comments (// and /* */) and trailing commas are allowed.
        // The launcher ("CovertAction Launcher.exe", or CovertAction.exe --setup, or Shift held as it starts) edits this file.
        // Each key shows its default; "" (empty text) means the default described above it, and a key left out takes its default.
        // Precedence: command line > environment variables > this file > defaults.
        // Relative paths are taken relative to this file's folder; %VARIABLES% are expanded.
        // File lookup: --settings <file>, else next to the program, else %APPDATA%\CovertAction\covertaction.settings.json.
        {
          // Your copy of Sid Meier's Covert Action: the folder with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE, FONTS.CV ...
          // (or a folder above it with a Covert subfolder). "": the program's folder, the one above it, the current folder.
          // Command line: the first argument; environment: COVERT_GAME_DIR.
          "gameFolder": "",

          // Where your saved games (CV0.SVE .. CV3.SVE) and the Hall of Fame go; the game folder itself is never changed.
          // "": CovertAction\Saves in your local application data. Command line: --saves <folder>.
          "savesFolder": "",

          "display": {
            // The window is 320x240 times this whole number (the picture's 4:3 shape), 1 to 8. Command line: --scale N.
            "scale": 3,
            // Or any window size, "WIDTHxHEIGHT" such as "1280x960"; when set it replaces scale. Command line: --window WxH.
            "resolution": "",
            // Command line: --fullscreen.
            "fullscreen": false,
            // How the 320x200 picture is enlarged: "pixelart" (every game pixel the same size, only the edges between
            // them blended), "nearest" (hard pixels; some a window pixel wider than others) or "smooth" (blurred).
            // Command line: --scaling <name>.
            "scaling": "pixelart"
          },

          "sound": {
            // The card the game plays on (the set-up screen's sound question of the original): "none", "speaker"
            // (PC speaker), "tandy", "adlib" or "mt32" (Roland MT-32). Command line: --sound <card>.
            "card": "adlib",
            // The folder with your MT-32 (or CM-32L) ROM files. "": MT32_ROMS next to the program.
            // Without the ROMs the AdLib driver plays instead (one line on the console says so).
            // Command line: --mt32-roms <folder>; environment: COVERT_MT32_ROMS.
            "mt32Roms": "",
            // Music on one card, sound effects on another, mixed: "card" (the sound card plays both, as the original),
            // or "none", "speaker", "tandy", "adlib", "mt32" for the tunes, and for the effects (D-620). The tunes may also
            // be "amiga": the Amiga version's music, played from your own Amiga files (D-640); the effects likewise (D-641).
            "music": "card",
            "effects": "card",
            // The folder with your Amiga files: the music (thememusic.dat, finalmusic.dat, spy6music.dat; the Amiga disk 1)
            // and the sound effects (gamesnd.dat, tacsnd.dat, codesnd.dat, bugsnd.dat; disk 2, chasesnd.dat; disk 3).
            // "": amiga/files next to the program, or next to the game. Without them the sound card plays instead.
            "amigaFiles": "",
            // true: the Amiga music and effects only where the Amiga game had them (music: the title, the menus and FINAL; the
            // selection screens' and the vacations' tunes stay silent; effects: those the Amiga game does not play stay silent).
            // false: the sound card plays those.
            "amigaOriginalOnly": false
          },

          "graphics": {
            // true: the Amiga version's pictures, read from your own Amiga files (the folder of sound.amigaFiles; the .pix
            // files of the three disks), in place of the PC's where the Amiga version has the same picture on the same
            // layout (D-642). false: the PC's pictures. Without the files the PC's pictures show.
            "amiga": false
          },

          "input": {
            // The controls (the set-up screen's controls question of the original): "joystick" (keyboard and joystick:
            // the game has the joystick or gamepad calibrated first) or "keyboard" (keyboard only).
            // Command line: --controls <joystick|keyboard>.
            "controls": "joystick",
            // How a held key repeats: "pc" (as the PC keyboard: after half a second, 10.9 times a second) or "windows"
            // (Windows' own delay and rate). Command line: --key-repeat <pc|windows>; environment: COVERT_KEY_REPEAT=system.
            "keyRepeat": "pc",

            // true: in a building raid the grey arrow keys walk the agent like the keypad's digits, and Shift with an
            // arrow lunges. false: the original's keys (the keypad walks, the grey arrows lunge). Command line:
            // --arrow-keys-walk.
            "arrowKeysWalk": false,

            // Key remapping (not in the original): a list of "your key=the original's key", e.g. ["Q=Up", "CapsLock=Esc"].
            // Keys are named as in SETTINGS.md; empty: the original's keys. The launcher's Controls tab edits it.
            "keyMap": [],
            // Gamepad buttons beyond the joystick's four (not in the original): a list of "button=the original's key", e.g.
            // ["Start=Esc"]. Buttons: LeftShoulder, RightShoulder, LeftTrigger, RightTrigger, Start, Back, LeftStick, RightStick.
            "padMap": [],

            // true: the mouse in the menus, lists and maps (not in the original): pointing moves the highlight, a left
            // click takes the choice, a right click is Esc. Command line: --mouse, --no-mouse.
            "mouse": false
          },

          "fixes": {
            // true: fix the original's bugs that do not change the story (a clue text, a key left in the buffer, the
            // name field's Delete key, the chief's comment after a capture, the Tandy driver's second pause, and
            // others the release notes list). false: the original's bugs, as it played in 1990.
            "originalBugs": false,
            // true: also fix the bugs of the campaign's logic. false: as the original.
            "campaignBugs": false
          },

          "cutContent": {
            // true: the dossier page stamped CAPTURED (CAPTRD01.PAN, which the original ships but never plays) is shown
            // when a cast member is captured, before the chief's comment. false: as the original.
            "capturedDossier": false,
            // true: the practice menu has a fifth choice, the car chase on its own as the original's program still
            // carries it (a random map, places labelled Loc1, Loc2 ..., a random start and goal, every place shown).
            // false: as the original.
            "standaloneChase": false,
            // true: the electronics board has a difficulty more, the fourth the game's own code carries: on the hardest
            // level an agent of the lowest electronics rating gets a board that shows only its last column of chips and
            // has eight fixed hidden ones. false: as the original (the hardest board is the third).
            "hardestCircuit": false,
            // true: the filler sentence of a coded message is one of all 16 in TEXT.DTA; the game's code asks for 12 and
            // never shows the last four ("Extreme caution is advised ...", "Do not hesitate to act in a decisive manner",
            // ...). false: as the original.
            "fillerTexts": false
          }
        }
        """;
}
