namespace CovertAction.Core.Raid;

/// <summary>What a person of the raid is doing (the flag word of <see cref="BadGuy"/>).</summary>
// orig: ACT_LEAVING .. ACT_ACTIVE (src/c/include/tac.h)
[Flags]
public enum ActorFlags : short
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>A guard is walking to the door of his room, to leave it.</summary>
    Leaving = 0x01,

    /// <summary>Just hit: the hit animation runs, then he is knocked out.</summary>
    Hit = 0x02,

    /// <summary>The player: the '5' key's slow, quiet step is on. A guard: he moves with small steps and looks harder (alert).</summary>
    Sneak = 0x04,

    /// <summary>The alert mark is shown over the guard.</summary>
    Alert = 0x08,

    /// <summary>The slot holds a person.</summary>
    Active = 0x80,
}

/// <summary>
/// One person in the building: slot 0 is the player, slots 1 to 8 the guards. Positions are pixels of the 640 x 512
/// pixel map (16 to a cell; a person spawns on a cell's centre, <c>cell * 16 + 8</c>).
/// </summary>
// orig: struct BadGuy, 28 bytes (src/c/include/tac.h); bg[9] at TAC DS:c870
public sealed class BadGuy
{
    /// <summary>+00 status, with the unused byte +01 above it: the <see cref="ActorFlags"/>.</summary>
    public ActorFlags Status { get; set; }

    /// <summary>+02 px: x in pixels.</summary>
    public short Px { get; set; }

    /// <summary>+04 py: y in pixels.</summary>
    public short Py { get; set; }

    /// <summary>+06 destx: the column of the cell a guard walks to; -1 for none.</summary>
    public short Destx { get; set; }

    /// <summary>+08 desty: the row of that cell.</summary>
    public short Desty { get; set; }

    /// <summary>+0a pose: the animation frame (a guard's walk cycle 0..3).</summary>
    public short Pose { get; set; }

    /// <summary>+0c dir: the facing, 1 north .. 8 north-west, clockwise.</summary>
    public short Dir { get; set; }

    /// <summary>+0e mode: the player's state (0 standing, 1 walking, 2 the arrest prompt, 4 a lunge); 0 for a guard.</summary>
    public short Mode { get; set; }

    /// <summary>+10 fire: the firing timer: while it runs the person is drawn aiming.</summary>
    public short Fire { get; set; }

    /// <summary>+12 room: the room the person is in.</summary>
    public short Room { get; set; }

    /// <summary>+14 pause: frames a guard still waits before he moves on.</summary>
    public short Pause { get; set; }

    /// <summary>+16 sighting: how long the guard has watched the player; above a limit set by the difficulty he raises the alarm and shoots.</summary>
    public short Sighting { get; set; }

    /// <summary>+18 sighted: frames in a row the player has been in the guard's sight.</summary>
    public short Sighted { get; set; }

    /// <summary>+1a stun: the knocked-out timer: above 0x20 he lies down, 999 and more he is out for good (9999 dead).</summary>
    public short Stun { get; set; }
}
