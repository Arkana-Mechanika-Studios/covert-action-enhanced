namespace CovertAction.Core.Raid;

/// <summary>A place on the 320 x 200 screen, from a table of the status panel's layout.</summary>
// orig: struct pos (src/c/include/tac.h)
public sealed class ScreenPoint
{
    /// <summary>+00 x: pixels from the left.</summary>
    public short X { get; set; }

    /// <summary>+02 y: pixels from the top.</summary>
    public short Y { get; set; }
}

/// <summary>A rectangle of the 320 x 200 screen or of a picture, given by its two corners (both inside it).</summary>
// orig: struct rect (src/c/include/tac.h)
public sealed class ScreenArea
{
    /// <summary>+00 x1: the left column.</summary>
    public short X1 { get; set; }

    /// <summary>+02 y1: the top row.</summary>
    public short Y1 { get; set; }

    /// <summary>+04 x2: the right column.</summary>
    public short X2 { get; set; }

    /// <summary>+06 y2: the bottom row.</summary>
    public short Y2 { get; set; }

    /// <summary>The width in pixels, the corners included.</summary>
    public int Width => X2 - X1 + 1;

    /// <summary>The height in pixels, the corners included.</summary>
    public int Height => Y2 - Y1 + 1;
}

/// <summary>The bits of <see cref="RaidState.Equip"/>: the items chosen on the equipment screen.</summary>
// orig: EQ_UZI .. EQ_FLOORPLAN (src/c/include/tac.h)
public static class Equipment
{
    /// <summary>The camera: photographs (F2) can be taken, and the film counter is shown.</summary>
    public const int Camera = 0x0004;

    /// <summary>The bugs, whose icons are drawn by count, not as an item.</summary>
    public const int BugsBit = 3;

    /// <summary>The Uzi: a better pistol. The equipment screen keeps the lowest bit as its opposite (a plain pistol).</summary>
    public const int Uzi = 0x0002;

    /// <summary>The bits of the grenade kinds: kind <c>n</c> in full (8 of them) when bit <c>GrenadeKinds + n</c> is set.</summary>
    public const int GrenadeKinds = 8;

    /// <summary>The bit that gives every kind of grenade two rounds.</summary>
    public const int SpareGrenades = 11;

    /// <summary>The bits the equipment screen keeps when it works out the kit: bit 15 (the floor plan) is not among them.</summary>
    public const int ChosenMask = 0x7ffe;

    /// <summary>The detector: the radar sees farther.</summary>
    public const int Detector = 0x0080;

    /// <summary>The floor plan: the radar shows every room, visited or not (nothing in the game sets it).</summary>
    public const int FloorPlan = unchecked((short)0x8000);
}
