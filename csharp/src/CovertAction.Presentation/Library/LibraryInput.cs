using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The input routines every part of the game has: reading a key as one code, the joystick used as cursor keys,
/// and the mouse driver calls. The original's programs each carry a copy; where the copies are the same
/// instructions (docs/source-map/shared-functions.csv) this one implementation stands for all of them, and its
/// comment lists them. The cryptography part calls none of the mouse routines.
/// </summary>
/// <param name="state">The part's variables.</param>
/// <param name="world">The drivers and the machine.</param>
public sealed class LibraryInput(ILibraryState state, Boundaries world)
{
    /// <summary>How far from the centre (of -128..127) the stick must be to count as pushed.</summary>
    private const int StickThreshold = 0x60;

    /// <summary>The joystick's direction codes run clockwise from 1 (up) to 8 (up and left).</summary>
    private const int DirectionUpRight = 2, DirectionUp = 1, DirectionUpLeft = 8, DirectionDownRight = 4, DirectionDown = 5, DirectionDownLeft = 6;

    /// <summary>
    /// The forms in which the keys of the cursor block arrive from the keyboard driver (plain, with Shift, with Num
    /// Lock, from the numeric pad), each with the one key code the game uses for it: the cursor keys become the plain
    /// cursor codes, the pad's digits keep a code of their own.
    /// </summary>
    private static readonly Dictionary<int, int> CursorBlockKeys = new()
    {
        [0x4800] = Keys.Up, [0x487e] = Keys.Up,
        [0x4900] = 0x4900,                          // page up
        [0xf400] = 0x4d00, [0x4d00] = 0x4d00,       // right
        [0x5100] = 0x5100,                          // page down
        [0x5000] = Keys.Down, [0x5060] = Keys.Down,
        [0x4f00] = 0x4f00,                          // end
        [0x4b00] = 0x4b00, [0x4b7c] = 0x4b00,       // left
        [0x4700] = 0x4700, [0x475c] = 0x4700,       // home
        [0x4838] = 0x4838,                          // pad 8
        [0x4939] = 0x4939,                          // pad 9
        [0x4d36] = 0x4d36, [0x4d46] = 0x4d36,       // pad 6
        [0x5133] = 0x5133,                          // pad 3
        [0x5032] = 0x5032,                          // pad 2
        [0x4f31] = 0x4f31,                          // pad 1
        [0x4b34] = 0x4b34, [0x4b43] = 0x4b34,       // pad 4
        [0x4737] = 0x4737,                          // pad 7
    };

    /// <summary>
    /// Waits for a key and returns its code (see <see cref="Keys"/>): the character for a key that types one, the
    /// scan code times 256 for the others, with the cursor block's many forms folded into one code each.
    /// </summary>
    // orig: CODE 0000:2412 read_key (src/c/code/code_0000_2412.c) matched; also FINAL 0100:99b8, BUG 00e8:62f6, CHASE 00df:3c60, GAME 00f7:d0f2; tests: CodeOracleTests
    // Other code under the same name: TAC 00e8:c52a (not this method; docs/source-map/shared-functions.csv).
    public int ReadKey()
    {
        var raw = world.Input.ReadKeyRaw() & 0xffff;
        if (CursorBlockKeys.TryGetValue(raw, out var key))
        {
            return key;
        }
        var character = raw & 0xff;
        return character != 0 ? character : raw;
    }

    /// <summary>
    /// Throws away every key waiting in the keyboard buffer; then, with the joystick option on, waits until neither
    /// joystick button is held down.
    /// </summary>
    // orig: CODE 0000:256c flush_input (src/c/code/code_0000_256c.c) matched; also FINAL 0100:9b12, GAME 00f7:d24c, TAC 00e8:c684, BUG 00e8:6450, CHASE 00df:3dba; tests: CodeOracleTests
    public void FlushInput()
    {
        while (!world.Input.NoKeyWaiting())
        {
            ReadKey();
        }
        while (state.Status.HasFlag(Options.Joystick) && (JoystickButtonDown(0) || JoystickButtonDown(1)))
        {
        }
    }

    /// <summary>The joystick as a keyboard: the key its position and buttons stand for right now.</summary>
    /// <returns>
    /// Cursor up for the stick pushed up (straight or diagonally), cursor down for down, Return for the first button,
    /// Esc for the second (a button wins over the stick, the second over the first); -1 when nothing is pressed or the
    /// joystick option is off.
    /// </returns>
    // orig: CODE 0000:259f poll_joystick_key (src/c/code/code_0000_259f.c) matched; also FINAL 0100:9b45, TAC 00e8:c6b7, BUG 00e8:6483, CHASE 00df:3ded, GAME 00f7:d27f; tests: CodeOracleTests
    public int PollJoystickKey()
    {
        if (!state.Status.HasFlag(Options.Joystick))
        {
            return -1;
        }
        int horizontal = 0, vertical = 0;
        var key = ReadJoystickDir(ref horizontal, ref vertical) switch
        {
            DirectionUp or DirectionUpRight or DirectionUpLeft => Keys.Up,
            DirectionDown or DirectionDownRight or DirectionDownLeft => Keys.Down,
            _ => -1,
        };
        if (JoystickButtonDown(0))
        {
            key = Keys.Return;
        }
        if (JoystickButtonDown(1))
        {
            key = Keys.Esc;
        }
        return key;
    }

    /// <summary>Reads the joystick and tells which way it is pushed.</summary>
    /// <param name="dx">Receives -1 for left, 1 for right, 0 for neither. Left as it is when the joystick option is off.</param>
    /// <param name="dy">Receives -1 for up, 1 for down, 0 for neither. Left as it is when the joystick option is off.</param>
    /// <returns>The direction code of the position, 1 (up) clockwise to 8, -1 for the stick at rest or the joystick option off.</returns>
    // orig: CODE 0000:261c read_joystick_dir (src/c/code/code_0000_261c.c) matched; also FINAL 0100:9bc2, TAC 00e8:c734, BUG 00e8:6500, CHASE 00df:3e6a, GAME 00f7:d2fc; tests: CodeOracleTests
    public int ReadJoystickDir(ref int dx, ref int dy)
    {
        if (!state.Status.HasFlag(Options.Joystick))
        {
            return -1;
        }
        var reading = state.Input.JoystickRaw = (short)world.Input.ReadJoystickAxes();
        // the reading packs two signed bytes: X in the low byte, Y in the high byte
        dx = Pushed((sbyte)reading);
        dy = Pushed((sbyte)(reading >> 8));
        // the table has one entry per position, three for each column from left to right; its middle entry is "at rest"
        const int middle = 4;
        return state.Input.JoystickDirTable[middle + 3 * dx + dy];
    }

    /// <summary>-1, 0 or 1 for an axis reading: beyond the threshold on the low side, near the centre, beyond it on the high side.</summary>
    private static int Pushed(int axis) => axis > StickThreshold ? 1 : axis < -StickThreshold ? -1 : 0;

    /// <summary>Whether a joystick button is held down right now.</summary>
    /// <param name="button">0 for the first button, 1 for the second; only the low byte is used.</param>
    // orig: CODE 0000:2698 joystick_button_down (src/c/code/code_0000_2698.c) matched; also FINAL 0100:9c3e, GAME 00f7:d378, TAC 00e8:c7b0, BUG 00e8:657c, CHASE 00df:3ee6; tests: CodeOracleTests
    public bool JoystickButtonDown(int button)
    {
        // the buttons are bits 4 and 5 of the reading, clear while held down
        // C: the button number is a signed char, and the shift count (button + 4) goes to the processor as it is; C16.Sar
        // shifts as the 8086 does. For the two real buttons this is plainly bit 4 or bit 5.
        var bit = C16.Sar((short)world.Input.ReadJoystickButtons(), (sbyte)button + 4) & 1;
        return bit == 0;
    }

    /// <summary>Resets the mouse driver.</summary>
    // orig: CODE 0000:27f0 mouse_reset (src/c/code/code_0000_27ee.c) matched; also FINAL 0100:bed0, CHASE 00df:403e; tests: CodeOracleTests
    // DORMANT: no caller in CODE, like every mouse routine below.
    public void MouseReset() => world.Mouse.Reset();

    /// <summary>Shows the mouse pointer.</summary>
    // orig: CODE 0000:2806 mouse_show_cursor (src/c/code/code_0000_27ee.c) matched; also FINAL 0100:bee6, CHASE 00df:4054; tests: CodeOracleTests
    public void MouseShowCursor() => world.Mouse.ShowCursor();

    /// <summary>Hides the mouse pointer.</summary>
    // orig: CODE 0000:281c mouse_hide_cursor (src/c/code/code_0000_27ee.c) matched; also FINAL 0100:befc, CHASE 00df:406a; tests: CodeOracleTests
    public void MouseHideCursor() => world.Mouse.HideCursor();

    /// <summary>Reads the buttons and the pointer's position into the mouse variables.</summary>
    /// <returns>The pointer's vertical position.</returns>
    // orig: CODE 0000:2832 mouse_get_status (src/c/code/code_0000_27ee.c) matched; also FINAL 0100:bf12, CHASE 00df:4080; tests: CodeOracleTests
    public int MouseGetStatus()
    {
        var status = world.Mouse.GetStatus();
        state.Input.MouseButtons = (short)status.Buttons;
        state.Input.MouseX = (short)status.X;
        return state.Input.MouseY = (short)status.Y;
    }

    /// <summary>Moves the pointer.</summary>
    /// <param name="x">Horizontal position.</param>
    /// <param name="y">Vertical position.</param>
    // orig: CODE 0000:285a mouse_set_position (src/c/code/code_0000_285a.c) matched; also FINAL 0100:bf3a, CHASE 00df:40a8; tests: CodeOracleTests
    public void MouseSetPosition(int x, int y) => world.Mouse.SetPosition(x, y);

    /// <summary>How often a button was pressed since the last query, and where the pointer was at the last press; both go to the mouse variables.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    /// <returns>The vertical position at the last press.</returns>
    // orig: CODE 0000:2880 mouse_button_press (src/c/code/code_0000_2880.c) matched; also FINAL 0100:bf60, CHASE 00df:40ce; tests: CodeOracleTests
    public int MouseButtonPress(int button) => Keep(world.Mouse.ButtonPresses(button));

    /// <summary>The same for releases of a button.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    /// <returns>The vertical position at the last release.</returns>
    // orig: CODE 0000:28b8 mouse_button_release (src/c/code/code_0000_28b8.c) matched; also FINAL 0100:bf98, CHASE 00df:4106; tests: CodeOracleTests
    public int MouseButtonRelease(int button) => Keep(world.Mouse.ButtonReleases(button));

    /// <summary>Limits the pointer to a rectangle.</summary>
    /// <param name="xmin">Left limit.</param>
    /// <param name="ymin">Top limit.</param>
    /// <param name="xmax">Right limit.</param>
    /// <param name="ymax">Bottom limit.</param>
    // orig: CODE 0000:28f0 mouse_set_range (src/c/code/code_0000_28f0.c) matched; also FINAL 0100:bfd0, CHASE 00df:413e; tests: CodeOracleTests
    public void MouseSetRange(int xmin, int ymin, int xmax, int ymax)
    {
        world.Mouse.SetHorizontalRange(xmin, xmax);
        world.Mouse.SetVerticalRange(ymin, ymax);
    }

    /// <summary>The driver's units across for one game pixel (int 33h counts 640 across in the 320-pixel modes).</summary>
    private const int MouseUnitsPerPixel = 2;

    /// <summary>The pointer's last position a menu saw (pixels of the screen), to tell a move from a pointer at rest.</summary>
    private (int X, int Y) _pointerSeen;

    /// <summary>
    /// Whether the player's mouse works in the menus and lists: the mouse option is on (a mouse driver with a mouse).
    /// Without it no menu asks the mouse driver anything, so the game makes exactly the original's calls.
    /// </summary>
    /// <remarks>New code (csharp/docs/MOUSE-SUPPORT.md; D-630).</remarks>
    public bool PointerOn => world.Mouse.Installed;

    /// <summary>
    /// A menu or list starts waiting: the presses made before it are forgotten (as <see cref="FlushInput"/> forgets the
    /// keys) and the pointer's place now counts as at rest, so the highlight stays where the menu puts it until the
    /// mouse moves.
    /// </summary>
    /// <remarks>New code (D-630). Does nothing without the mouse option.</remarks>
    // DIVERGENCE: D-630 the mouse in the menus and lists is a player option; the original's menus never read the mouse.
    public void BeginPointer()
    {
        if (!PointerOn)
        {
            return;
        }
        world.Mouse.ButtonPresses(0);
        world.Mouse.ButtonPresses(1);
        _pointerSeen = PointerPixel(world.Mouse.GetStatus());
    }

    /// <summary>
    /// What the mouse did since the menu last asked: a right click (Esc), a left click on a choice or beside every choice
    /// (Return), or a move onto a choice (the highlight follows). A pointer at rest does nothing, so the cursor keys keep
    /// working beside it.
    /// </summary>
    /// <param name="choiceAt">The menu's hit test: the choice under a pixel of the screen, -1 for none.</param>
    /// <remarks>New code (D-630). Without the mouse option: always nothing, and no call of the mouse driver.</remarks>
    public PointerEvent ReadPointer(Func<int, int, int> choiceAt)
    {
        if (!PointerOn)
        {
            return PointerEvent.Nothing;
        }
        if (world.Mouse.ButtonPresses(1).Count > 0)
        {
            world.Mouse.ButtonPresses(0);
            return new PointerEvent(PointerAction.Cancel, -1);
        }
        var left = world.Mouse.ButtonPresses(0);
        if (left.Count > 0)
        {
            var (x, y) = (left.X / MouseUnitsPerPixel, left.Y);
            _pointerSeen = (x, y);
            return new PointerEvent(PointerAction.Accept, choiceAt(x, y));
        }
        var place = PointerPixel(world.Mouse.GetStatus());
        if (place == _pointerSeen)
        {
            return PointerEvent.Nothing;
        }
        _pointerSeen = place;
        var choice = choiceAt(place.X, place.Y);
        return choice < 0 ? PointerEvent.Nothing : new PointerEvent(PointerAction.Point, choice);
    }

    /// <summary>
    /// A "press a key" wait's reading of the mouse: a left click stands for Space, a right click for Esc; a move is
    /// nothing.
    /// </summary>
    /// <returns>The key a click stands for, or -1.</returns>
    /// <remarks>New code (D-630). Without the mouse option: -1, and no call of the mouse driver.</remarks>
    public int ReadPointerKey() => ReadPointer(static (_, _) => -1).Action switch
    {
        PointerAction.Accept => Keys.Space,
        PointerAction.Cancel => Keys.Esc,
        _ => -1,
    };

    /// <summary>
    /// Whether the left button is held down, and where the pointer is (pixels of the screen): the building raid walks the
    /// agent towards it while it is held.
    /// </summary>
    /// <remarks>New code (D-636). Without the mouse option: not held, and no call of the mouse driver.</remarks>
    public (bool Held, int X, int Y) ReadPointerHold()
    {
        if (!PointerOn)
        {
            return (false, 0, 0);
        }
        var status = world.Mouse.GetStatus();
        var (x, y) = PointerPixel(status);
        return ((status.Buttons & 1) != 0, x, y);
    }

    /// <summary>Whether the right button was pressed since the last look (the presses are taken).</summary>
    /// <remarks>New code (D-636). Without the mouse option: false, and no call of the mouse driver.</remarks>
    public bool ReadPointerRightPress() => PointerOn && world.Mouse.ButtonPresses(1).Count > 0;

    /// <summary>Whether the car chase takes the mouse: the chase option (D-670) and the mouse itself are on.</summary>
    /// <remarks>New code (D-670).</remarks>
    public bool ChasePointerOn => world.MouseChase && PointerOn;

    /// <summary>
    /// The button press since the last look, the right button first (the left press is dropped with it), or null: the car
    /// chase turns it into the key it stands for.
    /// </summary>
    /// <remarks>New code (D-670). Without the mouse option: null, and no call of the mouse driver.</remarks>
    public PointerClick? ReadPointerClick()
    {
        if (!PointerOn)
        {
            return null;
        }
        var right = world.Mouse.ButtonPresses(1);
        if (right.Count > 0)
        {
            world.Mouse.ButtonPresses(0);
            return new PointerClick(true, right.X / MouseUnitsPerPixel, right.Y);
        }
        var left = world.Mouse.ButtonPresses(0);
        return left.Count > 0 ? new PointerClick(false, left.X / MouseUnitsPerPixel, left.Y) : null;
    }

    /// <summary>The screen pixel of the mouse driver's position.</summary>
    private static (int X, int Y) PointerPixel(Platform.MouseStatus status) => (status.X / MouseUnitsPerPixel, status.Y);

    /// <summary>Stores the answer to a button query in the mouse variables.</summary>
    private int Keep(Platform.MouseButtonEvents events)
    {
        state.Input.MouseButtons = (short)events.Buttons;
        state.Input.MousePressCount = (short)events.Count;
        state.Input.MouseX = (short)events.X;
        return state.Input.MouseY = (short)events.Y;
    }
}
