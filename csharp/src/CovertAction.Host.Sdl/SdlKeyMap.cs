// Reused from the Darklands Restoration Project, src/Darklands.Host.Sdl/SdlKeyMap.cs; same author.
// Adapted: the namespace; Keys is HostKeys here.
using CovertAction.Platform;

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
            return special;
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
