namespace CovertAction.Core.Shared;

/// <summary>
/// The option and campaign flags (<see cref="SharedState.Flags"/>, and each part's own copy of them). The player
/// switches sound, animations and joystick with Alt-S, Alt-A and Alt-J in any message box.
/// </summary>
// orig: SHARED+0x24 flags, the OPT_* bits (src/c/include/state.h)
[Flags]
public enum Options : short
{
    /// <summary>No flag set.</summary>
    None = 0,

    /// <summary>The agent is Maxine, not Max.</summary>
    Female = 0x02,

    /// <summary>Sound effects and music are on.</summary>
    Sound = 0x04,

    /// <summary>The picture animations are played.</summary>
    Animation = 0x08,

    /// <summary>The joystick is read as well as the keyboard.</summary>
    Joystick = 0x10,

    /// <summary>A practice mission started from the menus: the part returns to the menus, not to the campaign.</summary>
    Practice = 0x20,

    /// <summary>The save was made from the menus between two cases.</summary>
    SavedInFinal = 0x40,

    /// <summary>The copy-protection question was answered.</summary>
    Cleared = 0x80,
}
