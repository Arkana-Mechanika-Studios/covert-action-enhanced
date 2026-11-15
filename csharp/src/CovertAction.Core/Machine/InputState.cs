namespace CovertAction.Core.Machine;

/// <summary>
/// The variables of the joystick and mouse routines every module carries: the last joystick reading, the table that
/// turns it into a direction, and what the mouse driver last answered.
/// </summary>
// orig: CODE DS:11bc, DS:1b70, DS:1db4..DS:1dbb (src/c/include/code.h: joystick_dir_table, joystick_raw, mouse_*)
public sealed class InputState
{
    /// <summary>
    /// CODE DS:11bc: the direction code for each stick position, indexed by 3 * horizontal + vertical + 4, where
    /// horizontal and vertical are -1, 0 or 1 (the original indexes the table from its middle, DS:11c4). The codes run
    /// clockwise from 1 (up) to 8; the middle entry, the stick at rest, is -1.
    /// </summary>
    public short[] JoystickDirTable { get; } = new short[9];

    /// <summary>CODE DS:1b70: the last joystick reading: X in the low byte, Y in the high byte, each a signed byte.</summary>
    public short JoystickRaw { get; set; }

    /// <summary>CODE DS:1db4: the mouse buttons as the driver last reported them.</summary>
    public short MouseButtons { get; set; }

    /// <summary>CODE DS:1db6: the mouse's horizontal position as last reported.</summary>
    public short MouseX { get; set; }

    /// <summary>CODE DS:1db8: the mouse's vertical position as last reported.</summary>
    public short MouseY { get; set; }

    /// <summary>CODE DS:1dba: how often a button was pressed or released since the last query.</summary>
    public short MousePressCount { get; set; }
}
