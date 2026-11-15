namespace CovertAction.Settings.Tests;

/// <summary>What a run starts with: the command line over the environment over the settings file over the defaults (SETTINGS.md "Precedence").</summary>
public sealed class PrecedenceTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void TheCommandLineWinsOverTheEnvironmentWhichWinsOverTheFile()
    {
        var file = new GameSettings { GameFolder = "from-file", Mt32Roms = "roms-file", KeyRepeat = "pc", SoundCard = "adlib", Scale = 2 };
        var env = Environment(("COVERT_GAME_DIR", "from-env"), ("COVERT_MT32_ROMS", "roms-env"), ("COVERT_KEY_REPEAT", "system"));

        var fromFile = StartSettings.Resolve(CommandLine.Parse([]), _ => null, file);
        Assert.Equal(("from-file", "roms-file", false, "adlib", 640), (fromFile.GameFolder, fromFile.Mt32Roms, fromFile.SystemKeyRepeat, fromFile.SoundCard, fromFile.Width));
        Assert.Empty(fromFile.FixedKeys);

        var fromEnv = StartSettings.Resolve(CommandLine.Parse([]), env, file);
        Assert.Equal(("from-env", "roms-env", true), (fromEnv.GameFolder, fromEnv.Mt32Roms, fromEnv.SystemKeyRepeat));
        Assert.Equal("the environment (COVERT_GAME_DIR)", fromEnv.FixedKeys["gameFolder"]);
        Assert.Equal("the environment (COVERT_KEY_REPEAT)", fromEnv.FixedKeys["input.keyRepeat"]);

        var fromCli = StartSettings.Resolve(CommandLine.Parse(["from-cli", "--mt32-roms", "roms-cli", "--key-repeat", "pc", "--sound", "mt32", "--scale", "4"]), env, file);
        Assert.Equal(("from-cli", "roms-cli", false, "mt32", 1280, 960), (fromCli.GameFolder, fromCli.Mt32Roms, fromCli.SystemKeyRepeat, fromCli.SoundCard, fromCli.Width, fromCli.Height));
        Assert.Equal("the command line (--sound)", fromCli.FixedKeys["sound.card"]);
        Assert.StartsWith("the command line", fromCli.FixedKeys["display.scale"], StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowOnTheCommandLineReplacesBothKeysOfTheFile()
    {
        var file = new GameSettings { Resolution = "1000x750" };
        Assert.Equal((1000, 750), Size(StartSettings.Resolve(CommandLine.Parse([]), _ => null, file)));
        Assert.Equal((640, 480), Size(StartSettings.Resolve(CommandLine.Parse(["--scale", "2"]), _ => null, file)));
        Assert.Equal((800, 600), Size(StartSettings.Resolve(CommandLine.Parse(["--window", "800x600"]), _ => null, new GameSettings { Scale = 5 })));
    }

    private static (int, int) Size(StartSettings s) => (s.Width, s.Height);

    /// <summary>The controls (the set-up's controls answer): keyboard and joystick by default, the file's, the command line's over it.</summary>
    [Fact]
    public void TheControlsAreTheJoystickUnlessTheFileOrTheCommandLineSaysKeyboard()
    {
        var byDefault = StartSettings.Resolve(CommandLine.Parse([]), _ => null, new GameSettings());
        Assert.Equal(("joystick", true), (byDefault.Controls, byDefault.Joystick));
        var fromFile = StartSettings.Resolve(CommandLine.Parse([]), _ => null, new GameSettings { Controls = "keyboard" });
        Assert.Equal(("keyboard", false), (fromFile.Controls, fromFile.Joystick));
        var fromCli = StartSettings.Resolve(CommandLine.Parse(["--controls", "joystick"]), _ => null, new GameSettings { Controls = "keyboard" });
        Assert.Equal(("joystick", true), (fromCli.Controls, fromCli.Joystick));
        Assert.Equal("the command line (--controls)", fromCli.FixedKeys["input.controls"]);
    }

    [Fact]
    public void TheSoundCardsAreTheSetUpsLetters()
    {
        Assert.Equal("adlib", StartSettings.Resolve(CommandLine.Parse([]), _ => null, new GameSettings()).SoundCard);
        Assert.Equal('A', GameSettings.SoundLetter(null));
        Assert.Equal(['N', 'I', 'T', 'A', 'R'], new[] { "none", "speaker", "tandy", "adlib", "mt32" }.Select(c => GameSettings.SoundLetter(c)));
    }

    [Theory]
    [InlineData("--scale", "9")]
    [InlineData("--sound", "gus")]
    [InlineData("--sound", "setup")]
    [InlineData("--controls", "mouse")]
    [InlineData("--skip-setup", "")]
    [InlineData("--window", "big")]
    [InlineData("--practice", "raid")]
    [InlineData("--frobnicate", "")]
    public void ABadCommandLineIsRefused(string option, string value)
    {
        var cli = CommandLine.Parse(value.Length > 0 ? [option, value] : [option]);
        Assert.NotNull(cli.Error);
    }

    [Fact]
    public void TheCommandLineReadsEveryOption()
    {
        var cli = CommandLine.Parse(["game", "--saves", "s", "--fullscreen", "--scaling", "smooth", "--controls", "keyboard", "--settings", "x.json", "--setup", "--no-launcher", "--practice", "chase", "--level", "2", "--launcher-screenshot", "l.png"]);
        Assert.Null(cli.Error);
        Assert.Equal(("game", "s", true, "smooth", "keyboard", "x.json", true, true, "chase", 2, "l.png"),
            (cli.GameFolder, cli.SavesFolder, cli.Fullscreen, cli.Scaling, cli.Controls, cli.SettingsFile, cli.Setup, cli.NoLauncher, cli.Practice, cli.Level, cli.LauncherScreenshot));
        Assert.True(CommandLine.Parse(["--help"]).Help);
    }
}
