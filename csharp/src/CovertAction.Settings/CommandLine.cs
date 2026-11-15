using System.Globalization;

namespace CovertAction.Settings;

/// <summary>
/// The program's command line: what it names, null for what it leaves to the environment, the settings file and the
/// defaults (<see cref="StartSettings"/>). New code (the host's configuration).
/// </summary>
internal sealed class CommandLine
{
    public const string Usage = """
        Covert Action (C# rewrite, an unofficial fan project; you need your own copy of the game)

          CovertAction [game folder] [options]

        Without options the game starts with the title of the original's set-up screen, as COVERT.EXE did; the sound card
        and the controls it asked for are settings here (--sound, --controls). The launcher (the settings
        screen) opens with --setup, while Shift is held as the program starts, or when the game folder is not found;
        "CovertAction Launcher.exe" beside the program opens it too. Its settings are kept in covertaction.settings.json
        (csharp/docs/SETTINGS.md); what is given here wins over that file for this run.

        game folder        your copy of the game (the folder with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE, FONTS.CV
                           and the other files); else COVERT_GAME_DIR, else the settings file, else the program's folder,
                           the one above it, or the current folder
        --saves <folder>   where saved games and the Hall of Fame go (default: CovertAction\Saves in local application data)
        --scale <n>        window pixels per game pixel, 1..8 (the window is 320x240 times n; default 3)
        --window <WxH>     any window size, e.g. 1280x960
        --fullscreen       fill the screen
        --scaling <name>   pixelart (default), nearest or smooth
        --sound <card>     none, speaker, tandy, adlib (default) or mt32
        --controls <how>   joystick (keyboard and joystick; default) or keyboard (keyboard only)
        --mt32-roms <dir>  the MT-32 ROM files' folder (default MT32_ROMS next to the program; COVERT_MT32_ROMS)
        --arrow-keys-walk  in a building raid the grey arrows walk like the keypad (Shift+arrow lunges)
        --key-repeat <how> pc (the PC keyboard's repeat; default) or windows (COVERT_KEY_REPEAT=system)
        --mouse, --no-mouse  the mouse in the menus, lists and maps (not in the original; default: off)
        --settings <file>  read and save this settings file instead of the one found
        --setup            open the launcher first; its Play starts the game
        --no-launcher      never open the launcher (a missing game folder ends the program with a message)
        --launcher-screenshot <file.png>
                           draw the launcher as it would open into a PNG file, without a window, and end
        --practice <part>  a developer's shortcut past the set-up and the menus: crypto (decode a message) or chase
        --level <n>        the practice's difficulty, 0 (Local Disturbance, the default) to 3 (Global Crisis)
        --help             this text
        """;

    public string? GameFolder { get; private set; }

    public string? SavesFolder { get; private set; }

    public int? Scale { get; private set; }

    public string? Resolution { get; private set; }

    public bool? Fullscreen { get; private set; }

    public string? Scaling { get; private set; }

    public string? SoundCard { get; private set; }

    public string? Controls { get; private set; }

    public bool? ArrowKeysWalk { get; private set; }

    public string? Mt32Roms { get; private set; }

    public string? KeyRepeat { get; private set; }

    /// <summary>--mouse (true) or --no-mouse (false): the mouse in the menus (input.mouse); null: not given.</summary>
    public bool? Mouse { get; private set; }

    public string? SettingsFile { get; private set; }

    public bool Setup { get; private set; }

    public bool NoLauncher { get; private set; }

    public bool Help { get; private set; }

    public string? LauncherScreenshot { get; private set; }

    /// <summary>"crypto", "chase", or "" (the whole game).</summary>
    public string Practice { get; private set; } = "";

    public int Level { get; private set; }

    /// <summary>What was wrong with the command line, or null.</summary>
    public string? Error { get; private set; }

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var cli = new CommandLine();
        for (var i = 0; i < args.Count && cli.Error is null; i++)
        {
            var arg = args[i];
            string? Value() => i + 1 < args.Count ? args[++i] : null;
            switch (arg)
            {
                case "--help" or "-h" or "-?" or "/?":
                    cli.Help = true;
                    break;
                case "--setup":
                    cli.Setup = true;
                    break;
                case "--no-launcher":
                    cli.NoLauncher = true;
                    break;
                case "--fullscreen":
                    cli.Fullscreen = true;
                    break;
                case "--arrow-keys-walk":
                    cli.ArrowKeysWalk = true;
                    break;
                case "--mouse":
                    cli.Mouse = true;
                    break;
                case "--no-mouse":
                    cli.Mouse = false;
                    break;
                case "--saves":
                    cli.SavesFolder = Value() ?? cli.Fail("--saves needs a folder");
                    break;
                case "--settings":
                    cli.SettingsFile = Value() ?? cli.Fail("--settings needs a file");
                    break;
                case "--mt32-roms":
                    cli.Mt32Roms = Value() ?? cli.Fail("--mt32-roms needs a folder");
                    break;
                case "--launcher-screenshot":
                    cli.LauncherScreenshot = Value() ?? cli.Fail("--launcher-screenshot needs a file");
                    break;
                case "--scale":
                    cli.Scale = int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var scale) && scale is >= 1 and <= 8
                        ? scale : cli.FailNumber("--scale needs a whole number from 1 to 8");
                    break;
                case "--level":
                    cli.Level = int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level is >= 0 and <= 3
                        ? level : cli.FailNumber("--level needs 0, 1, 2 or 3") ?? 0;
                    break;
                case "--window":
                    var window = Value();
                    cli.Resolution = global::CovertAction.Settings.SettingsFile.ParseResolution(window) is not null ? window : cli.Fail("--window needs WIDTHxHEIGHT, e.g. 1280x960");
                    break;
                case "--scaling":
                    cli.Scaling = cli.OneOf("--scaling", Value(), GameSettings.Scalings);
                    break;
                case "--sound":
                    cli.SoundCard = cli.OneOf("--sound", Value(), GameSettings.SoundCards);
                    break;
                case "--controls":
                    cli.Controls = cli.OneOf("--controls", Value(), GameSettings.ControlChoices);
                    break;
                case "--key-repeat":
                    cli.KeyRepeat = cli.OneOf("--key-repeat", Value(), GameSettings.KeyRepeats);
                    break;
                case "--practice":
                    var part = Value();
                    cli.Practice = part is "crypto" or "chase" ? part : cli.Fail("--practice needs crypto or chase") ?? "";
                    break;
                default:
                    if (!arg.StartsWith('-') && cli.GameFolder is null)
                    {
                        cli.GameFolder = arg;
                    }
                    else
                    {
                        cli.Fail($"unknown argument \"{arg}\"");
                    }
                    break;
            }
        }
        return cli;
    }

    private string? Fail(string message)
    {
        Error ??= message;
        return null;
    }

    private int? FailNumber(string message)
    {
        Error ??= message;
        return null;
    }

    private string? OneOf(string option, string? value, IReadOnlyList<string> choices)
    {
        var known = choices.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
        return known ?? Fail($"{option} needs one of {string.Join(", ", choices)}");
    }
}
