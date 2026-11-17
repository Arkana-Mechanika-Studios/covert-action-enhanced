using System.Text.Json.Nodes;

namespace CovertAction.Settings.Tests;

/// <summary>The settings file (csharp/docs/SETTINGS.md): how it is read, where it is looked for, and that the launcher's edits keep it as it was.</summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "covert-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheTemplateReadsAsTheDefaults()
    {
        var load = SettingsFile.Parse(SettingsFile.Template);
        Assert.Empty(load.Problems);
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, load.Settings);
        Assert.Equal("adlib", start.SoundCard);
        Assert.Equal("joystick", start.Controls);
        Assert.True(start.Joystick);
        Assert.Equal("pixelart", start.Scaling);
        Assert.Equal((960, 720), (start.Width, start.Height));
        Assert.False(start.Fullscreen);
        Assert.False(start.SystemKeyRepeat);
        Assert.Null(start.GameFolder);
        Assert.Null(start.SavesFolder);
        Assert.Null(start.Mt32Roms);
        Assert.Empty(start.FixedKeys);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowedAndUnknownKeysOrBadValuesAreReportedAndIgnored()
    {
        var load = SettingsFile.Parse("""
            // a comment
            {
              "display": { "scale": 4, /* inline */ "scaling": "SMOOTH", "fullscreen": "yes", },
              "sound": { "card": "gus" },
              "colour": "green",
            }
            """);
        Assert.Equal(4, load.Settings.Scale);
        Assert.Equal("smooth", load.Settings.Scaling);
        Assert.Null(load.Settings.Fullscreen);
        Assert.Null(load.Settings.SoundCard);
        Assert.Equal(3, load.Problems.Count);
        Assert.Contains(load.Problems, p => p.Contains("\"colour\"", StringComparison.Ordinal));
        Assert.Contains(load.Problems, p => p.Contains("sound.card", StringComparison.Ordinal));
        Assert.Contains(load.Problems, p => p.Contains("display.fullscreen", StringComparison.Ordinal));
    }

    [Fact]
    public void ABrokenFileGivesTheDefaultsAndSaysWhy()
    {
        var load = SettingsFile.Parse("{ \"display\": ");
        Assert.Single(load.Problems);
        Assert.Null(load.Settings.Scale);
    }

    [Fact]
    public void RelativeFoldersAreTakenFromTheFilesFolderAndAResolutionReplacesTheScale()
    {
        var load = SettingsFile.Parse("""{ "gameFolder": "game", "display": { "scale": 2, "resolution": "1000x750" } }""", _folder);
        Assert.Equal(Path.Combine(_folder, "game"), load.Settings.GameFolder);
        Assert.Null(load.Settings.Scale);
        Assert.Equal((1000, 750), SettingsFile.ParseResolution(load.Settings.Resolution));
    }

    [SkippableFact]
    public void TheLaunchersEditsKeepTheCommentsAndReadBackAsSaved()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows paths and a Windows registry value: nothing here runs on Linux");
        var path = Path.Combine(_folder, SettingsFile.FileName);
        Assert.True(SettingsFile.TryWriteTemplate(path, out _));
        Assert.False(SettingsFile.TryWriteTemplate(path, out _), "an existing file is never overwritten");
        var changes = new Dictionary<string, JsonNode?>
        {
            ["gameFolder"] = "C:/Games/Covert Action/Covert",
            ["display.scale"] = 4,
            ["display.scaling"] = "nearest",
            ["sound.card"] = "mt32",
            ["input.controls"] = "keyboard",
            ["input.keyRepeat"] = "windows",
        };
        Assert.True(SettingsFileEdits.TrySet(path, changes, out var message), message);
        var text = File.ReadAllText(path);
        Assert.Contains("// Precedence: command line > environment variables > this file > defaults.", text);
        var load = SettingsFile.Load(path);
        Assert.Empty(load.Problems);
        var s = load.Settings;
        Assert.Equal(("C:/Games/Covert Action/Covert", 4, "nearest", "mt32", "keyboard", "windows"), (s.GameFolder, s.Scale, s.Scaling, s.SoundCard, s.Controls, s.KeyRepeat));
    }

    /// <summary>
    /// A file of an earlier version, whose set-up screen asked for the card ("setup") and could be skipped: it loads
    /// without a problem, the card is the default AdLib and the skip is ignored (the set-up screen asks nothing now).
    /// </summary>
    [Fact]
    public void AnEarlierFilesSetUpCardAndSkipLoadAsTheDefaults()
    {
        var load = SettingsFile.Parse("""{ "sound": { "card": "setup" }, "startup": { "skipSetup": true } }""");
        Assert.Empty(load.Problems);
        Assert.Null(load.Settings.SoundCard);
        var start = StartSettings.Resolve(CommandLine.Parse([]), _ => null, load.Settings);
        Assert.Equal(("adlib", "joystick"), (start.SoundCard, start.Controls));
    }

    [Fact]
    public void AKeyTheFileLacksIsAddedToItsSection()
    {
        var edited = SettingsFileEdits.Set("{\n  // mine\n  \"display\": { \"scale\": 2 }\n}", [new("display.fullscreen", true), new("sound.card", "adlib")]);
        var s = SettingsFile.Parse(edited).Settings;
        Assert.Equal((2, true, "adlib"), (s.Scale, s.Fullscreen, s.SoundCard));
        Assert.Contains("// mine", edited);
    }

    [Fact]
    public void TheFileIsLookedForGivenThenBesideTheProgramThenInTheAccountsFolder()
    {
        var program = Path.Combine(_folder, "program");
        var appData = Path.Combine(_folder, "appdata");
        Directory.CreateDirectory(program);
        Directory.CreateDirectory(Path.Combine(appData, SettingsFile.AppDataFolder));
        Assert.Null(SettingsFile.Locate(null, program, appData));
        var inAccount = Path.Combine(appData, SettingsFile.AppDataFolder, SettingsFile.FileName);
        File.WriteAllText(inAccount, "{}");
        Assert.Equal(inAccount, SettingsFile.Locate(null, program, appData));
        var beside = Path.Combine(program, SettingsFile.FileName);
        File.WriteAllText(beside, "{}");
        Assert.Equal(beside, SettingsFile.Locate(null, program, appData));
        Assert.Equal("given.json", SettingsFile.Locate("given.json", program, appData));
        // a new file goes beside the program when that folder can be written
        Assert.Equal(beside, SettingsFile.WritablePath(program, appData));
        Assert.Equal(inAccount, SettingsFile.WritablePath(Path.Combine(_folder, "no such folder"), appData));
    }
}
