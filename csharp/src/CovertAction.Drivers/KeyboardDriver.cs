using CovertAction.Platform;

namespace CovertAction.Drivers;

/// <summary>
/// The game's keyboard and joystick: what the original's small input driver (MISC.EXE) does for the game, with the
/// host's key presses and the host's game port. A key comes from the host as the game reads keys: the key's number in
/// the high byte and its character in the low byte (<see cref="HostKeys"/>). The joystick comes as the port shows it
/// (<see cref="JoystickReading"/>); the driver's own calibration and scaling make the game's signed axis bytes of it.
/// Without a joystick it reads as centred once calibrated, with no button pressed.
/// </summary>
/// <param name="link">The host: where the keys and the joystick come from, and what is shown while the game waits for a key.</param>
public sealed class KeyboardDriver(HostLink link) : IInputDriver
{
    /// <summary>The key the host has given that the game has not taken yet.</summary>
    private int? _waiting;

    /// <inheritdoc/>
    // orig: MISC 0000:004c no_key_waiting driver; tests: MiscOracleTests, KeyboardDriverTests
    public bool NoKeyWaiting() => !KeyWaiting();

    /// <inheritdoc/>
    /// <remarks>While the game waits here the screen is shown and the host stays responsive.</remarks>
    // orig: MISC 0000:0047 read_key_raw driver; tests: MiscOracleTests, KeyboardDriverTests
    public int ReadKeyRaw()
    {
        while (!KeyWaiting())
        {
            // KeyWaiting has shown the screen if the game changed it or a frame of the display has passed: showing it on
            // every turn handed the window a frame each millisecond (csharp/docs/PERFORMANCE.md)
            link.Host.Clock.Sleep(1);
        }
        var key = _waiting!.Value;
        _waiting = null;
        return key;
    }

    /// <inheritdoc/>
    public bool Kbhit() => KeyWaiting();

    /// <summary>The scan code of a function or cursor key whose first half (0) <see cref="Getch"/> has given.</summary>
    private int? _scanCodeToCome;

    /// <inheritdoc/>
    /// <remarks>New code over the host's keys, which come as the BIOS gives them (scan code and character): DOS hands the same key on in the two halves described.</remarks>
    public int Getch()
    {
        if (_scanCodeToCome is { } scanCode)
        {
            _scanCodeToCome = null;
            return scanCode;
        }
        var key = ReadKeyRaw();
        var character = key & 0xff;
        if (character == 0)
        {
            _scanCodeToCome = key >> 8 & 0xff;
        }
        return character;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Measures the stick and takes the position as each axis's centre, and as its lowest and highest position so far:
    /// the next reading on either side of the centre is then that side's new extreme. Without a joystick both timers
    /// run to the loop's end and the centre is that count, which every later reading equals: the stick reads as centred.
    /// </remarks>
    // orig: MISC 0000:0080 calibrate_joystick driver; tests: MiscJoystickOracleTests, KeyboardDriverTests
    public void CalibrateJoystick()
    {
        var (x, y) = MeasureJoystick();
        SetAxisCentre(_x, x);
        SetAxisCentre(_y, y);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each axis is scaled between the centre and the farthest position seen on that side since the calibration: a
    /// position at or beyond it is -127 or 127 (and becomes the new farthest), one between in proportion. Before any
    /// calibration the centre is 0, so every reading counts as the far right and the far bottom (0x7f7f).
    /// </remarks>
    // orig: MISC 0000:00aa read_joystick_axes driver; tests: MiscJoystickOracleTests, KeyboardDriverTests
    public int ReadJoystickAxes()
    {
        var (x, y) = MeasureJoystick();
        // the driver keeps the two answers as the bytes of one word: X low, Y high
        const int byteMask = 0xff;
        return ScaleAxis(_x, x) & byteMask | (ScaleAxis(_y, y) & byteMask) << 8;
    }

    /// <inheritdoc/>
    /// <remarks>The game reads the port itself (<c>inp(0x201)</c>); the host gives what it reads.</remarks>
    public int ReadJoystickButtons() => link.Host.Input.ReadJoystick().Buttons;

    /// <summary>The most turns the driver's timing loop makes: its counter is a 16-bit word started at 0xFFFF.</summary>
    private const int LoopTurns = 0xffff;

    /// <summary>The answer for a position at or beyond the farthest seen on a side: -127 or 127.</summary>
    private const int FullDeflection = 0x7f;

    /// <summary>
    /// What the driver keeps of one axis of the stick between calls, in turns of its timing loop (0..65535; the
    /// driver compares them as unsigned words). Each is the driver's own data word, X then Y one word apart.
    /// </summary>
    private sealed class Axis
    {
        /// <summary>The position at the last calibration (MISC DS:70, DS:72).</summary>
        public ushort Centre;

        /// <summary>The lowest position seen since the calibration (MISC DS:60, DS:62): the far left or the top.</summary>
        public ushort Lowest;

        /// <summary>The highest position seen since the calibration (MISC DS:68, DS:6a): the far right or the bottom.</summary>
        public ushort Highest;

        /// <summary>How far <see cref="Lowest"/> was below the centre when it was seen (MISC DS:50, DS:52); not reset by a calibration.</summary>
        public ushort SpanBelow;

        /// <summary>How far <see cref="Highest"/> was above the centre when it was seen (MISC DS:58, DS:5a); not reset by a calibration.</summary>
        public ushort SpanAbove;
    }

    // the driver's data starts as the file has it, all 0: the stick is centred at 0 until the game calibrates it
    private readonly Axis _x = new(), _y = new();

    /// <summary>
    /// Measures the stick: starts the game port's two timers and counts the turns of a loop in which each timer is
    /// still on, until both have ended or the loop has turned 65535 times (a timer with no stick never ends).
    /// </summary>
    /// <returns>The counts of the X and the Y timer, 0..65535.</returns>
    // orig: MISC 0000:00c4 measure_joystick driver; tests: MiscJoystickOracleTests, KeyboardDriverTests
    private (ushort X, ushort Y) MeasureJoystick()
    {
        // the original counts with interrupts off, reading the port in every turn; the host's reading has the counts
        var reading = link.Host.Input.ReadJoystick();
        return (Turns(reading.XCount), Turns(reading.YCount));

        static ushort Turns(int count) => (ushort)Math.Clamp(count, 0, LoopTurns);
    }

    /// <summary>Calibrates one axis: the measured position becomes its centre, its lowest and its highest.</summary>
    // orig: MISC 0000:0097 set_axis_centre driver; tests: MiscJoystickOracleTests
    private static void SetAxisCentre(Axis axis, ushort position)
    {
        axis.Centre = position;
        axis.Lowest = position;
        axis.Highest = position;
    }

    /// <summary>
    /// Scales one axis's measured position against its centre and the farthest position seen on that side.
    /// </summary>
    /// <returns>-127..127 with -128 possible just short of the low end (see below); 0 exactly at the centre.</returns>
    // orig: MISC 0000:00f1 scale_axis driver; tests: MiscJoystickOracleTests, KeyboardDriverTests
    private static int ScaleAxis(Axis axis, ushort position)
    {
        if (position == axis.Centre)
        {
            return 0;
        }
        if (position < axis.Centre)
        {
            var below = axis.Centre - position;
            if (position <= axis.Lowest)
            {
                axis.Lowest = position;
                axis.SpanBelow = (ushort)below;
                return -FullDeflection;
            }
            // the driver makes the fraction below/span a 16-bit one (the distance times 65536, divided by the span),
            // halves it, negates it and keeps the high byte: the result rounds down, towards the low end, and can reach
            // -128 just short of the extreme where the extreme itself gives -127.
            // The division never overflows and never divides by 0: a position above the lowest one and below the
            // centre means the lowest was set here since the calibration, with a span larger than this distance.
            return -(int)(Fraction(below, axis.SpanBelow) >> 1) >> 8;
        }
        var above = position - axis.Centre;
        if (position >= axis.Highest)
        {
            axis.Highest = position;
            axis.SpanAbove = (ushort)above;
            return FullDeflection;
        }
        // the same fraction, halved, its high byte: 0..127, rounded down towards the centre
        return (int)(Fraction(above, axis.SpanAbove) >> 1) >> 8;
    }

    /// <summary>distance / span as a 16-bit fraction (0..65535): the driver's division of the distance times 65536 by the span.</summary>
    private static uint Fraction(int distance, ushort span) => ((uint)distance << 16) / span;

    /// <summary>
    /// The grey cursor keys walk like the keypad's digits (Shift with them lunges). New code, a player option off by default
    /// (DIVERGENCE: D-610); the host sets it while the building raid runs, so no other part sees it.
    /// </summary>
    public bool ArrowKeysWalk { get; set; }

    /// <summary>Num Lock as the game last set it; on at start, as on the PCs of the time.</summary>
    private bool _numLock = true;

    /// <inheritdoc/>
    /// <remarks>New code: the host delivers finished key codes, so the setting is only kept for the game to read back.</remarks>
    public bool NumLock() => _numLock;

    /// <inheritdoc/>
    public void SetNumLock(bool on) => _numLock = on;

    /// <summary>Whether a key is there to be taken; asks the host for one when none is held yet.</summary>
    private bool KeyWaiting()
    {
        if (_waiting is null)
        {
            link.Pump();
            if (link.Host.Input.TryReadKey(out var key))
            {
                // a keypad key is a digit or a cursor key by the game's Num Lock, as the BIOS gives it
                _waiting = HostKeys.ResolveKeypad(HostKeys.ResolveArrow(key, ArrowKeysWalk), _numLock);
            }
            else
            {
                // a game that asks for a key again and again (a menu box waiting) still sees its screen shown, a change at once
                link.PresentIfChangedOrDue();
            }
        }
        return _waiting is not null;
    }
}
