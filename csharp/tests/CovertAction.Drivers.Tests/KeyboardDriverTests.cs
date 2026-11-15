using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Msc;
using CovertAction.Platform;

namespace CovertAction.Drivers.Tests;

/// <summary>The game's keyboard, joystick and timer on the headless host: keys, the wait for a key, the stick's scaling, frames.</summary>
public sealed class KeyboardDriverTests
{
    private static (KeyboardDriver Keyboard, HostLink Link, HeadlessHost Host) Setup(params int[] keys)
    {
        var host = new HeadlessHost(keys, unixSeconds: 1000);
        var graphics = new McgaGraphicsDriver(new FontSet(McgaGraphicsDriverTests.FontFile), new byte[3 * McgaDriverFile.Colours]);
        var link = new HostLink(graphics, host);
        return (new KeyboardDriver(link), link, host);
    }

    [Fact]
    public void AKeyWaitsUntilItIsRead()
    {
        var (keyboard, _, _) = Setup(HostKeys.Enter, HostKeys.F1);
        Assert.False(keyboard.NoKeyWaiting());
        Assert.True(keyboard.Kbhit());
        Assert.True(keyboard.Kbhit());                      // asking does not take the key
        Assert.Equal(HostKeys.Enter, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.F1, keyboard.ReadKeyRaw());
    }

    [Fact]
    public void GetchGivesACharacterAtOnceAndAFunctionKeyInTwoHalves()
    {
        // Enter (scan code 1Ch, character 0Dh), then F1 (scan code 3Bh, no character), then the letter a
        var (keyboard, _, _) = Setup(0x1c0d, 0x3b00, 0x1e61);
        Assert.Equal(0x0d, keyboard.Getch());
        Assert.Equal(0, keyboard.Getch());
        Assert.Equal(0x3b, keyboard.Getch());
        Assert.Equal(0x61, keyboard.Getch());
        Assert.True(keyboard.NoKeyWaiting());
    }

    [Fact]
    public void AKeypadKeyGivesItsDigitWhileNumLockIsOnAndItsCursorKeyWhenItIsOff()
    {
        // keypad 1 and keypad 8, twice: Num Lock is on at start (as on the PCs of the time), then the launcher clears it
        var (keyboard, _, _) = Setup(HostKeys.End | HostKeys.Keypad, HostKeys.Up | HostKeys.Keypad,
            HostKeys.End | HostKeys.Keypad, HostKeys.Up | HostKeys.Keypad);
        Assert.Equal(0x4f31, keyboard.ReadKeyRaw());
        Assert.Equal(0x4838, keyboard.ReadKeyRaw());
        keyboard.SetNumLock(false);
        Assert.Equal(HostKeys.End, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.Up, keyboard.ReadKeyRaw());
    }

    // the grey arrows as the SDL host gives them: the cursor code with the GreyArrow mark (and ShiftHeld with Shift)
    private const int GreyUp = HostKeys.Up | HostKeys.GreyArrow, GreyEnd = HostKeys.End | HostKeys.GreyArrow;

    [Fact]
    public void WithoutTheOptionAGreyArrowGivesItsCursorCodeWhateverNumLockSaysAndShiftChangesNothing()
    {
        var (keyboard, _, _) = Setup(GreyUp, GreyUp | HostKeys.ShiftHeld, GreyEnd, GreyUp, HostKeys.Up | HostKeys.Keypad);
        Assert.False(keyboard.ArrowKeysWalk);
        Assert.Equal(HostKeys.Up, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.Up, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.End, keyboard.ReadKeyRaw());
        keyboard.SetNumLock(false);
        Assert.Equal(HostKeys.Up, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.Up, keyboard.ReadKeyRaw());       // the keypad with Num Lock off
    }

    [Fact]
    public void WithTheOptionAGreyArrowWalksLikeTheKeypadAndShiftWithItLunges()
    {
        // Num Lock on, as in the raid: up and End give the keypad's 8 and 1; with Shift the cursor codes, which lunge
        var (keyboard, _, _) = Setup(GreyUp, GreyEnd, GreyUp | HostKeys.ShiftHeld, GreyEnd | HostKeys.ShiftHeld, HostKeys.Up | HostKeys.Keypad);
        keyboard.ArrowKeysWalk = true;
        Assert.Equal(0x4838, keyboard.ReadKeyRaw());
        Assert.Equal(0x4f31, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.Up, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.End, keyboard.ReadKeyRaw());
        Assert.Equal(0x4838, keyboard.ReadKeyRaw());            // the keypad is as before
    }

    [Fact]
    public void KeysWithoutMarksAreUntouchedByTheOption()
    {
        var (keyboard, _, _) = Setup(HostKeys.Up, HostKeys.Enter, HostKeys.F1);
        keyboard.ArrowKeysWalk = true;
        Assert.Equal(HostKeys.Up, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.Enter, keyboard.ReadKeyRaw());
        Assert.Equal(HostKeys.F1, keyboard.ReadKeyRaw());
    }

    [Fact]
    public void WithoutAKeyTheBufferIsEmpty()
    {
        var (keyboard, _, _) = Setup();
        Assert.True(keyboard.NoKeyWaiting());
    }

    [Fact]
    public void WaitingForAKeyShowsTheScreenAndEndsTheGameWhenTheHostQuits()
    {
        // the headless host asks to quit when its script has no key left
        var (keyboard, _, host) = Setup();
        Assert.Throws<ProgramExitException>(() => keyboard.ReadKeyRaw());
        Assert.True(host.Frames > 0);
    }

    [Fact]
    public void WithoutAJoystickTheStickReadsAsCentredOnceCalibrated()
    {
        var (keyboard, _, _) = Setup();
        keyboard.CalibrateJoystick();
        Assert.Equal(0, keyboard.ReadJoystickAxes());
        Assert.Equal(0xff, keyboard.ReadJoystickButtons());     // every button bit set: none pressed
    }

    [Fact]
    public void BeforeAnyCalibrationTheStickReadsAsPushedToTheLowerRight()
    {
        // the driver's centre is 0 as loaded: any count is above it and a new highest, 127 on both axes
        var (keyboard, _, _) = Setup();
        Assert.Equal(0x7f7f, keyboard.ReadJoystickAxes());
        var (withStick, _, host) = Setup();
        host.Joystick = new JoystickReading(0xfc, 61, 61);
        Assert.Equal(0x7f7f, withStick.ReadJoystickAxes());
        // 30 is above the centre 0 and below the highest, 61: 30/61 of the way, the fraction 32230, halved, its high byte 62
        host.Joystick = new JoystickReading(0xfc, 30, 61);
        Assert.Equal(0x7f3e, withStick.ReadJoystickAxes());
    }

    /// <summary>The calibrated stick swept along X; the values worked out from MISC 0000:00f1.</summary>
    [Theory]
    [InlineData(61, 0x00)]      // at the centre
    [InlineData(3, 0x81)]       // the far left, the new lowest: -127
    [InlineData(4, 0x82)]       // 57/58 of the way: the fraction 64406, halved 32203, negated, its high byte -126
    [InlineData(30, 0xbb)]      // 31/58: -69 (rounded down)
    [InlineData(60, 0xfd)]      // 1/58: -3, not -2: the low side rounds away from the centre
    [InlineData(119, 0x7f)]     // the far right, the new highest: 127
    [InlineData(118, 0x7d)]     // 57/58: 125
    [InlineData(62, 0x02)]      // 1/58: 2
    public void TheStickIsScaledBetweenTheCentreAndTheFarthestPositionSeen(int x, int expected)
    {
        var (keyboard, _, host) = Setup();
        host.Joystick = new JoystickReading(0xfc, 61, 61);
        keyboard.CalibrateJoystick();
        // the extremes are learnt first, as a player sweeping the stick teaches them
        host.Joystick = new JoystickReading(0xfc, 3, 61);
        keyboard.ReadJoystickAxes();
        host.Joystick = new JoystickReading(0xfc, 119, 61);
        keyboard.ReadJoystickAxes();
        host.Joystick = new JoystickReading(0xfc, x, 61);
        Assert.Equal(expected, keyboard.ReadJoystickAxes());   // Y at the centre: 0 in the high byte
    }

    [Fact]
    public void YIsTheHighByteAndTheFirstPushIsAlwaysAnExtreme()
    {
        var (keyboard, _, host) = Setup();
        host.Joystick = new JoystickReading(0xfc, 61, 61);
        keyboard.CalibrateJoystick();
        // a count one below the centre is the lowest seen so far: up at once, -127
        host.Joystick = new JoystickReading(0xfc, 61, 60);
        Assert.Equal(0x8100, keyboard.ReadJoystickAxes());
        host.Joystick = new JoystickReading(0xfc, 62, 3);
        Assert.Equal(0x817f, keyboard.ReadJoystickAxes());
        // 60 is now 1/58 of the way up: -3
        host.Joystick = new JoystickReading(0xfc, 61, 60);
        Assert.Equal(0xfd00, keyboard.ReadJoystickAxes());
    }

    [Fact]
    public void JustShortOfALongLowEndTheAnswerIsMinus128()
    {
        // a span of 256 turns (a fast PC's counts): 255 of the 256 make the fraction 65280, halved 32640, negated -32640,
        // whose high byte is -128, where the end itself gives -127
        var (keyboard, _, host) = Setup();
        host.Joystick = new JoystickReading(0xfc, 1000, 1000);
        keyboard.CalibrateJoystick();
        host.Joystick = new JoystickReading(0xfc, 744, 1000);
        Assert.Equal(0x0081, keyboard.ReadJoystickAxes());
        host.Joystick = new JoystickReading(0xfc, 745, 1000);
        Assert.Equal(0x0080, keyboard.ReadJoystickAxes());
    }

    [Fact]
    public void ATimerThatNeverEndsStopsAtTheLoopsEnd()
    {
        // X has no stick: it counts 65535, as does a count beyond the loop's end; Y is calibrated at 61
        var (keyboard, _, host) = Setup();
        host.Joystick = new JoystickReading(0xf8, JoystickReading.NeverEnds, 61);
        keyboard.CalibrateJoystick();
        host.Joystick = new JoystickReading(0xf8, 70000, 61);
        Assert.Equal(0, keyboard.ReadJoystickAxes());
        host.Joystick = new JoystickReading(0xf8, 65534, 61);
        Assert.Equal(0x0081, keyboard.ReadJoystickAxes());
    }

    [Fact]
    public void ACalibrationKeepsTheOldSpansButStartsTheExtremesAgain()
    {
        var (keyboard, _, host) = Setup();
        host.Joystick = new JoystickReading(0xfc, 61, 61);
        keyboard.CalibrateJoystick();
        host.Joystick = new JoystickReading(0xfc, 3, 119);
        Assert.Equal(0x7f81, keyboard.ReadJoystickAxes());
        host.Joystick = new JoystickReading(0xfc, 70, 50);
        keyboard.CalibrateJoystick();
        // the old centre is now below X's new centre and above Y's: both new extremes
        host.Joystick = new JoystickReading(0xfc, 61, 61);
        Assert.Equal(0x7f81, keyboard.ReadJoystickAxes());
    }

    [Fact]
    public void TheButtonsAreThePortsBits()
    {
        var (keyboard, _, host) = Setup();
        host.Joystick = JoystickReading.FromStick(0, 0, buttonsHeld: 0b01);
        Assert.Equal(0xec, keyboard.ReadJoystickButtons());     // bit 4 clear: the first button; bits 2, 3: no second stick
        host.Joystick = JoystickReading.FromStick(0, 0, buttonsHeld: 0b1010);
        Assert.Equal(0x5c, keyboard.ReadJoystickButtons());     // the second and the fourth: bits 5 and 7 clear
    }

    [Theory]
    [InlineData(-1, 3)]         // 0 ohms: 24.2 microseconds, 2.57 turns of 9.43 microseconds
    [InlineData(0, 61)]         // 50 kilohms: 574.2 microseconds
    [InlineData(1, 119)]        // 100 kilohms: 1124.2 microseconds
    [InlineData(-0.5, 32)]      // 25 kilohms: 299.2 microseconds, 31.7 turns
    [InlineData(-3, 3)]         // beyond the end: the end
    public void AStickPositionGivesTheCountsOfARealJoystick(double position, int expected)
    {
        var reading = JoystickReading.FromStick(position, -position, 0);
        Assert.Equal(expected, reading.XCount);
        Assert.Equal(0xfc, reading.Buttons);
        Assert.Equal(expected, JoystickReading.FromStick(0, position, 0).YCount);
    }

    [Fact]
    public void AControllerSweptAndCentredGivesTheGameFullAndZeroDeflection()
    {
        var (keyboard, _, host) = Setup();
        host.Joystick = JoystickReading.FromStick(0, 0, 0);
        keyboard.CalibrateJoystick();
        host.Joystick = JoystickReading.FromStick(-1, 1, 0);
        Assert.Equal(0x7f81, keyboard.ReadJoystickAxes());      // left and down
        host.Joystick = JoystickReading.FromStick(1, -1, 0);
        Assert.Equal(0x817f, keyboard.ReadJoystickAxes());      // right and up
        host.Joystick = JoystickReading.FromStick(0, 0, 0);
        Assert.Equal(0, keyboard.ReadJoystickAxes());
        host.Joystick = JoystickReading.FromStick(-0.9, 0, 0);  // past the game's threshold (0x60 of 127)
        Assert.True((sbyte)keyboard.ReadJoystickAxes() < -0x60);
    }

    [Fact]
    public void AFrameWaitShowsTheScreenAndKeepsTheDisplaysPace()
    {
        var (_, link, host) = Setup(HostKeys.Enter);
        var timer = new GameTimer(link);
        for (var frame = 0; frame < 70; frame++)
        {
            timer.WaitForFrame();
        }
        Assert.Equal(70, host.Frames);
        // 70 frames of a 70 Hz display take a second (the host's clock counts whole milliseconds)
        Assert.InRange(host.Milliseconds, 930, 1000);
        Assert.Equal(1000, timer.Time());
    }
}
