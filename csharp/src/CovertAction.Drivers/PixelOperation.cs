namespace CovertAction.Drivers;

/// <summary>
/// How the graphics driver combines a line's pixel with the pixel already on the page. The game chooses it with
/// the high byte of the colour it sets (<see cref="McgaGraphicsDriver.SetColor"/>); the original driver does it by
/// rewriting one instruction of its line routine (MOV, AND, OR or XOR; the four instruction bytes stand in front of
/// its set_color routine, indexed by the operation).
/// </summary>
public enum PixelOperation
{
    /// <summary>The line's colour replaces the pixel.</summary>
    Replace = 0,

    /// <summary>The pixel keeps only the bits it shares with the colour.</summary>
    And = 1,

    /// <summary>The colour's bits are added to the pixel's.</summary>
    Or = 2,

    /// <summary>The colour's bits flip the pixel's: drawing the same line twice restores the picture.</summary>
    Xor = 3,
}
