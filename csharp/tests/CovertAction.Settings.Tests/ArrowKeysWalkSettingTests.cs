using CovertAction.Host.Sdl;
using CovertAction.Platform;

namespace CovertAction.Settings.Tests;

/// <summary>The player option "arrow keys walk in a raid" (divergence D-610): its setting, and the marks the SDL host puts on the grey cursor keys.</summary>
public sealed class ArrowKeysWalkSettingTests
{
    private const int ScancodeUp = 82, ScancodeKeypad8 = 96, ModLeftShift = 0x0001;

    [Fact]
    public void TheOptionIsOffByDefaultAndReadFromTheFileOrTheCommandLine()
    {
        Assert.False(StartSettings.Resolve(CommandLine.Parse([]), _ => null, new GameSettings()).ArrowKeysWalk);
        Assert.False(StartSettings.Resolve(CommandLine.Parse([]), _ => null, SettingsFile.Parse(SettingsFile.Template).Settings).ArrowKeysWalk);

        var file = SettingsFile.Parse("{ \"input\": { \"arrowKeysWalk\": true } }");
        Assert.Empty(file.Problems);
        Assert.True(StartSettings.Resolve(CommandLine.Parse([]), _ => null, file.Settings).ArrowKeysWalk);

        var cli = StartSettings.Resolve(CommandLine.Parse(["--arrow-keys-walk"]), _ => null, new GameSettings());
        Assert.True(cli.ArrowKeysWalk);
        Assert.Equal("the command line (--arrow-keys-walk)", cli.FixedKeys["input.arrowKeysWalk"]);
    }

    [Fact]
    public void TheHostMarksTheGreyArrowsAndLeavesTheKeypadAlone()
    {
        Assert.Equal(HostKeys.Up | HostKeys.GreyArrow, SdlKeyMap.ToGameKey(ScancodeUp, 0, 0));
        Assert.Equal(HostKeys.Up | HostKeys.GreyArrow | HostKeys.ShiftHeld, SdlKeyMap.ToGameKey(ScancodeUp, 0, ModLeftShift));
        Assert.Equal(HostKeys.Up | HostKeys.Keypad, SdlKeyMap.ToGameKey(ScancodeKeypad8, 0, ModLeftShift));
    }
}
