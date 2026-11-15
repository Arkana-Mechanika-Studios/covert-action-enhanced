namespace CovertAction.Presentation;

/// <summary>The numbers of the sound effects the game's code asks the sound driver for (those known so far).</summary>
// orig: the FX_* constants of src/c/include/code.h
public static class Sounds
{
    /// <summary>An option (sound, animations, joystick) was switched off.</summary>
    public const int OptionOff = 0x13;

    /// <summary>An option was switched on.</summary>
    public const int OptionOn = 0x14;

    /// <summary>Cryptography: a letter was assigned.</summary>
    public const int LetterSet = 0x2d;

    /// <summary>Cryptography: a hint is being worked out, or the message is decoded.</summary>
    public const int HintDone = 0x2e;
}
