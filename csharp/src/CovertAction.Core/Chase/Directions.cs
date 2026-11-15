namespace CovertAction.Core.Chase;

/// <summary>
/// The four headings of the car chase, as the original numbers them. A heading is also the number of a side of a map
/// cell (0 the top side, 1 the right, ...) and the index into the step tables <see cref="ChaseState.DirDx"/> and
/// <see cref="ChaseState.DirDy"/>.
/// </summary>
// orig: DIR_UP .. DIR_LEFT (src/c/include/chase.h)
public static class Directions
{
    /// <summary>Up the screen (y falls).</summary>
    public const int Up = 0;

    /// <summary>To the right (x grows).</summary>
    public const int Right = 1;

    /// <summary>Down the screen (y grows).</summary>
    public const int Down = 2;

    /// <summary>To the left (x falls).</summary>
    public const int Left = 3;

    /// <summary>The value of a car's pending turn when the player has asked for none.</summary>
    public const int None = -1;
}
