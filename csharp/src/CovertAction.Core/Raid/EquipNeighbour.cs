namespace CovertAction.Core.Raid;

/// <summary>
/// One row of the equipment screen's cursor table: the item the cursor reaches from this item with each arrow key.
/// </summary>
// orig: struct nbr (src/c/include/tac.h), read by equipment_screen (src/c/tac/tac_00e8_79ea.c)
public sealed class EquipNeighbour
{
    /// <summary>+00 up: the item above.</summary>
    public short Up { get; set; }

    /// <summary>+02 down: the item below.</summary>
    public short Down { get; set; }

    /// <summary>+04 left: the item to the left.</summary>
    public short Left { get; set; }

    /// <summary>+06 right: the item to the right.</summary>
    public short Right { get; set; }
}
