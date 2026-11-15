namespace CovertAction.Settings;

/// <summary>
/// The settings a run starts with: the command line over the environment over the settings file over the defaults,
/// each value from the first that names it. New code (the host's configuration); csharp/docs/SETTINGS.md.
/// </summary>
internal sealed record StartSettings
{
    /// <summary>The window's width per scale step: the 320x200 picture shown at 4:3 is 320x240 window pixels at scale 1.</summary>
    public const int BaseWidth = 320;

    public const int BaseHeight = 240;

    public const int DefaultScale = 3;

    /// <summary>The environment variables read, as SETTINGS.md lists them.</summary>
    public const string GameFolderVariable = "COVERT_GAME_DIR";

    public const string RomFolderVariable = "COVERT_MT32_ROMS";

    public const string KeyRepeatVariable = "COVERT_KEY_REPEAT";

    /// <summary>The game folder named (on the command line, by COVERT_GAME_DIR or in the file), or null: searched.</summary>
    public string? GameFolder { get; init; }

    public string? SavesFolder { get; init; }

    public int Width { get; init; } = BaseWidth * DefaultScale;

    public int Height { get; init; } = BaseHeight * DefaultScale;

    public bool Fullscreen { get; init; }

    public string Scaling { get; init; } = "pixelart";

    /// <summary>One of <see cref="GameSettings.SoundCards"/>: the set-up screen's sound answer.</summary>
    public string SoundCard { get; init; } = GameSettings.DefaultSoundCard;

    /// <summary>One of <see cref="GameSettings.ControlChoices"/>: the set-up screen's controls answer.</summary>
    public string Controls { get; init; } = GameSettings.DefaultControls;

    /// <summary>The controls are the keyboard and the joystick (the set-up's joystick flag), else the keyboard only.</summary>
    public bool Joystick => Controls == "joystick";

    public string? Mt32Roms { get; init; }

    /// <summary>A held key repeats at Windows' rate (else the PC keyboard's).</summary>
    public bool SystemKeyRepeat { get; init; }

    /// <summary>In a building raid the grey arrow keys walk (D-610).</summary>
    public bool ArrowKeysWalk { get; init; }

    /// <summary>The card the tunes play on: "card" (the sound card, as the original) or a card of its own (D-620).</summary>
    public string MusicCard { get; init; } = "card";

    /// <summary>The card the sound effects play on: "card" (the sound card, as the original) or a card of its own (D-620).</summary>
    public string EffectsCard { get; init; } = "card";

    /// <summary>The mouse in the menus, lists and maps (input.mouse; off by default, as the original has none).</summary>
    public bool Mouse { get; init; }

    /// <summary>
    /// The settings-file keys the command line or the environment sets for this run, with what sets them ("the command
    /// line (--scale)"): the launcher shows them but cannot change them.
    /// </summary>
    public IReadOnlyDictionary<string, string> FixedKeys { get; init; } = new Dictionary<string, string>();

    /// <summary>The settings of a run: <paramref name="cli"/> over <paramref name="environment"/> over <paramref name="file"/> over the defaults.</summary>
    public static StartSettings Resolve(CommandLine cli, Func<string, string?> environment, GameSettings file)
    {
        var fixedKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        string? Env(string name) => environment(name) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value : null;
        string? Pick(string key, string? fromCli, string option, string? fromEnv, string? variable, string? fromFile)
        {
            if (fromCli is not null)
            {
                fixedKeys[key] = $"the command line ({option})";
                return fromCli;
            }
            if (fromEnv is not null)
            {
                fixedKeys[key] = $"the environment ({variable})";
                return fromEnv;
            }
            return fromFile;
        }

        var game = Pick("gameFolder", cli.GameFolder, "the game folder", Env(GameFolderVariable), GameFolderVariable, file.GameFolder);
        var saves = Pick("savesFolder", cli.SavesFolder, "--saves", null, null, file.SavesFolder);
        var roms = Pick("sound.mt32Roms", cli.Mt32Roms, "--mt32-roms", Env(RomFolderVariable), RomFolderVariable, file.Mt32Roms);
        var envRepeat = Env(KeyRepeatVariable) is { } repeat
            ? string.Equals(repeat, "system", StringComparison.OrdinalIgnoreCase) || string.Equals(repeat, "windows", StringComparison.OrdinalIgnoreCase) ? "windows" : "pc"
            : null;
        var keyRepeat = Pick("input.keyRepeat", cli.KeyRepeat, "--key-repeat", envRepeat, KeyRepeatVariable, file.KeyRepeat) ?? "pc";
        var scaling = Pick("display.scaling", cli.Scaling, "--scaling", null, null, file.Scaling) ?? "pixelart";
        var card = Pick("sound.card", cli.SoundCard, "--sound", null, null, file.SoundCard) ?? GameSettings.DefaultSoundCard;
        var controls = Pick("input.controls", cli.Controls, "--controls", null, null, file.Controls) ?? GameSettings.DefaultControls;
        bool Switch(string key, bool? fromCli, string option, bool? fromFile)
        {
            if (fromCli is { } given)
            {
                fixedKeys[key] = $"the command line ({option})";
                return given;
            }
            return fromFile ?? false;
        }
        var fullscreen = Switch("display.fullscreen", cli.Fullscreen, "--fullscreen", file.Fullscreen);
        var arrowsWalk = Switch("input.arrowKeysWalk", cli.ArrowKeysWalk, "--arrow-keys-walk", file.ArrowKeysWalk);
        var mouse = Switch("input.mouse", cli.Mouse, cli.Mouse == false ? "--no-mouse" : "--mouse", file.Mouse);

        // the window: a size on the command line (--window or --scale) replaces both keys of the file
        (int Width, int Height) size;
        if (cli.Resolution is not null || cli.Scale is not null)
        {
            var option = cli.Resolution is not null ? "--window" : "--scale";
            fixedKeys["display.scale"] = fixedKeys["display.resolution"] = $"the command line ({option})";
            size = SettingsFile.ParseResolution(cli.Resolution) ?? (BaseWidth * cli.Scale!.Value, BaseHeight * cli.Scale!.Value);
        }
        else
        {
            size = SettingsFile.ParseResolution(file.Resolution) ?? (BaseWidth * (file.Scale ?? DefaultScale), BaseHeight * (file.Scale ?? DefaultScale));
        }

        return new StartSettings
        {
            GameFolder = game,
            SavesFolder = saves,
            Width = size.Width,
            Height = size.Height,
            Fullscreen = fullscreen,
            Scaling = scaling,
            SoundCard = card,
            Controls = controls,
            Mt32Roms = roms,
            SystemKeyRepeat = keyRepeat == "windows",
            ArrowKeysWalk = arrowsWalk,
            MusicCard = file.MusicCard ?? "card",
            EffectsCard = file.EffectsCard ?? "card",
            Mouse = mouse,
            FixedKeys = fixedKeys,
        };
    }
}
