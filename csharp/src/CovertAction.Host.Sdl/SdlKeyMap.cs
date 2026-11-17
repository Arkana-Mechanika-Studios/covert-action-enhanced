// Reused from the Darklands Restoration Project, src/Darklands.Host.Sdl/SdlKeyMap.cs; same author.
// Adapted: the namespace; Keys is HostKeys here.
using CovertAction.Platform;
using CovertAction.Settings;

namespace CovertAction.Host.Sdl;

/// <summary>Translates SDL keyboard events (USB scancodes) into game key codes.</summary>
internal static class SdlKeyMap
{
    private const int ModShift = SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift;
    private const int ModCtrl = SdlNative.KeyModLeftCtrl | SdlNative.KeyModRightCtrl;
    private const int ModAlt = 0x0100 | 0x0200;

    // SDL scancode → PC scan code for keys without a character.
    private static readonly Dictionary<int, int> Special = new()
    {
        [40] = HostKeys.Enter, [41] = HostKeys.Escape, [42] = HostKeys.Backspace, [43] = HostKeys.Tab, [44] = HostKeys.Space,
        [82] = HostKeys.Up, [81] = HostKeys.Down, [80] = HostKeys.Left, [79] = HostKeys.Right,
        [74] = HostKeys.Home, [77] = HostKeys.End, [75] = HostKeys.PageUp, [78] = HostKeys.PageDown,
        [88] = HostKeys.Enter,                                  // keypad Enter
        // the keypad's digit keys: the keyboard driver gives their digit or their cursor key by Num Lock (HostKeys.Keypad)
        [96] = HostKeys.Up | HostKeys.Keypad, [90] = HostKeys.Down | HostKeys.Keypad, [92] = HostKeys.Left | HostKeys.Keypad,
        [94] = HostKeys.Right | HostKeys.Keypad, [95] = HostKeys.Home | HostKeys.Keypad, [89] = HostKeys.End | HostKeys.Keypad,
        [97] = HostKeys.PageUp | HostKeys.Keypad, [91] = HostKeys.PageDown | HostKeys.Keypad, [93] = HostKeys.Center | HostKeys.Keypad,
        [98] = HostKeys.Insert | HostKeys.Keypad, [99] = HostKeys.Delete | HostKeys.Keypad,
    };

    /// <summary>
    /// The game key of a key press with the player's key remapping applied first (D-671): a mapped key arrives as the key of the
    /// original it is mapped to. With no mapping this is <see cref="ToGameKey(int, int, int, int)"/>.
    /// </summary>
    // DIVERGENCE: D-671 a player option (input.keyMap), empty by default: the key the game receives is the one the player mapped
    public static int ToGameKey(KeyRemap remap, int scancode, int sym, int mod, int shifted = 0)
    {
        var (code, symbol, shift) = remap.Apply(scancode, sym, shifted);
        return ToGameKey(code, symbol, mod, shift);
    }

    /// <summary>
    /// The game key of a gamepad button's mapped key (D-672): the key of the original pressed with no modifier.
    /// </summary>
    // DIVERGENCE: D-672 a player option (input.padMap), empty by default: a pad button beyond the joystick's four types a key
    public static int PadGameKey(int scancode) => ToGameKey(scancode, KeyBindings.UsSymbol(scancode), 0);

    /// <summary>
    /// The game key of a key press: <paramref name="sym"/> is the layout's unshifted key (the key events' symbol), <paramref name="shifted"/>
    /// the layout's key with Shift (0 when Shift is not held). With Shift a symbol key gives the layout's shifted character, as the DOS
    /// keyboard driver gives it (AZERTY's '=' key: '+', its '&amp;' key: '1'; US QWERTY's '=' key: '+'); a letter gives its capital and a
    /// digit itself, as before. The keypad's minus and plus give the BIOS's words (4A2Dh, 4E2Bh).
    /// </summary>
    // orig: root 047C:0744 (read-key: the BIOS word of INT 16h, the scan code high, the character low)
    // DIVERGENCE: host: SDL3's key events give the unshifted key whatever the layout (SDL_KEYCODE_OPTIONS "none"), so the shifted
    //   character is looked up from the layout (SdlHost.ShiftedKey); under DOS the BIOS with the layout's driver gives the shifted
    //   character with the US key's scan code for the US characters (HostKeys.ForCharacter), whatever key the layout has it on.
    public static int ToGameKey(int scancode, int sym, int mod, int shifted = 0)
    {
        if (scancode is 86 or 87)                         // keypad minus, keypad plus
        {
            var word = scancode == 86 ? 0x4A2D : 0x4E2B;
            return (mod & ModAlt) != 0 ? word & 0xFF00 : word;
        }
        if (scancode is >= 58 and <= 67)                  // F1..F10
        {
            var n = scancode - 58;
            if ((mod & ModCtrl) != 0 && n < 5)
            {
                return 0x5E00 + (n << 8);                  // Ctrl-F1..F5 make a member the leader
            }
            return HostKeys.F1 + (n << 8);
        }
        if (scancode is 68 or 69)                         // F11, F12
        {
            return scancode == 68 ? HostKeys.F11 : HostKeys.F12;
        }
        if (Special.TryGetValue(scancode, out var special))
        {
            // the grey cursor keys carry marks the keyboard driver reads for the player option "arrow keys walk" (D-610);
            // it takes them off again, so without the option the key is the plain code
            return scancode is 82 or 81 or 80 or 79 or 74 or 77 or 75 or 78
                ? special | HostKeys.GreyArrow | ((mod & ModShift) != 0 ? HostKeys.ShiftHeld : 0)
                : special;
        }
        if (sym is < 32 or > 126)
        {
            return HostKeys.None;
        }
        var c = (char)sym;
        if ((mod & ModAlt) != 0)
        {
            return HostKeys.ForCharacter(c) & 0xFF00;         // Alt keys carry no character
        }
        if ((mod & ModShift) != 0)
        {
            c = char.IsAsciiLetterOrDigit(c) || shifted is < 32 or > 126 ? char.ToUpperInvariant(c) : (char)shifted;
        }
        return HostKeys.ForCharacter(c);
    }
}
