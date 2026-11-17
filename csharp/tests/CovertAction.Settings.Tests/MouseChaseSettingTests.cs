namespace CovertAction.Settings.Tests;

/// <summary>The player option "mouse in the car chase" (divergence D-670): off by default, read from the file or the command line.</summary>
public sealed class MouseChaseSettingTests
{
    [Fact]
    public void TheOptionIsOffByDefaultAndReadFromTheFileOrTheCommandLine()
    {
        Assert.False(StartSettings.Resolve(CommandLine.Parse([]), _ => null, new GameSettings()).MouseChase);
        var template = SettingsFile.Parse(SettingsFile.Template);
        Assert.Empty(template.Problems);
        Assert.False(StartSettings.Resolve(CommandLine.Parse([]), _ => null, template.Settings).MouseChase);

        var file = SettingsFile.Parse("{ \"input\": { \"mouseChase\": true } }");
        Assert.Empty(file.Problems);
        var fromFile = StartSettings.Resolve(CommandLine.Parse([]), _ => null, file.Settings);
        Assert.True(fromFile.MouseChase);
        Assert.False(fromFile.Mouse);
        Assert.NotEmpty(SettingsFile.Parse("{ \"input\": { \"mouseChase\": \"yes\" } }").Problems);

        var cli = StartSettings.Resolve(CommandLine.Parse(["--mouse-chase"]), _ => null, new GameSettings());
        Assert.True(cli.MouseChase);
        Assert.Equal("the command line (--mouse-chase)", cli.FixedKeys["input.mouseChase"]);
        Assert.False(StartSettings.Resolve(CommandLine.Parse(["--no-mouse-chase"]), _ => null, file.Settings).MouseChase);
    }
}
