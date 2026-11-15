namespace CovertAction.Presentation;

/// <summary>
/// The game's key codes, as its key reader returns them: the character itself for a key that types one ('a', Esc,
/// Return), and the keyboard's scan code times 256 for a key that types none (function keys, cursor keys, Alt
/// combinations).
/// </summary>
// orig: the KEY_* constants of src/c/include/code.h
public static class Keys
{
    /// <summary>Esc.</summary>
    public const int Esc = 0x1b;

    /// <summary>Return.</summary>
    public const int Return = 0x0d;

    /// <summary>The space bar.</summary>
    public const int Space = 0x20;

    /// <summary>F1.</summary>
    public const int F1 = 0x3b00;

    /// <summary>F10.</summary>
    public const int F10 = 0x4400;

    /// <summary>Alt-Q: quit the game.</summary>
    public const int AltQ = 0x1000;

    /// <summary>Alt-P: pause.</summary>
    public const int AltP = 0x1900;

    /// <summary>Alt-A: animations on or off.</summary>
    public const int AltA = 0x1e00;

    /// <summary>Alt-S: sound on or off.</summary>
    public const int AltS = 0x1f00;

    /// <summary>Alt-J: joystick on or off.</summary>
    public const int AltJ = 0x2400;

    /// <summary>Cursor up.</summary>
    public const int Up = 0x4800;

    /// <summary>Cursor down.</summary>
    public const int Down = 0x5000;
}
