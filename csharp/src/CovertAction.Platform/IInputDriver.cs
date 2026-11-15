namespace CovertAction.Platform;

/// <summary>
/// The keyboard and joystick as the game sees them: the calls into the original's small input driver (MISC.EXE),
/// and the two places where the game's code asks past that driver. The game's own code turns these raw readings into
/// its key codes and directions.
/// </summary>
public interface IInputDriver
{
    /// <summary>Whether the keyboard buffer is empty (true: <see cref="ReadKeyRaw"/> would have to wait).</summary>
    // orig: CODE 0000:0382 no_key_waiting thunk; also FINAL 0000:040e, TAC 0000:040e, BUG 0000:040e, GAME 0000:0382, CHASE 0000:0382
    bool NoKeyWaiting();

    /// <summary>Waits for a key and takes it from the keyboard buffer.</summary>
    /// <returns>The key as the BIOS gives it: the scan code times 256 plus the ASCII code (0 for a function or cursor key).</returns>
    // orig: CODE 0000:0389 read_key_raw thunk; also FINAL 0000:0415, TAC 0000:0415, BUG 0000:0415, GAME 0000:0389, CHASE 0000:0389
    int ReadKeyRaw();

    /// <summary>Measures the joystick's centre position; the game calls it when the player switches the joystick on.</summary>
    // orig: CODE 0000:03ac calibrate_joystick thunk; also FINAL 0000:0438, TAC 0000:0438, BUG 0000:0438, GAME 0000:03ac, CHASE 0000:03ac
    void CalibrateJoystick();

    /// <summary>Reads the joystick's position relative to its centre.</summary>
    /// <returns>Both axes in one 16-bit word: X in the low byte, Y in the high byte, each a signed byte (-128..127).</returns>
    // orig: CODE 0000:03b3 read_joystick_axes thunk; also FINAL 0000:043f, TAC 0000:043f, BUG 0000:043f, GAME 0000:03b3, CHASE 0000:03b3
    int ReadJoystickAxes();

    /// <summary>
    /// The joystick's buttons, as the PC's game port gives them: one bit per button from bit 4 (the first button) to
    /// bit 7, clear while the button is held down. The low four bits belong to the stick's axes and mean nothing here.
    /// The original reads the port itself, past the input driver.
    /// </summary>
    /// <returns>The eight bits; 0xFF without a joystick.</returns>
    int ReadJoystickButtons();

    /// <summary>
    /// Whether a key is waiting, asked through the C library (<c>kbhit</c>) instead of the input driver; the game
    /// uses it to end its blinking and scanning loops. Does not take the key.
    /// </summary>
    // orig: CODE 0000:31fe kbhit (SLIBCR.LIB dos\kbhit.asm) lib; also GAME 00f7:f60a, TAC 00e8:f322
    bool Kbhit();

    /// <summary>
    /// Waits for a key and takes it, asked through the C library (<c>getch</c>, DOS function 8) instead of the input
    /// driver; nothing is shown for it.
    /// </summary>
    /// <returns>The key's character. A function or cursor key comes in two calls: first 0, then its scan code.</returns>
    // orig: FINAL 0100:e28c getch (SLIBCR.LIB dos\getch.asm) lib; also GAME 00f7:f61e, TAC 00e8:f336
    int Getch();

    /// <summary>
    /// Whether the keyboard's Num Lock is on. The building raid reads its keys from the numeric pad and switches
    /// Num Lock off while a box is up and on again afterwards; the original does it by changing the PC's keyboard
    /// flags in memory, past every driver.
    /// </summary>
    bool NumLock();

    /// <summary>Switches the keyboard's Num Lock on or off (see <see cref="NumLock"/>).</summary>
    /// <param name="on">Whether it is to be on.</param>
    void SetNumLock(bool on);
}
