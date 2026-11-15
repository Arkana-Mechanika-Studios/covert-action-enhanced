namespace CovertAction.Core.Electronics;

/// <summary>
/// What one 8x8-pixel cell of the circuit board holds: the pieces of wire that leave it, one bit per compass
/// direction, and whether current flows through it. A straight wire across a cell is two pieces (west and east); a
/// bend is two pieces at an angle. The directions are those of <see cref="ElectronicsState.BUGMoveX"/> and
/// <see cref="ElectronicsState.BUGMoveY"/>: bit n is the piece towards neighbour n.
/// </summary>
// orig: BUG DS:843c board, the WIRE_* bits (src/c/include/bug.h)
[Flags]
public enum Wire : short
{
    /// <summary>An empty cell.</summary>
    None = 0,

    /// <summary>A piece going up.</summary>
    North = 0x01,

    /// <summary>A piece going up and right.</summary>
    NorthEast = 0x02,

    /// <summary>A piece going right.</summary>
    East = 0x04,

    /// <summary>A piece going down and right.</summary>
    SouthEast = 0x08,

    /// <summary>A piece going down.</summary>
    South = 0x10,

    /// <summary>A piece going down and left.</summary>
    SouthWest = 0x20,

    /// <summary>A piece going left.</summary>
    West = 0x40,

    /// <summary>A piece going up and left.</summary>
    NorthWest = 0x80,

    /// <summary>Current flows through the cell: its pieces are drawn bright.</summary>
    Powered = 0x100,
}
