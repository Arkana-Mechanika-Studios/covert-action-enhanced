namespace CovertAction.Platform;

/// <summary>
/// The PC's analog joystick at one moment, as the game port shows it to the input driver (MISC.EXE). The port has a
/// timer for each axis of the stick: started by the driver, it stays on for a time that grows with the stick's
/// position (the resistance of its potentiometer), and the driver measures it by counting how often its timing loop
/// finds the timer still on. The buttons are bits of the same port, clear while held down.
/// </summary>
/// <remarks>
/// A host gives these raw readings; the input driver (<c>CovertAction.Drivers.KeyboardDriver</c>) turns them into the
/// game's signed axis bytes with the original's own calibration and scaling, so a controller of today goes through the
/// same arithmetic as a joystick of 1990.
/// </remarks>
/// <param name="Buttons">
/// The port's eight bits: bits 4 and 5 the first joystick's two buttons, 6 and 7 a second joystick's (or the third and
/// fourth button of a four-button stick), each clear while held down. Bits 0 and 1 are the first joystick's axis
/// timers, 2 and 3 the second's, set while on. 0xFF without a joystick.
/// </param>
/// <param name="XCount">
/// How many turns of the driver's timing loop find the X timer (left to right) still on after the driver starts it:
/// small with the stick to the left. <see cref="NeverEnds"/> when no stick is there (the timer never ends).
/// </param>
/// <param name="YCount">The same for the Y timer (top to bottom): small with the stick pushed up.</param>
public readonly record struct JoystickReading(int Buttons, int XCount, int YCount)
{
    /// <summary>The count of a timer that never ends: an axis with no stick on it.</summary>
    public const int NeverEnds = int.MaxValue;

    /// <summary>The port without a joystick: every bit set, and timers that never end.</summary>
    public const int NoJoystickBits = 0xFF;

    /// <summary>The PC without a joystick.</summary>
    public static JoystickReading None => new(NoJoystickBits, NeverEnds, NeverEnds);

    // The model of a PC joystick and the port's timer, from the IBM game control adapter's description: the timer stays
    // on 24.2 microseconds plus 0.011 microseconds per ohm, and the stick's potentiometer goes from 0 to 100 kilohms.
    private const double TimerBaseMicroseconds = 24.2, MicrosecondsPerOhm = 0.011, FullResistanceOhms = 100_000;

    // One turn of the driver's timing loop (MISC 0000:00d5..00e4: in, and, je, two shifts and adds, loop) takes 45 clock
    // cycles of the 8086 (DECISIONS.md: the processor is the 8086), at the PC's 4.77 MHz.
    private const double LoopTurnCycles = 45, ClockMegahertz = 14.31818 / 3;

    // the first joystick's timers have ended when the driver looks (bits 0, 1 clear); a second one is not there (2, 3 set)
    private const int FirstJoystickPresentBits = 0x0c;

    private const int ButtonBitsShift = 4, ButtonMask = 0x0f;

    /// <summary>
    /// What the port gives for a stick at a position with buttons held: the counts a real analog joystick gives the
    /// driver on the PC the rewrite takes as the original's (an 8086 at 4.77 MHz): 3 at one end, 61 in the middle, 119 at
    /// the other.
    /// </summary>
    /// <param name="x">The stick's position from left (-1) to right (1); 0 in the middle.</param>
    /// <param name="y">The stick's position from up (-1) to down (1); 0 in the middle.</param>
    /// <param name="buttonsHeld">The buttons held down: bit 0 the first, up to bit 3 the fourth.</param>
    // UNCERTAIN: U-12 the counts a real joystick gives: the timer's formula of the adapter's description and the loop's cycles on an 8086 at 4.77 MHz.
    public static JoystickReading FromStick(double x, double y, int buttonsHeld) =>
        new(FirstJoystickPresentBits | (~buttonsHeld & ButtonMask) << ButtonBitsShift, Count(x), Count(y));

    /// <summary>The loop's turns for a position from -1 to 1 along an axis.</summary>
    private static int Count(double position)
    {
        var ohms = (Math.Clamp(position, -1, 1) + 1) / 2 * FullResistanceOhms;
        var microseconds = TimerBaseMicroseconds + MicrosecondsPerOhm * ohms;
        var turnMicroseconds = LoopTurnCycles / ClockMegahertz;
        return (int)Math.Round(microseconds / turnMicroseconds);
    }
}
